using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TodoDesktopApp.Dialogs;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;
using TodoDesktopApp.ViewModels;
using TodoDesktopApp.Views;

namespace TodoDesktopApp;

public partial class MainWindow : Window
{
    private readonly TodoService _todoService;
    private readonly TaskExchangeService _taskExchangeService;
    private readonly FloatingTaskWindow _floatingWindow;
    private readonly WindowLevelService _windowLevelService;
    private readonly MainViewModel _viewModel;
    private bool _allowClose;
    private System.Windows.Point? _taskGridDragStart;
    private TodoItem? _taskGridDraggedTodo;
    private ScrollViewer? _taskGridScrollViewer;
    private ScrollViewer? _pinnedActionGridScrollViewer;
    private bool _isSyncingGridScroll;
    private bool _isSyncingHorizontalScroll;
    private bool _isSyncingGridSelection;

    public MainWindow(
        TodoService todoService,
        FloatingTaskWindow floatingWindow,
        WindowLevelService windowLevelService)
    {
        InitializeComponent();
        ApplyStartupBounds();
        _todoService = todoService;
        _taskExchangeService = new TaskExchangeService(todoService);
        _floatingWindow = floatingWindow;
        _windowLevelService = windowLevelService;
        _viewModel = new MainViewModel(todoService);
        DataContext = _viewModel;
        ClearSortIndicators();
    }

    public void OpenFromUserRequest()
    {
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void AddTodoFromUserRequest()
    {
        OpenFromUserRequest();
        AddTodo();
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        AddTodo();
    }

    private void RowEditButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TodoItem todo)
        {
            EditTodo(todo);
        }
    }

    private void AddSubtaskButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = RequireSelectedTodo();
        if (selected is null)
        {
            return;
        }

        AddSubtask(selected);
    }

    private void TaskRowAddSubtaskMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuTodo(sender) is TodoItem todo)
        {
            AddSubtask(todo);
        }
    }

    private void ManageGroupsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TaskGroupManagerWindow(_todoService) { Owner = this };
        dialog.ShowDialog();
        _viewModel.Refresh();
    }

    private void ImportExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void ImportTasksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = T("Dialog.ImportTasks.Title"),
            Filter = T("Dialog.JsonFile.Filter"),
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var previewResult = _taskExchangeService.ReadImportPreview(dialog.FileName);
            var previewTasks = previewResult.RootTasks;
            if (previewTasks.Count == 0)
            {
                ConfirmDialogWindow.ShowInfo(this, T("Dialog.ImportTasks.EmptyTitle"), T("Dialog.ImportTasks.EmptyMessage"));
                return;
            }

            var previewDialog = new ImportTasksPreviewWindow(previewTasks, previewResult.SkippedDuplicateCount) { Owner = this };
            if (previewDialog.ShowDialog() != true)
            {
                return;
            }

            var importedCount = _taskExchangeService.ImportTasks(previewDialog.RootTasks);
            RefreshAfterImport();
            ConfirmDialogWindow.ShowInfo(
                this,
                T("Dialog.ImportTasks.DoneTitle"),
                F("Dialog.ImportTasks.DoneMessageFormat", importedCount));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.ImportTasks.FailedTitle"), ex.Message);
        }
    }

    private void RefreshAfterImport()
    {
        _viewModel.Refresh();
        Dispatcher.BeginInvoke(new Action(_viewModel.Refresh), DispatcherPriority.ApplicationIdle);
    }

    private void ExportTasksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = T("Dialog.ExportTasks.Title"),
            Filter = T("Dialog.JsonFile.Filter"),
            FileName = $"JMtodo-tasks-{DateTime.Now:yyyyMMdd-HHmm}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _taskExchangeService.ExportAllTasks(dialog.FileName);
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.ExportTasks.DoneTitle"), T("Dialog.ExportTasks.DoneMessage"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.ExportTasks.FailedTitle"), ex.Message);
        }
    }

    private void ShowImportSampleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ImportSampleWindow(_taskExchangeService.CreateImportSampleJson()) { Owner = this };
        dialog.ShowDialog();
    }

    private void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyStatusToTodos(GetSelectedTodos(), markCompleted: true);
    }

    private void ReopenButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyStatusToTodos(GetSelectedTodos(), markCompleted: false);
    }

    private void MoveToGroupButton_Click(object sender, RoutedEventArgs e)
    {
        MoveTodosToGroup(GetSelectedTodos());
    }

    private void TaskRowMoveToGroupMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuTodo(sender) is TodoItem todo)
        {
            MoveTodosToGroup(new[] { todo });
        }
    }

    private void TaskRowCompleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuTodo(sender) is TodoItem todo)
        {
            ApplyStatusToTodos(new[] { todo }, markCompleted: true);
        }
    }

    private void TaskRowReopenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuTodo(sender) is TodoItem todo)
        {
            ApplyStatusToTodos(new[] { todo }, markCompleted: false);
        }
    }

    private void MoveTodosToGroup(IReadOnlyCollection<TodoItem> selectedTodos)
    {
        if (selectedTodos.Count == 0)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.NoSelection.Title"), T("Dialog.NoSelection.RootMessage"));
            return;
        }

        if (selectedTodos.Any(todo => todo.IsSubtask))
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.CannotMoveSubtask.Title"), T("Dialog.CannotMoveSubtask.Message"));
            return;
        }

        var dialog = new MoveToGroupDialogWindow(_todoService.GetGroups(), selectedTodos.Count) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var confirmDialog = new ConfirmDialogWindow
        {
            Owner = this,
            TitleText = T("Dialog.MoveConfirm.Title"),
            MessageText = F("Dialog.MoveConfirm.Message", selectedTodos.Count, dialog.TargetGroupName),
            ConfirmText = T("Dialog.MoveConfirm.Confirm"),
            CancelText = T("Dialog.Cancel")
        };

        if (confirmDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var targetGroupId = dialog.SelectedGroupId;
            if (dialog.CreateNewGroup)
            {
                targetGroupId = _todoService.CreateGroup(dialog.NewGroupName, dialog.NewGroupIconKey).Id;
            }

            _todoService.MoveRootTodosToGroup(selectedTodos.Select(todo => todo.Id), targetGroupId!);
        }
        catch (InvalidOperationException ex)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.MoveFailed.Title"), ex.Message);
        }
    }

    private void ApplyStatusToTodos(IReadOnlyCollection<TodoItem> selectedTodos, bool markCompleted)
    {
        if (selectedTodos.Count == 0)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.NoSelection.Title"), T("Dialog.NoSelection.Message"));
            return;
        }

        foreach (var todo in selectedTodos)
        {
            if (markCompleted)
            {
                _todoService.Complete(todo.Id);
            }
            else
            {
                _todoService.Reopen(todo.Id);
            }
        }
    }

    private void RowSoftDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TodoItem todo)
        {
            SoftDeleteTodo(todo);
        }
    }

    private void RowRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TodoItem todo)
        {
            RestoreTodo(todo);
        }
    }

    private void RowPermanentDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TodoItem todo)
        {
            PermanentDeleteTodo(todo);
        }
    }

    private void MultiSelectCheckBox_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var enableMultiSelect = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        var selectedTodo = TaskGrid.SelectedItems.Cast<TodoItem>().FirstOrDefault();

        if (!enableMultiSelect)
        {
            if (TaskGrid.SelectionMode == DataGridSelectionMode.Extended)
            {
                TaskGrid.SelectedItems.Clear();
            }
            else
            {
                TaskGrid.SelectedItem = null;
            }
        }

        _viewModel.IsMultiSelectMode = enableMultiSelect;
        TaskGrid.SelectionMode = enableMultiSelect ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single;
        PinnedActionGrid.SelectionMode = enableMultiSelect ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single;

        if (!enableMultiSelect && selectedTodo is not null)
        {
            TaskGrid.SelectedItem = selectedTodo;
        }

        UpdateSelectionFromTaskGrid();
        SyncPinnedActionGridSelection();
    }

    private void RowSelectionCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TodoItem todo)
        {
            return;
        }

        if (TaskGrid.SelectedItems.Contains(todo))
        {
            TaskGrid.SelectedItems.Remove(todo);
        }
        else
        {
            TaskGrid.SelectedItems.Add(todo);
        }

        UpdateSelectionFromTaskGrid();
        e.Handled = true;
    }

    private void GroupSummary_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TodoGroupSummary summary)
        {
            _viewModel.ToggleGroupFilter(summary);
        }
    }

    private void SoftDeleteTodo(TodoItem todo)
    {
        var messageText = todo.IsSubtask
            ? T("Dialog.DeleteConfirm.SubtaskMessage")
            : T("Dialog.DeleteConfirm.RootMessage");
        var dialog = new ConfirmDialogWindow
        {
            Owner = this,
            TitleText = T("Dialog.DeleteConfirm.Title"),
            MessageText = messageText,
            ConfirmText = T("Dialog.DeleteConfirm.Confirm"),
            CancelText = T("Dialog.Cancel")
        };

        if (dialog.ShowDialog() == true)
        {
            _todoService.SoftDelete(todo.Id);
        }
    }

    private void RestoreTodo(TodoItem todo)
    {
        var dialog = new ConfirmDialogWindow
        {
            Owner = this,
            TitleText = T("Dialog.RestoreConfirm.Title"),
            MessageText = T("Dialog.RestoreConfirm.Message"),
            ConfirmText = T("Dialog.RestoreConfirm.Confirm"),
            CancelText = T("Dialog.Cancel")
        };

        if (dialog.ShowDialog() == true)
        {
            _todoService.Restore(todo.Id);
        }
    }

    private void PermanentDeleteTodo(TodoItem todo)
    {
        var messageText = todo.IsSubtask
            ? T("Dialog.PermanentDelete.SubtaskMessage")
            : T("Dialog.PermanentDelete.RootMessage");
        var dialog = new ConfirmDialogWindow
        {
            Owner = this,
            TitleText = T("Dialog.PermanentDelete.Title"),
            MessageText = messageText,
            ConfirmText = T("Dialog.DeleteConfirm.Confirm"),
            CancelText = T("Dialog.Cancel")
        };

        if (dialog.ShowDialog() == true)
        {
            _todoService.DeletePermanent(todo.Id);
        }
    }

    private void ShowFloatingButton_Click(object sender, RoutedEventArgs e)
    {
        ShowFloatingOrOpenManager();
    }

    private void TaskGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isSyncingGridSelection)
        {
            return;
        }

        UpdateSelectionFromTaskGrid();
        SyncPinnedActionGridSelection();
    }

    private void TaskRowContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: DataGridRow row } && row.DataContext is TodoItem todo)
        {
            SelectSingleTodo(todo);
        }
    }

    private void PinnedActionGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isSyncingGridSelection)
        {
            return;
        }

        SyncTaskGridSelectionFromPinnedActionGrid();
    }

    private void TaskGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _taskGridScrollViewer = FindDescendant<ScrollViewer>(TaskGrid);
        if (_taskGridScrollViewer is null)
        {
            return;
        }

        _taskGridScrollViewer.ScrollChanged -= TaskGridScrollViewer_ScrollChanged;
        _taskGridScrollViewer.ScrollChanged += TaskGridScrollViewer_ScrollChanged;
        TaskGrid.SizeChanged -= TaskGrid_SizeChanged;
        TaskGrid.SizeChanged += TaskGrid_SizeChanged;
        SyncScrollViewer(_pinnedActionGridScrollViewer, _taskGridScrollViewer.VerticalOffset);
        UpdateTaskHorizontalScrollBar();
    }

    private void PinnedActionGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _pinnedActionGridScrollViewer = FindDescendant<ScrollViewer>(PinnedActionGrid);
        if (_pinnedActionGridScrollViewer is null)
        {
            return;
        }

        _pinnedActionGridScrollViewer.ScrollChanged -= PinnedActionGridScrollViewer_ScrollChanged;
        _pinnedActionGridScrollViewer.ScrollChanged += PinnedActionGridScrollViewer_ScrollChanged;
        PinnedActionGrid.SizeChanged -= PinnedActionGrid_SizeChanged;
        PinnedActionGrid.SizeChanged += PinnedActionGrid_SizeChanged;
        SyncScrollViewer(_pinnedActionGridScrollViewer, _taskGridScrollViewer?.VerticalOffset ?? 0);
        UpdateTaskHorizontalScrollBar();
        SyncPinnedActionGridSelection();
    }

    private void TaskGridScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.HorizontalChange) > double.Epsilon ||
            Math.Abs(e.ExtentWidthChange) > double.Epsilon ||
            Math.Abs(e.ViewportWidthChange) > double.Epsilon)
        {
            UpdateTaskHorizontalScrollBar();
        }

        if (Math.Abs(e.VerticalChange) < double.Epsilon &&
            Math.Abs(e.ExtentHeightChange) < double.Epsilon &&
            Math.Abs(e.ViewportHeightChange) < double.Epsilon)
        {
            return;
        }

        SyncScrollViewer(_pinnedActionGridScrollViewer, e.VerticalOffset);
    }

    private void TaskGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTaskHorizontalScrollBar();
    }

    private void PinnedActionGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTaskHorizontalScrollBar();
    }

    private void TaskHorizontalScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingHorizontalScroll || _taskGridScrollViewer is null)
        {
            return;
        }

        try
        {
            _isSyncingHorizontalScroll = true;
            _taskGridScrollViewer.ScrollToHorizontalOffset(e.NewValue);
        }
        finally
        {
            _isSyncingHorizontalScroll = false;
        }
    }

    private void UpdateTaskHorizontalScrollBar()
    {
        if (_taskGridScrollViewer is null)
        {
            TaskHorizontalScrollBar.Visibility = Visibility.Collapsed;
            return;
        }

        var maximum = Math.Max(0, _taskGridScrollViewer.ScrollableWidth);
        var pinnedWidth = Math.Max(0, PinnedActionGrid.ActualWidth);
        // 外置滚动条横跨固定操作列，滑块比例按整张表的可见宽度计算。
        var tableViewportWidth = Math.Max(0, _taskGridScrollViewer.ViewportWidth + pinnedWidth);

        try
        {
            _isSyncingHorizontalScroll = true;
            TaskHorizontalScrollBar.Maximum = maximum;
            TaskHorizontalScrollBar.ViewportSize = tableViewportWidth;
            TaskHorizontalScrollBar.LargeChange = Math.Max(16, _taskGridScrollViewer.ViewportWidth);
            TaskHorizontalScrollBar.Value = Math.Min(maximum, _taskGridScrollViewer.HorizontalOffset);
            TaskHorizontalScrollBar.Visibility = maximum > 0.1 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _isSyncingHorizontalScroll = false;
        }
    }

    private void PinnedActionGridScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalChange) < double.Epsilon &&
            Math.Abs(e.ExtentHeightChange) < double.Epsilon &&
            Math.Abs(e.ViewportHeightChange) < double.Epsilon)
        {
            return;
        }

        SyncScrollViewer(_taskGridScrollViewer, e.VerticalOffset);
    }

    private void SyncScrollViewer(ScrollViewer? target, double verticalOffset)
    {
        if (_isSyncingGridScroll || target is null || Math.Abs(target.VerticalOffset - verticalOffset) < 0.01)
        {
            return;
        }

        try
        {
            _isSyncingGridScroll = true;
            target.ScrollToVerticalOffset(verticalOffset);
        }
        finally
        {
            _isSyncingGridScroll = false;
        }
    }

    private void SyncPinnedActionGridSelection()
    {
        var selectedTodos = TaskGrid.SelectedItems.Cast<TodoItem>().ToList();
        if (PinnedActionGrid.SelectionMode == DataGridSelectionMode.Single && selectedTodos.Count > 1)
        {
            selectedTodos = selectedTodos.Take(1).ToList();
        }

        try
        {
            _isSyncingGridSelection = true;

            if (PinnedActionGrid.SelectionMode == DataGridSelectionMode.Single)
            {
                PinnedActionGrid.SelectedItem = selectedTodos.FirstOrDefault();
                return;
            }

            PinnedActionGrid.SelectedItems.Clear();
            foreach (var todo in selectedTodos)
            {
                PinnedActionGrid.SelectedItems.Add(todo);
            }
        }
        finally
        {
            _isSyncingGridSelection = false;
        }
    }

    private void SyncTaskGridSelectionFromPinnedActionGrid()
    {
        var selectedTodos = PinnedActionGrid.SelectedItems.Cast<TodoItem>().ToList();

        try
        {
            _isSyncingGridSelection = true;

            if (TaskGrid.SelectionMode == DataGridSelectionMode.Single)
            {
                TaskGrid.SelectedItem = PinnedActionGrid.SelectedItem as TodoItem;
            }
            else
            {
                TaskGrid.SelectedItems.Clear();
                foreach (var todo in selectedTodos)
                {
                    TaskGrid.SelectedItems.Add(todo);
                }
            }
        }
        finally
        {
            _isSyncingGridSelection = false;
        }

        UpdateSelectionFromTaskGrid();
    }

    private void SelectSingleTodo(TodoItem todo)
    {
        try
        {
            _isSyncingGridSelection = true;
            SelectSingleTodoInGrid(TaskGrid, todo);
            SelectSingleTodoInGrid(PinnedActionGrid, todo);
        }
        finally
        {
            _isSyncingGridSelection = false;
        }

        UpdateSelectionFromTaskGrid();
    }

    private static void SelectSingleTodoInGrid(DataGrid grid, TodoItem todo)
    {
        if (grid.SelectionMode == DataGridSelectionMode.Single)
        {
            grid.SelectedItem = todo;
            return;
        }

        grid.SelectedItems.Clear();
        grid.SelectedItems.Add(todo);
    }

    private void TaskGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        var sortMemberPath = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(sortMemberPath))
        {
            return;
        }

        e.Handled = true;
        if (e.Column.SortDirection == ListSortDirection.Descending)
        {
            _viewModel.ClearManualSort();
            ClearSortIndicators();
            return;
        }

        var direction = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        _viewModel.ApplyManualSort(sortMemberPath, direction);
        ApplySortIndicators(sortMemberPath, direction);
    }

    private void ToggleSubtasksButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TodoItem todo)
        {
            _viewModel.ToggleSubtasks(todo);
        }
    }

    private void TaskGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _taskGridDragStart = null;
        _taskGridDraggedTodo = null;
        if (IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.DataContext is TodoItem todo && todo.Status == TodoStatus.Active)
        {
            _taskGridDragStart = e.GetPosition(TaskGrid);
            _taskGridDraggedTodo = todo;
        }
    }

    private void TaskGrid_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_taskGridDragStart is null || _taskGridDraggedTodo is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(TaskGrid);
        if (Math.Abs(position.X - _taskGridDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _taskGridDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragged = _taskGridDraggedTodo;
        _taskGridDragStart = null;
        _taskGridDraggedTodo = null;
        System.Windows.DragDrop.DoDragDrop(
            TaskGrid,
            new System.Windows.DataObject(typeof(TodoItem), dragged),
            System.Windows.DragDropEffects.Move);
    }

    private void TaskGrid_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(TodoItem)) as TodoItem;
        var target = GetTaskGridDropTarget(e);
        e.Effects = CanReorder(dragged, target) ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void TaskGrid_Drop(object sender, System.Windows.DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(TodoItem)) as TodoItem;
        var target = GetTaskGridDropTarget(e);
        if (!CanReorder(dragged, target))
        {
            return;
        }

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        var insertBefore = row is null || e.GetPosition(row).Y < row.ActualHeight / 2;
        if (_todoService.TryReorderActiveTodo(dragged!.Id, target!.Id, insertBefore))
        {
            _viewModel.SelectedTodo = _viewModel.Todos.FirstOrDefault(todo => todo.Id == dragged.Id);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void AddTodo()
    {
        var editor = new TodoEditorViewModel(_todoService.GetGroups());
        var dialog = new TaskEditWindow(editor) { Owner = this, Title = T("Tray.AddTask") };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _todoService.Create(
            editor.Title,
            editor.Note,
            editor.GetStartDate(),
            editor.GetDueDate(),
            editor.SelectedGroupId,
            editor.GetNewAttachmentPaths());
    }

    private void EditTodo(TodoItem todo)
    {
        var editor = new TodoEditorViewModel(todo.Clone());
        var dialog = new TaskEditWindow(editor) { Owner = this, Title = T("Floating.EditTask") };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        todo.Title = editor.Title;
        todo.Note = editor.Note;
        todo.StartDate = editor.GetStartDate();
        todo.DueDate = editor.GetDueDate();
        _todoService.Update(todo, editor.GetKeptAttachmentIds(), editor.GetNewAttachmentPaths());
    }

    private void AddSubtask(TodoItem parent)
    {
        if (parent.IsSubtask)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.CannotAddSubtask.Title"), T("Dialog.CannotAddSubtask.DepthMessage"));
            return;
        }

        if (parent.Status != TodoStatus.Active)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.CannotAddSubtask.Title"), T("Dialog.CannotAddSubtask.StatusMessage"));
            return;
        }

        var editor = new TodoEditorViewModel(parent, isNewSubtask: true);
        var dialog = new TaskEditWindow(editor) { Owner = this, Title = T("Main.AddSubtask") };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _todoService.CreateSubtask(
            parent.Id,
            editor.Title,
            editor.Note,
            editor.GetStartDate(),
            editor.GetDueDate(),
            editor.GetNewAttachmentPaths());
    }

    private void TaskAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not TodoItem todo || todo.Attachments.Count == 0)
        {
            return;
        }

        e.Handled = true;
        if (todo.Attachments.Count == 1)
        {
            OpenAttachment(todo.Attachments[0]);
            return;
        }

        if (button.ContextMenu is not null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void AttachmentMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TodoAttachment attachment)
        {
            OpenAttachment(attachment);
        }
    }

    private void OpenAttachment(TodoAttachment attachment)
    {
        try
        {
            _todoService.OpenAttachment(attachment);
        }
        catch (InvalidOperationException ex)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.OpenFileFailed.Title"), ex.Message);
        }
    }

    private TodoItem? RequireSelectedTodo()
    {
        if (_viewModel.SelectedTodo is not null)
        {
            return _viewModel.SelectedTodo;
        }

        ConfirmDialogWindow.ShowInfo(this, T("Dialog.NoSelection.Title"), T("Dialog.NoSelection.OneMessage"));
        return null;
    }

    private List<TodoItem> GetSelectedTodos()
    {
        return TaskGrid.SelectedItems.Cast<TodoItem>().ToList();
    }

    private static TodoItem? GetContextMenuTodo(object sender)
    {
        return (sender as FrameworkElement)?.DataContext as TodoItem;
    }

    private void UpdateSelectionFromTaskGrid()
    {
        var selectedTodos = GetSelectedTodos();
        _viewModel.SelectedTodo = selectedTodos.LastOrDefault();
        _viewModel.SelectedCount = selectedTodos.Count;
        _viewModel.SelectedSubtaskCount = selectedTodos.Count(todo => todo.IsSubtask);
    }

    private void ShowFloatingOrOpenManager()
    {
        if (_todoService.GetFloatingTodos().Count == 0)
        {
            OpenFromUserRequest();
            return;
        }

        _floatingWindow.ShowExpandedFromUserRequest();
    }

    private void ApplyStartupBounds()
    {
        var workArea = SystemParameters.WorkArea;
        var desiredWidth = workArea.Width * 2 / 3;
        var desiredHeight = workArea.Height * 2 / 3;

        Width = Math.Min(workArea.Width, Math.Max(MinWidth, desiredWidth));
        Height = Math.Min(workArea.Height, Math.Max(MinHeight, desiredHeight));
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private TodoItem? GetTaskGridDropTarget(System.Windows.DragEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        return row?.DataContext as TodoItem;
    }

    private static bool CanReorder(TodoItem? dragged, TodoItem? target)
    {
        if (dragged is null || target is null || dragged.Id == target.Id)
        {
            return false;
        }

        if (dragged.Status != TodoStatus.Active || target.Status != TodoStatus.Active)
        {
            return false;
        }

        if (dragged.IsSubtask != target.IsSubtask)
        {
            return false;
        }

        return !dragged.IsSubtask || dragged.ParentId == target.ParentId;
    }

    private void ApplySortIndicators(string sortMemberPath, ListSortDirection direction)
    {
        foreach (var column in TaskGrid.Columns)
        {
            column.SortDirection = column.SortMemberPath == sortMemberPath ? direction : null;
        }
    }

    private void ClearSortIndicators()
    {
        foreach (var column in TaskGrid.Columns)
        {
            column.SortDirection = null;
        }
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        return FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null ||
               FindAncestor<System.Windows.Controls.TextBox>(source) is not null ||
               FindAncestor<System.Windows.Controls.ComboBox>(source) is not null;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? source)
        where T : DependencyObject
    {
        if (source is null)
        {
            return null;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(source);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(source, i);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static string T(string key) => LocalizationService.Text(key);

    private static string F(string key, params object?[] args) => LocalizationService.Format(key, args);
}
