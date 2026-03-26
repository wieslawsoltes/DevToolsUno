using System.Diagnostics;
using DevTools.Uno.Diagnostics.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace DevTools.Uno.Diagnostics.Internal;

internal static class MemoryInspector
{
    public static MemorySnapshot CaptureSnapshot(FrameworkElement root, DependencyObject? inspectionTarget, MemorySnapshot? previousSnapshot)
    {
        var gcSnapshot = CaptureGcSnapshot();
        var processSnapshot = CaptureProcessSnapshot();
        var hostRoot = CaptureTreeSummary("Host Root", root, includeOpenPopups: true);
        var selection = inspectionTarget is not null
            ? CaptureTreeSummary("Selection", inspectionTarget, includeOpenPopups: false)
            : null;

        var managedHeapBytes = gcSnapshot.ManagedHeapBytes;
        var totalAllocatedBytes = gcSnapshot.TotalAllocatedBytes;

        return new MemorySnapshot
        {
            SampleId = $"MEMORY:{DateTime.UtcNow.Ticks}",
            Timestamp = DateTime.Now,
            ManagedHeapBytes = managedHeapBytes,
            HeapDeltaBytes = previousSnapshot is null ? 0 : managedHeapBytes - previousSnapshot.ManagedHeapBytes,
            TotalAllocatedBytes = totalAllocatedBytes,
            AllocatedDeltaBytes = previousSnapshot is null ? 0 : totalAllocatedBytes - previousSnapshot.TotalAllocatedBytes,
            WorkingSetBytes = processSnapshot.WorkingSetBytes,
            PrivateMemoryBytes = processSnapshot.PrivateMemoryBytes,
            GarbageCollector = gcSnapshot,
            Process = processSnapshot,
            HostRoot = hostRoot,
            Selection = selection,
            Summary = BuildSnapshotSummary(managedHeapBytes, processSnapshot, selection, hostRoot, gcSnapshot),
        };
    }

    public static MemorySampleViewModel CreateSampleViewModel(MemorySnapshot snapshot)
    {
        return new MemorySampleViewModel
        {
            SampleId = snapshot.SampleId,
            TimestampText = snapshot.Timestamp.ToString("HH:mm:ss"),
            ManagedHeapText = FormatBytes(snapshot.ManagedHeapBytes),
            HeapDeltaText = FormatSignedBytes(snapshot.HeapDeltaBytes),
            TotalAllocatedText = FormatBytes(snapshot.TotalAllocatedBytes),
            WorkingSetText = FormatBytes(snapshot.WorkingSetBytes),
            PrivateMemoryText = FormatBytes(snapshot.PrivateMemoryBytes),
            GcCollectionsText = $"{snapshot.GarbageCollector.Gen0Collections}/{snapshot.GarbageCollector.Gen1Collections}/{snapshot.GarbageCollector.Gen2Collections}",
            Summary = snapshot.Summary,
            Snapshot = snapshot,
        };
    }

    public static IReadOnlyList<MemoryInspectionItemViewModel> BuildInspectionItems(MemorySampleViewModel sample)
    {
        var snapshot = sample.Snapshot;
        var items = new List<MemoryInspectionItemViewModel>
        {
            new()
            {
                Name = "Current Snapshot",
                Value = sample.ManagedHeapText,
                Detail = snapshot.Summary,
                InspectionObject = snapshot,
            },
            new()
            {
                Name = "Garbage Collector",
                Value = FormatBytes(snapshot.GarbageCollector.HeapSizeBytes),
                Detail = $"Collections: {snapshot.GarbageCollector.Gen0Collections}/{snapshot.GarbageCollector.Gen1Collections}/{snapshot.GarbageCollector.Gen2Collections}",
                InspectionObject = snapshot.GarbageCollector,
            },
            new()
            {
                Name = "Process",
                Value = FormatBytes(snapshot.Process.WorkingSetBytes),
                Detail = $"{snapshot.Process.ProcessName} #{snapshot.Process.ProcessId} | Threads: {snapshot.Process.ThreadCount}",
                InspectionObject = snapshot.Process,
            },
            new()
            {
                Name = "Host Root",
                Value = $"{snapshot.HostRoot.VisualNodeCount} visual / {snapshot.HostRoot.LogicalNodeCount} logical",
                Detail = snapshot.HostRoot.Summary,
                InspectionObject = snapshot.HostRoot,
            },
        };

        if (snapshot.Selection is { } selection)
        {
            items.Add(new MemoryInspectionItemViewModel
            {
                Name = "Selection",
                Value = $"{selection.VisualNodeCount} visual / {selection.LogicalNodeCount} logical",
                Detail = selection.Summary,
                InspectionObject = selection,
            });
        }

        return items;
    }

    public static IReadOnlyList<MemoryTrackedObjectViewModel> BuildTrackedObjects(IReadOnlyList<TrackedMemoryReference> trackedReferences)
    {
        var now = DateTime.Now;
        return trackedReferences.Select(reference => BuildTrackedObject(reference, now)).ToArray();
    }

    public static string FormatInspectionTargetSummary(MemorySnapshot? snapshot, FrameworkElement root)
    {
        if (snapshot?.Selection is { } selection)
        {
            return $"Selection: {selection.DisplayName}";
        }

        return $"Selection: (none, using host root {DescribeDependencyObject(root)})";
    }

    public static string FormatBytes(long value)
    {
        if (value < 0)
        {
            return "(n/a)";
        }

        return AssetEntryViewModelFormatter.FormatBytes((ulong)value);
    }

    public static string FormatSignedBytes(long value)
    {
        if (value == 0)
        {
            return "0 KB";
        }

        var sign = value > 0 ? "+" : "-";
        var absolute = value == long.MinValue ? long.MaxValue : Math.Abs(value);
        return $"{sign}{FormatBytes(absolute)}";
    }

    public static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 60)
        {
            return $"{Math.Max(0, (int)age.TotalSeconds)} s";
        }

        if (age.TotalMinutes < 60)
        {
            return $"{age.TotalMinutes:0.#} min";
        }

        return $"{age.TotalHours:0.#} hr";
    }

    private static GcMemorySnapshot CaptureGcSnapshot()
    {
        GCMemoryInfo memoryInfo;
        try
        {
            memoryInfo = GC.GetGCMemoryInfo();
        }
        catch
        {
            memoryInfo = default;
        }

        return new GcMemorySnapshot
        {
            ManagedHeapBytes = SafeRead(() => GC.GetTotalMemory(false)),
            TotalAllocatedBytes = SafeRead(() => GC.GetTotalAllocatedBytes()),
            Gen0Collections = SafeRead(() => GC.CollectionCount(0)),
            Gen1Collections = SafeRead(() => GC.CollectionCount(1)),
            Gen2Collections = SafeRead(() => GC.CollectionCount(2)),
            HeapSizeBytes = memoryInfo.HeapSizeBytes,
            FragmentedBytes = memoryInfo.FragmentedBytes,
            HighMemoryLoadThresholdBytes = memoryInfo.HighMemoryLoadThresholdBytes,
            MemoryLoadBytes = memoryInfo.MemoryLoadBytes,
            TotalAvailableMemoryBytes = memoryInfo.TotalAvailableMemoryBytes,
            Index = memoryInfo.Index,
            Generation = memoryInfo.Generation,
            Compacted = memoryInfo.Compacted,
            Concurrent = memoryInfo.Concurrent,
        };
    }

    private static ProcessMemorySnapshot CaptureProcessSnapshot()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();

            return new ProcessMemorySnapshot
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                WorkingSetBytes = SafeRead(() => process.WorkingSet64),
                PrivateMemoryBytes = SafeRead(() => process.PrivateMemorySize64),
                PagedMemoryBytes = SafeRead(() => process.PagedMemorySize64),
                VirtualMemoryBytes = SafeRead(() => process.VirtualMemorySize64),
                ThreadCount = SafeRead(() => process.Threads.Count),
                HandleCount = SafeReadNullable(() => process.HandleCount),
                StartTime = SafeReadNullable(() => process.StartTime),
                TotalProcessorTime = SafeRead(() => process.TotalProcessorTime),
                UserProcessorTime = SafeRead(() => process.UserProcessorTime),
                PrivilegedProcessorTime = SafeRead(() => process.PrivilegedProcessorTime),
            };
        }
        catch
        {
            return new ProcessMemorySnapshot
            {
                ProcessId = Environment.ProcessId,
                ProcessName = AppDomain.CurrentDomain.FriendlyName,
                WorkingSetBytes = 0,
                PrivateMemoryBytes = 0,
                PagedMemoryBytes = 0,
                VirtualMemoryBytes = 0,
                ThreadCount = 0,
                HandleCount = null,
                StartTime = null,
                TotalProcessorTime = TimeSpan.Zero,
                UserProcessorTime = TimeSpan.Zero,
                PrivilegedProcessorTime = TimeSpan.Zero,
            };
        }
    }

    private static TreeMemorySummary CaptureTreeSummary(string label, DependencyObject root, bool includeOpenPopups)
    {
        var popupCount = includeOpenPopups && root is FrameworkElement feWithRoot && feWithRoot.XamlRoot is { } xamlRoot
            ? VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Count
            : 0;
        var visualCount = CountVisualNodes(root, includeOpenPopups);
        var logicalCount = CountLogicalNodes(root, includeOpenPopups);
        var displayName = DescribeDependencyObject(root);

        return new TreeMemorySummary
        {
            Label = label,
            DisplayName = displayName,
            TypeName = root.GetType().FullName ?? root.GetType().Name,
            ElementName = root is FrameworkElement frameworkElement ? frameworkElement.Name : string.Empty,
            VisualNodeCount = visualCount,
            LogicalNodeCount = logicalCount,
            PopupCount = popupCount,
            Summary = BuildTreeSummary(label, displayName, visualCount, logicalCount, popupCount),
        };
    }

    private static string BuildSnapshotSummary(
        long managedHeapBytes,
        ProcessMemorySnapshot process,
        TreeMemorySummary? selection,
        TreeMemorySummary hostRoot,
        GcMemorySnapshot gc)
    {
        var selectionText = selection is not null
            ? $"{selection.DisplayName} ({selection.VisualNodeCount}V/{selection.LogicalNodeCount}L)"
            : $"Host Root {hostRoot.DisplayName}";

        return $"Heap {FormatBytes(managedHeapBytes)} | Working Set {FormatBytes(process.WorkingSetBytes)} | Selection {selectionText} | GC {gc.Gen0Collections}/{gc.Gen1Collections}/{gc.Gen2Collections}";
    }

    private static string BuildTreeSummary(string label, string displayName, int visualCount, int logicalCount, int popupCount)
    {
        return popupCount > 0
            ? $"{label}: {displayName} | {visualCount} visual | {logicalCount} logical | {popupCount} popup(s)"
            : $"{label}: {displayName} | {visualCount} visual | {logicalCount} logical";
    }

    private static MemoryTrackedObjectViewModel BuildTrackedObject(TrackedMemoryReference reference, DateTime now)
    {
        var isAlive = reference.TryGetTarget(out var target) && target is not null;
        var currentTree = isAlive ? CaptureTreeSummary("Tracked Target", target!, includeOpenPopups: false) : null;
        var snapshot = new TrackedObjectSnapshot
        {
            TrackingId = reference.TrackingId,
            DisplayName = reference.DisplayName,
            TypeName = reference.TypeName,
            TrackedAt = reference.TrackedAt,
            Age = now - reference.TrackedAt,
            InitialPath = reference.InitialPath,
            Status = isAlive ? "Alive" : "Collected",
            IsAlive = isAlive,
            CurrentTree = currentTree,
            LiveTarget = target,
            Summary = isAlive && currentTree is not null
                ? $"{reference.DisplayName} is alive with {currentTree.VisualNodeCount} visual and {currentTree.LogicalNodeCount} logical nodes."
                : $"{reference.DisplayName} is no longer strongly reachable by the diagnostics weak reference.",
        };

        return new MemoryTrackedObjectViewModel
        {
            TrackingId = reference.TrackingId,
            Name = reference.DisplayName,
            StatusText = snapshot.Status,
            AgeText = FormatAge(snapshot.Age),
            Summary = snapshot.Summary,
            TypeText = reference.TypeName,
            DetailsObject = snapshot,
            IsAlive = snapshot.IsAlive,
        };
    }

    private static int CountVisualNodes(DependencyObject root, bool includeOpenPopups)
    {
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        var count = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            count++;

            var visualCount = VisualTreeHelper.GetChildrenCount(current);
            for (var index = 0; index < visualCount; index++)
            {
                if (VisualTreeHelper.GetChild(current, index) is { } child)
                {
                    queue.Enqueue(child);
                }
            }

            if (includeOpenPopups &&
                ReferenceEquals(current, root) &&
                current is FrameworkElement frameworkElement &&
                frameworkElement.XamlRoot is not null)
            {
                foreach (var popupChild in TreeInspector.GetOpenPopupChildren(frameworkElement.XamlRoot))
                {
                    queue.Enqueue(popupChild);
                }
            }
        }

        return count;
    }

    private static int CountLogicalNodes(DependencyObject root, bool includeOpenPopups)
    {
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(DependencyObject Node, bool IsRoot)>();
        queue.Enqueue((root, true));
        var count = 0;

        while (queue.Count > 0)
        {
            var (current, isRoot) = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            count++;
            foreach (var child in TreeInspector.GetLogicalChildren(current, includeOpenPopups && isRoot))
            {
                queue.Enqueue((child, false));
            }
        }

        return count;
    }

    private static string DescribeDependencyObject(DependencyObject value)
    {
        return value switch
        {
            FrameworkElement element => ResourceInspector.FormatElementName(element),
            _ => value.GetType().Name,
        };
    }

    private static T SafeRead<T>(Func<T> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return default!;
        }
    }

    private static T? SafeReadNullable<T>(Func<T> getter)
        where T : struct
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class MemorySnapshot
{
    public required string SampleId { get; init; }

    public required DateTime Timestamp { get; init; }

    public required long ManagedHeapBytes { get; init; }

    public required long HeapDeltaBytes { get; init; }

    public required long TotalAllocatedBytes { get; init; }

    public required long AllocatedDeltaBytes { get; init; }

    public required long WorkingSetBytes { get; init; }

    public required long PrivateMemoryBytes { get; init; }

    public required GcMemorySnapshot GarbageCollector { get; init; }

    public required ProcessMemorySnapshot Process { get; init; }

    public required TreeMemorySummary HostRoot { get; init; }

    public TreeMemorySummary? Selection { get; init; }

    public required string Summary { get; init; }
}

internal sealed class GcMemorySnapshot
{
    public required long ManagedHeapBytes { get; init; }

    public required long TotalAllocatedBytes { get; init; }

    public required int Gen0Collections { get; init; }

    public required int Gen1Collections { get; init; }

    public required int Gen2Collections { get; init; }

    public required long HeapSizeBytes { get; init; }

    public required long FragmentedBytes { get; init; }

    public required long HighMemoryLoadThresholdBytes { get; init; }

    public required long MemoryLoadBytes { get; init; }

    public required long TotalAvailableMemoryBytes { get; init; }

    public required long Index { get; init; }

    public required int Generation { get; init; }

    public required bool Compacted { get; init; }

    public required bool Concurrent { get; init; }
}

internal sealed class ProcessMemorySnapshot
{
    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public required long WorkingSetBytes { get; init; }

    public required long PrivateMemoryBytes { get; init; }

    public required long PagedMemoryBytes { get; init; }

    public required long VirtualMemoryBytes { get; init; }

    public required int ThreadCount { get; init; }

    public required int? HandleCount { get; init; }

    public required DateTime? StartTime { get; init; }

    public required TimeSpan TotalProcessorTime { get; init; }

    public required TimeSpan UserProcessorTime { get; init; }

    public required TimeSpan PrivilegedProcessorTime { get; init; }
}

internal sealed class TreeMemorySummary
{
    public required string Label { get; init; }

    public required string DisplayName { get; init; }

    public required string TypeName { get; init; }

    public required string ElementName { get; init; }

    public required int VisualNodeCount { get; init; }

    public required int LogicalNodeCount { get; init; }

    public required int PopupCount { get; init; }

    public required string Summary { get; init; }
}

internal sealed class TrackedObjectSnapshot
{
    public required string TrackingId { get; init; }

    public required string DisplayName { get; init; }

    public required string TypeName { get; init; }

    public required DateTime TrackedAt { get; init; }

    public required TimeSpan Age { get; init; }

    public required string InitialPath { get; init; }

    public required string Status { get; init; }

    public required bool IsAlive { get; init; }

    public TreeMemorySummary? CurrentTree { get; init; }

    public object? LiveTarget { get; init; }

    public required string Summary { get; init; }
}

internal sealed class TrackedMemoryReference
{
    private readonly WeakReference<DependencyObject> _reference;

    public TrackedMemoryReference(string trackingId, DependencyObject target)
    {
        TrackingId = trackingId;
        DisplayName = target is FrameworkElement element ? ResourceInspector.FormatElementName(element) : target.GetType().Name;
        TypeName = target.GetType().FullName ?? target.GetType().Name;
        TrackedAt = DateTime.Now;
        InitialPath = BuildPath(target);
        _reference = new WeakReference<DependencyObject>(target);
    }

    public string TrackingId { get; }

    public string DisplayName { get; }

    public string TypeName { get; }

    public DateTime TrackedAt { get; }

    public string InitialPath { get; }

    public bool TryGetTarget(out DependencyObject? target) => _reference.TryGetTarget(out target);

    private static string BuildPath(DependencyObject element)
    {
        var parts = new Stack<string>();
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            parts.Push(current switch
            {
                FrameworkElement frameworkElement when !string.IsNullOrWhiteSpace(frameworkElement.Name) => $"{current.GetType().Name}#{frameworkElement.Name}",
                _ => current.GetType().Name,
            });
        }

        return string.Join(" > ", parts);
    }
}
