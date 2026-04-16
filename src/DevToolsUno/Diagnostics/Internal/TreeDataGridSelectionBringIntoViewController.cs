using Uno.Controls;
using Uno.Controls.Selection;
using Microsoft.UI.Xaml;

namespace DevToolsUno.Diagnostics.Internal;

internal sealed class TreeDataGridSelectionBringIntoViewController
{
    private const int DefaultBringAttempts = 6;

    private readonly FrameworkElement _owner;
    private readonly TreeDataGrid _treeDataGrid;
    private readonly Func<object?, ITreeSelectionModel?> _resolveSelection;
    private ITreeSelectionModel? _selection;
    private bool _isLoaded;
    private bool _bringScheduled;
    private int _remainingBringAttempts;

    public TreeDataGridSelectionBringIntoViewController(
        FrameworkElement owner,
        TreeDataGrid treeDataGrid,
        Func<object?, ITreeSelectionModel?> resolveSelection)
    {
        _owner = owner;
        _treeDataGrid = treeDataGrid;
        _resolveSelection = resolveSelection;

        owner.Loaded += OnLoaded;
        owner.Unloaded += OnUnloaded;
        owner.DataContextChanged += OnDataContextChanged;
        AttachSelection(owner.DataContext);
    }

    public void RequestBringIntoView()
    {
        _remainingBringAttempts = DefaultBringAttempts;
        ScheduleBringIntoView();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        RequestBringIntoView();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _bringScheduled = false;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        AttachSelection(sender.DataContext);
        RequestBringIntoView();
    }

    private void AttachSelection(object? dataContext)
    {
        var selection = _resolveSelection(dataContext);
        if (ReferenceEquals(_selection, selection))
        {
            return;
        }

        if (_selection is not null)
        {
            _selection.SelectionChanged -= OnSelectionChanged;
        }

        _selection = selection;

        if (_selection is not null)
        {
            _selection.SelectionChanged += OnSelectionChanged;
        }
    }

    private void OnSelectionChanged(object? sender, TreeSelectionModelSelectionChangedEventArgs e)
    {
        RequestBringIntoView();
    }

    private void ScheduleBringIntoView()
    {
        if (!_isLoaded || _bringScheduled)
        {
            return;
        }

        _bringScheduled = true;
        if (!_owner.DispatcherQueue.TryEnqueue(ProcessBringIntoView))
        {
            ProcessBringIntoView();
        }
    }

    private void ProcessBringIntoView()
    {
        _bringScheduled = false;

        if (!_isLoaded || _selection is null)
        {
            return;
        }

        var selectedIndex = _selection.SelectedIndex;
        if (selectedIndex.Count == 0)
        {
            return;
        }

        _treeDataGrid.UpdateLayout();

        var rowIndex = _treeDataGrid.Rows?.ModelIndexToRowIndex(selectedIndex) ?? -1;
        if (rowIndex >= 0 && _treeDataGrid.RowsPresenter is not null)
        {
            _treeDataGrid.RowsPresenter.BringIntoView(rowIndex);
            return;
        }

        if (--_remainingBringAttempts > 0)
        {
            ScheduleBringIntoView();
        }
    }
}
