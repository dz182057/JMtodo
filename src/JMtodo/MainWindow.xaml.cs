using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TodoDesktopApp.Dialogs;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;
using TodoDesktopApp.ViewModels;
using TodoDesktopApp.Views;

namespace TodoDesktopApp;

public partial class MainWindow : Window
{
    private readonly TodoService _todoService;
    private readonly FloatingTaskWindow _floatingWindow;
    private readonly WindowLevelService _windowLevelService;
    private readonly MainViewModel _viewModel;
    private bool _allowClose;
    private System.Windows.Point? _taskGridDragStart;
    private TodoItem? _taskGridDraggedTodo;
    private ScrollViewer? _taskGridScrollViewer;
    private ScrollViewer? _pinnedActionGridScrollViewer;
    private bool _isSyncingGridScroll;
    private bool _isSyncingGridSelection;

    public MainWindow(TodoService todoService, FloatingTaskWindow floatingWindow, WindowLevelService windowLevelService)
    {
        InitializeComponent();
        ApplyStartupBounds();
        _todoService = todoService;
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

    private void ManageGroupsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TaskGroupManagerWindow(_todoService) { Owner = this };
        dialog.ShowDialog();
        _viewModel.Refresh();
    }

    private void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedTodos = GetSelectedTodos();
        if (selectedTodos.Count == 0)
        {
            ConfirmDialogWindow.ShowInfo(this, "没有选中任务", "请先选择任务。");
            return;
        }

        foreach (var todo in selectedTodos)
        {
            _todoService.Complete(todo.Id);
        }
    }

    private void ReopenButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedTodos = GetSelectedTodos();
        if (selectedTodos.Count == 0)
        {
            ConfirmDialogWindow.ShowInfo(this, "没有选中任务", "请先选择任务。");
            return;
        }

        foreach (var todo in selectedTodos)
        {
            _todoService.Reopen(todo.Id);
        }
    }

    private void MoveToGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedTodos = GetSelectedTodos();
        if (selectedTodos.Count == 0)
        {
            ConfirmDialogWindow.ShowInfo(this, "没有选中任务", "请先选择一个或多个主任务。");
            return;
        }

        if (selectedTodos.Any(todo => todo.IsSubtask))
        {
            ConfirmDialogWindow.ShowInfo(this, "不能移动子任务", "移动到任务组只支持主任务，请取消勾选子任务后再操作。");
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
            TitleText = "确认移动",
            MessageText = $"确定将 {selectedTodos.Count} 个主任务移动到「{dialog.TargetGroupName}」吗？\n这些主任务下的子任务会一起移动。",
            ConfirmText = "确认移动",
            CancelText = "取消"
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
            ConfirmDialogWindow.ShowInfo(this, "移动失败", ex.Message);
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
            ? "确定要删除这个任务吗？任务会移入已删除列表。"
            : "确定要删除这个主任务吗？它对应的所有子任务也会一起移入已删除列表。";
        var dialog = new ConfirmDialogWindow
        {
            Owner = this,
            TitleText = "确认删除",
            MessageText = messageText,
            ConfirmText = "确认删除",
            CancelText = "取消"
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
            TitleText = "确认恢复",
            MessageText = "确定要恢复这个任务吗？恢复后会回到未完成状态。",
            ConfirmText = "确认恢复",
            CancelText = "取消"
        };

        if (dialog.ShowDialog() == true)
        {
            _todoService.Restore(todo.Id);
        }
    }

    private void PermanentDeleteTodo(TodoItem todo)
    {
        var messageText = todo.IsSubtask
            ? "确定要彻底删除这个任务吗？此操作不可恢复。"
            : "确定要彻底删除这个主任务吗？它对应的所有子任务也会一起彻底删除，此操作不可恢复。";
        var dialog = new ConfirmDialogWindow
        {
            Owner = this,
            TitleText = "彻底删除",
            MessageText = messageText,
            ConfirmText = "确认删除",
            CancelText = "取消"
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
        SyncScrollViewer(_pinnedActionGridScrollViewer, _taskGridScrollViewer.VerticalOffset);
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
        SyncScrollViewer(_pinnedActionGridScrollViewer, _taskGridScrollViewer?.VerticalOffset ?? 0);
        SyncPinnedActionGridSelection();
    }

    private void TaskGridScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalChange) < double.Epsilon &&
            Math.Abs(e.ExtentHeightChange) < double.Epsilon &&
            Math.Abs(e.ViewportHeightChange) < double.Epsilon)
        {
            return;
        }

        SyncScrollViewer(_pinnedActionGridScrollViewer, e.VerticalOffset);
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
        var dialog = new TaskEditWindow(editor) { Owner = this, Title = "新增任务" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _todoService.Create(editor.Title, editor.Note, editor.GetStartDate(), editor.GetDueDate(), editor.SelectedGroupId);
    }

    private void EditTodo(TodoItem todo)
    {
        var editor = new TodoEditorViewModel(todo.Clone());
        var dialog = new TaskEditWindow(editor) { Owner = this, Title = "编辑任务" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        todo.Title = editor.Title;
        todo.Note = editor.Note;
        todo.StartDate = editor.GetStartDate();
        todo.DueDate = editor.GetDueDate();
        _todoService.Update(todo);
    }

    private void AddSubtask(TodoItem parent)
    {
        if (parent.IsSubtask)
        {
            ConfirmDialogWindow.ShowInfo(this, "不能添加子任务", "暂时只支持二级子任务，不能继续添加下一级。");
            return;
        }

        if (parent.Status != TodoStatus.Active)
        {
            ConfirmDialogWindow.ShowInfo(this, "不能添加子任务", "只有未完成的主任务可以添加子任务。");
            return;
        }

        var editor = new TodoEditorViewModel(parent, isNewSubtask: true);
        var dialog = new TaskEditWindow(editor) { Owner = this, Title = "新增子任务" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _todoService.CreateSubtask(parent.Id, editor.Title, editor.Note, editor.GetStartDate(), editor.GetDueDate());
    }

    private TodoItem? RequireSelectedTodo()
    {
        if (_viewModel.SelectedTodo is not null)
        {
            return _viewModel.SelectedTodo;
        }

        ConfirmDialogWindow.ShowInfo(this, "没有选中任务", "请先选择一个任务。");
        return null;
    }

    private List<TodoItem> GetSelectedTodos()
    {
        return TaskGrid.SelectedItems.Cast<TodoItem>().ToList();
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
}
