using System.Collections.ObjectModel;
using Avalonia;
using Uno.Controls;
using Uno.Controls.Models.TreeDataGrid;
using Uno.Controls.Selection;
using DevToolsUno.Diagnostics.Internal;
using Microsoft.UI.Xaml;
using IndexPath = Uno.Controls.IndexPath;
using AGridLength = Avalonia.GridLength;
using AGridUnitType = Avalonia.GridUnitType;

namespace DevToolsUno.Diagnostics.ViewModels;

internal sealed class MemoryPageViewModel : ViewModelBase, IDisposable
{
    private const int MaxHistorySamples = 120;
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(1);

    private readonly FrameworkElement _root;
    private readonly DispatcherTimer _timer;
    private readonly List<MemorySampleViewModel> _history = [];
    private readonly List<TrackedMemoryReference> _trackedReferences = [];
    private MemorySnapshot? _currentSnapshot;
    private MemorySampleViewModel? _selectedSample;
    private MemoryInspectionItemViewModel? _selectedDetailItem;
    private MemoryTrackedObjectViewModel? _selectedTrackedObject;
    private MemoryObjectDetailsViewModel? _details;
    private DependencyObject? _inspectionTarget;
    private bool _includeClrProperties;
    private bool _isSampling = true;
    private int _sampleCount;
    private int _trackedCount;
    private int _trackingSequence;
    private MemoryDetailsMode _detailsMode = MemoryDetailsMode.SampleFacts;
    private string? _pendingTrackedSelectionId;

    public MemoryPageViewModel(FrameworkElement root, bool includeClrProperties)
    {
        _root = root;
        _includeClrProperties = includeClrProperties;

        RefreshCommand = new RelayCommand(() => TakeSample(recordHistory: true));
        ToggleSamplingCommand = new RelayCommand(ToggleSampling);
        ForceGcCommand = new RelayCommand(ForceGc);
        ResetHistoryCommand = new RelayCommand(ResetHistory, () => _history.Count > 0);
        TrackSelectionCommand = new RelayCommand(TrackSelection, () => _inspectionTarget is not null);
        ClearTrackedCommand = new RelayCommand(ClearTracked, () => _trackedReferences.Count > 0);
        RemoveCollectedCommand = new RelayCommand(RemoveCollected, HasCollectedReferences);

        var sampleSource = new FlatTreeDataGridSource<MemorySampleViewModel>(Array.Empty<MemorySampleViewModel>());
        sampleSource.Columns.Add(new TextColumn<MemorySampleViewModel, string>("Time", x => x.TimestampText, new AGridLength(1, AGridUnitType.Star)));
        sampleSource.Columns.Add(new TextColumn<MemorySampleViewModel, string>("Managed Heap", x => x.ManagedHeapText, new AGridLength(1.3, AGridUnitType.Star)));
        sampleSource.Columns.Add(new TextColumn<MemorySampleViewModel, string>("Heap Δ", x => x.HeapDeltaText, new AGridLength(1, AGridUnitType.Star)));
        sampleSource.Columns.Add(new TextColumn<MemorySampleViewModel, string>("Allocated", x => x.TotalAllocatedText, new AGridLength(1.3, AGridUnitType.Star)));
        sampleSource.Columns.Add(new TextColumn<MemorySampleViewModel, string>("Working Set", x => x.WorkingSetText, new AGridLength(1.3, AGridUnitType.Star)));
        sampleSource.Columns.Add(new TextColumn<MemorySampleViewModel, string>("GC", x => x.GcCollectionsText, new AGridLength(1, AGridUnitType.Star)));
        SampleSource = sampleSource;
        SampleSelection = new TreeDataGridRowSelectionModel<MemorySampleViewModel>(SampleSource)
        {
            SingleSelect = true,
        };
        SampleSelection.SelectionChanged += (_, _) => SelectedSample = SampleSelection.SelectedItem;
        SampleSource.Selection = SampleSelection;

        var trackedSource = new FlatTreeDataGridSource<MemoryTrackedObjectViewModel>(Array.Empty<MemoryTrackedObjectViewModel>());
        trackedSource.Columns.Add(new TextColumn<MemoryTrackedObjectViewModel, string>("Target", x => x.Name, new AGridLength(2, AGridUnitType.Star)));
        trackedSource.Columns.Add(new TextColumn<MemoryTrackedObjectViewModel, string>("Status", x => x.StatusText, new AGridLength(1, AGridUnitType.Star)));
        trackedSource.Columns.Add(new TextColumn<MemoryTrackedObjectViewModel, string>("Age", x => x.AgeText, new AGridLength(1, AGridUnitType.Star)));
        trackedSource.Columns.Add(new TextColumn<MemoryTrackedObjectViewModel, string>("Type", x => x.TypeText, new AGridLength(1.5, AGridUnitType.Star)));
        TrackedSource = trackedSource;
        TrackedSelection = new TreeDataGridRowSelectionModel<MemoryTrackedObjectViewModel>(TrackedSource)
        {
            SingleSelect = true,
        };
        TrackedSelection.SelectionChanged += (_, _) => SelectedTrackedObject = TrackedSelection.SelectedItem;
        TrackedSource.Selection = TrackedSelection;

        _timer = new DispatcherTimer
        {
            Interval = SamplingInterval,
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        TakeSample(recordHistory: true);
    }

    public FlatTreeDataGridSource<MemorySampleViewModel> SampleSource { get; }

    public TreeDataGridRowSelectionModel<MemorySampleViewModel> SampleSelection { get; }

    public FlatTreeDataGridSource<MemoryTrackedObjectViewModel> TrackedSource { get; }

    public TreeDataGridRowSelectionModel<MemoryTrackedObjectViewModel> TrackedSelection { get; }

    public ObservableCollection<MemoryInspectionItemViewModel> DetailItems { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ToggleSamplingCommand { get; }

    public RelayCommand ForceGcCommand { get; }

    public RelayCommand ResetHistoryCommand { get; }

    public RelayCommand TrackSelectionCommand { get; }

    public RelayCommand ClearTrackedCommand { get; }

    public RelayCommand RemoveCollectedCommand { get; }

    public bool IsSampling
    {
        get => _isSampling;
        private set
        {
            if (RaiseAndSetIfChanged(ref _isSampling, value))
            {
                RaisePropertyChanged(nameof(SamplingButtonText));
                RaisePropertyChanged(nameof(StatusText));
            }
        }
    }

    public string SamplingButtonText => IsSampling ? "Pause Sampling" : "Start Sampling";

    public string InspectionTargetSummary => MemoryInspector.FormatInspectionTargetSummary(_currentSnapshot, _root);

    public string StatusText => IsSampling
        ? $"Live sampling every {SamplingInterval.TotalSeconds:0} second."
        : "Live sampling paused.";

    public string CurrentManagedHeapText => _currentSnapshot is null ? "(n/a)" : MemoryInspector.FormatBytes(_currentSnapshot.ManagedHeapBytes);

    public string CurrentHeapDeltaText => _currentSnapshot is null ? "(n/a)" : MemoryInspector.FormatSignedBytes(_currentSnapshot.HeapDeltaBytes);

    public string CurrentAllocatedText => _currentSnapshot is null ? "(n/a)" : MemoryInspector.FormatBytes(_currentSnapshot.TotalAllocatedBytes);

    public string CurrentAllocatedDeltaText => _currentSnapshot is null ? "(n/a)" : MemoryInspector.FormatSignedBytes(_currentSnapshot.AllocatedDeltaBytes);

    public string CurrentWorkingSetText => _currentSnapshot is null ? "(n/a)" : MemoryInspector.FormatBytes(_currentSnapshot.WorkingSetBytes);

    public string CurrentPrivateMemoryText => _currentSnapshot is null ? "(n/a)" : MemoryInspector.FormatBytes(_currentSnapshot.PrivateMemoryBytes);

    public string CurrentGcCollectionsText => _currentSnapshot is null
        ? "(n/a)"
        : $"{_currentSnapshot.GarbageCollector.Gen0Collections}/{_currentSnapshot.GarbageCollector.Gen1Collections}/{_currentSnapshot.GarbageCollector.Gen2Collections}";

    public string CurrentSelectionText => _currentSnapshot?.Selection?.DisplayName ?? "(none)";

    public string CurrentSelectionCountsText => _currentSnapshot?.Selection is { } selection
        ? $"{selection.VisualNodeCount} visual / {selection.LogicalNodeCount} logical"
        : "No current selection";

    public string CurrentRootCountsText => _currentSnapshot is null
        ? "(n/a)"
        : $"{_currentSnapshot.HostRoot.VisualNodeCount} visual / {_currentSnapshot.HostRoot.LogicalNodeCount} logical";

    public string CurrentProcessText => _currentSnapshot is null
        ? "(n/a)"
        : $"{_currentSnapshot.Process.ProcessName} #{_currentSnapshot.Process.ProcessId}";

    public string CurrentSnapshotSummary => _currentSnapshot?.Summary ?? "No memory samples captured yet.";

    public string SampleCountText => _sampleCount == 1 ? "1 sample" : $"{_sampleCount} samples";

    public string TrackedCountText => _trackedCount == 1 ? "1 tracked weak reference" : $"{_trackedCount} tracked weak references";

    public string SelectedSampleName => SelectedSample?.TimestampText ?? "No sample selected";

    public string SelectedSampleSummary => SelectedSample?.Summary ?? "Select a sample to inspect GC, process, and tree-memory snapshots.";

    public string TrackedSelectionSummary => SelectedTrackedObject?.Summary ?? "Track selected controls to see whether they stay alive after garbage collection.";

    public MemoryObjectDetailsViewModel? Details
    {
        get => _details;
        private set => RaiseAndSetIfChanged(ref _details, value);
    }

    public MemorySampleViewModel? SelectedSample
    {
        get => _selectedSample;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedSample, value))
            {
                RaisePropertyChanged(nameof(SelectedSampleName));
                RaisePropertyChanged(nameof(SelectedSampleSummary));
                RefreshDetailItems();
            }
        }
    }

    public MemoryInspectionItemViewModel? SelectedDetailItem
    {
        get => _selectedDetailItem;
        set
        {
            if (RaiseAndSetIfChanged(ref _selectedDetailItem, value))
            {
                _detailsMode = MemoryDetailsMode.SampleFacts;
                UpdateDetailsFromCurrentMode();
            }
        }
    }

    public MemoryTrackedObjectViewModel? SelectedTrackedObject
    {
        get => _selectedTrackedObject;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedTrackedObject, value))
            {
                RaisePropertyChanged(nameof(TrackedSelectionSummary));
                if (value is not null)
                {
                    _detailsMode = MemoryDetailsMode.TrackedObject;
                }

                UpdateDetailsFromCurrentMode();
            }
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    public void Refresh() => TakeSample(recordHistory: true);

    public void RefreshDetails() => Details?.Refresh();

    public void UpdateInspectionTarget(DependencyObject? element)
    {
        if (ReferenceEquals(_inspectionTarget, element))
        {
            return;
        }

        _inspectionTarget = element;
        TrackSelectionCommand.RaiseCanExecuteChanged();
        TakeSample(recordHistory: false);
    }

    public void UpdateIncludeClrProperties(bool includeClrProperties)
    {
        if (_includeClrProperties == includeClrProperties)
        {
            return;
        }

        _includeClrProperties = includeClrProperties;
        Details?.UpdateIncludeClrProperties(includeClrProperties);
    }

    private void OnTimerTick(object? sender, object e)
    {
        if (IsSampling)
        {
            TakeSample(recordHistory: true);
        }
    }

    private void ToggleSampling()
    {
        IsSampling = !IsSampling;
        if (IsSampling)
        {
            _timer.Start();
            TakeSample(recordHistory: true);
        }
        else
        {
            _timer.Stop();
        }
    }

    private void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        TakeSample(recordHistory: true);
    }

    private void ResetHistory()
    {
        _history.Clear();
        if (_currentSnapshot is not null)
        {
            _history.Add(MemoryInspector.CreateSampleViewModel(_currentSnapshot));
        }

        RefreshSampleSource(selectNewest: true, restoreId: null);
    }

    private void TrackSelection()
    {
        if (_inspectionTarget is null)
        {
            return;
        }

        foreach (var existing in _trackedReferences)
        {
            if (existing.TryGetTarget(out var target) && ReferenceEquals(target, _inspectionTarget))
            {
                _pendingTrackedSelectionId = existing.TrackingId;
                RefreshTrackedObjects();
                return;
            }
        }

        var reference = new TrackedMemoryReference($"TRACK:{++_trackingSequence}", _inspectionTarget);
        _trackedReferences.Insert(0, reference);
        _pendingTrackedSelectionId = reference.TrackingId;
        RefreshTrackedObjects();
    }

    private void ClearTracked()
    {
        _trackedReferences.Clear();
        RefreshTrackedObjects();
    }

    private void RemoveCollected()
    {
        _trackedReferences.RemoveAll(reference => !reference.TryGetTarget(out _));
        RefreshTrackedObjects();
    }

    private bool HasCollectedReferences()
        => _trackedReferences.Any(reference => !reference.TryGetTarget(out _));

    private void TakeSample(bool recordHistory)
    {
        var previousSnapshot = _currentSnapshot;
        _currentSnapshot = MemoryInspector.CaptureSnapshot(_root, _inspectionTarget, previousSnapshot);
        RaiseCurrentSnapshotProperties();

        if (recordHistory || _history.Count == 0)
        {
            var previousTopId = _history.Count > 0 ? _history[0].SampleId : null;
            var restoreId = SelectedSample?.SampleId;
            var selectNewest = SelectedSample is null || string.Equals(restoreId, previousTopId, StringComparison.Ordinal);

            _history.Insert(0, MemoryInspector.CreateSampleViewModel(_currentSnapshot));
            while (_history.Count > MaxHistorySamples)
            {
                _history.RemoveAt(_history.Count - 1);
            }

            RefreshSampleSource(selectNewest, restoreId);
        }
        else
        {
            RaisePropertyChanged(nameof(SampleCountText));
        }

        RefreshTrackedObjects();
    }

    private void RaiseCurrentSnapshotProperties()
    {
        RaisePropertyChanged(nameof(InspectionTargetSummary));
        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(CurrentManagedHeapText));
        RaisePropertyChanged(nameof(CurrentHeapDeltaText));
        RaisePropertyChanged(nameof(CurrentAllocatedText));
        RaisePropertyChanged(nameof(CurrentAllocatedDeltaText));
        RaisePropertyChanged(nameof(CurrentWorkingSetText));
        RaisePropertyChanged(nameof(CurrentPrivateMemoryText));
        RaisePropertyChanged(nameof(CurrentGcCollectionsText));
        RaisePropertyChanged(nameof(CurrentSelectionText));
        RaisePropertyChanged(nameof(CurrentSelectionCountsText));
        RaisePropertyChanged(nameof(CurrentRootCountsText));
        RaisePropertyChanged(nameof(CurrentProcessText));
        RaisePropertyChanged(nameof(CurrentSnapshotSummary));
    }

    private void RefreshSampleSource(bool selectNewest, string? restoreId)
    {
        SampleSource.Items = _history.ToArray();
        if (RaiseAndSetIfChanged(ref _sampleCount, _history.Count, nameof(SampleCountText)))
        {
            RaisePropertyChanged(nameof(SampleCountText));
        }

        ResetHistoryCommand.RaiseCanExecuteChanged();

        if (_history.Count == 0)
        {
            SampleSelection.SelectedIndex = default;
            SelectedSample = null;
            return;
        }

        if (!selectNewest &&
            restoreId is not null &&
            TryFindSample(_history, restoreId, out var restoreIndex, out var restoreSample))
        {
            SampleSelection.SelectedIndex = new IndexPath(restoreIndex);
            SelectedSample = restoreSample;
            return;
        }

        SampleSelection.SelectedIndex = new IndexPath(0);
        SelectedSample = _history[0];
    }

    private void RefreshTrackedObjects()
    {
        var restoreId = _pendingTrackedSelectionId ?? SelectedTrackedObject?.TrackingId;
        _pendingTrackedSelectionId = null;

        var trackedObjects = MemoryInspector.BuildTrackedObjects(_trackedReferences).ToArray();
        TrackedSource.Items = trackedObjects;

        if (RaiseAndSetIfChanged(ref _trackedCount, trackedObjects.Length, nameof(TrackedCountText)))
        {
            RaisePropertyChanged(nameof(TrackedCountText));
        }

        ClearTrackedCommand.RaiseCanExecuteChanged();
        RemoveCollectedCommand.RaiseCanExecuteChanged();

        if (restoreId is not null && TryFindTracked(trackedObjects, restoreId, out var trackedIndex, out var trackedObject))
        {
            TrackedSelection.SelectedIndex = new IndexPath(trackedIndex);
            SelectedTrackedObject = trackedObject;
            return;
        }

        TrackedSelection.SelectedIndex = default;
        if (_detailsMode == MemoryDetailsMode.TrackedObject)
        {
            SelectedTrackedObject = null;
        }
    }

    private void RefreshDetailItems()
    {
        var preserveTrackedDetails = _detailsMode == MemoryDetailsMode.TrackedObject && SelectedTrackedObject is not null;
        var restoreName = SelectedDetailItem?.Name;
        DetailItems.Clear();
        foreach (var item in SelectedSample is not null ? MemoryInspector.BuildInspectionItems(SelectedSample) : Array.Empty<MemoryInspectionItemViewModel>())
        {
            DetailItems.Add(item);
        }

        if (restoreName is not null)
        {
            SelectedDetailItem = DetailItems.FirstOrDefault(x => string.Equals(x.Name, restoreName, StringComparison.Ordinal));
        }

        SelectedDetailItem ??= DetailItems.FirstOrDefault();

        if (preserveTrackedDetails)
        {
            _detailsMode = MemoryDetailsMode.TrackedObject;
        }

        UpdateDetailsFromCurrentMode();
    }

    private void UpdateDetailsFromCurrentMode()
    {
        if (_detailsMode == MemoryDetailsMode.TrackedObject && SelectedTrackedObject?.DetailsObject is { } trackedObject)
        {
            Details = new MemoryObjectDetailsViewModel(
                SelectedTrackedObject.Name,
                SelectedTrackedObject.StatusText,
                SelectedTrackedObject.Summary,
                "Tracked Object",
                SelectedTrackedObject.TypeText,
                SelectedTrackedObject.TrackingId,
                trackedObject,
                _includeClrProperties);
            return;
        }

        if (SelectedDetailItem?.InspectionObject is { } inspectionObject)
        {
            Details = new MemoryObjectDetailsViewModel(
                SelectedDetailItem.Name,
                SelectedDetailItem.Value,
                SelectedDetailItem.Detail,
                "Memory Snapshot",
                SelectedSample?.TimestampText ?? "Memory",
                SelectedSample?.SampleId ?? "Memory",
                inspectionObject,
                _includeClrProperties);
            return;
        }

        Details = null;
    }

    private static bool TryFindSample(IReadOnlyList<MemorySampleViewModel> samples, string sampleId, out int index, out MemorySampleViewModel? sample)
    {
        for (var i = 0; i < samples.Count; i++)
        {
            if (string.Equals(samples[i].SampleId, sampleId, StringComparison.Ordinal))
            {
                index = i;
                sample = samples[i];
                return true;
            }
        }

        index = -1;
        sample = null;
        return false;
    }

    private static bool TryFindTracked(IReadOnlyList<MemoryTrackedObjectViewModel> tracked, string trackingId, out int index, out MemoryTrackedObjectViewModel? trackedObject)
    {
        for (var i = 0; i < tracked.Count; i++)
        {
            if (string.Equals(tracked[i].TrackingId, trackingId, StringComparison.Ordinal))
            {
                index = i;
                trackedObject = tracked[i];
                return true;
            }
        }

        index = -1;
        trackedObject = null;
        return false;
    }

    private enum MemoryDetailsMode
    {
        SampleFacts,
        TrackedObject,
    }
}
