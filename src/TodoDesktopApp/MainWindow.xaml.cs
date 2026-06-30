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
    private bool _wasMinimized;
    private bool _suppressFloatingOnRestore;
    private System.Windows.Point? _taskGridDragStart;
    private TodoItem? _taskGridDraggedTodo;

    public MainWindow(TodoService todoService, FloatingTaskWindow floatingWindow, WindowLevelService windowLevelService)
    {
        InitializeComponent();
        ApplyStartupBounds();
        _todoService = todoService;
        _floatingWindow = floatingWindow;
        _windowLevelService = windowLevelService;
        _viewModel = new MainViewModel(todoService);
        DataContext = _viewModel;
        ApplySortIndicators(MainViewModel.DefaultSortMemberPath, ListSortDirection.Ascending);
    }

    public void OpenFromUserRequest()
    {
        if (!IsVisible)
        {
            Show();
        }

        _suppressFloatingOnRestore = WindowState == WindowState.Minimized;
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

    private void ToggleMultiSelectButton_Click(object sender, RoutedEventArgs e)
    {
        var enableMultiSelect = !_viewModel.IsMultiSelectMode;
        var selectedTodo = TaskGrid.SelectedItems.Cast<TodoItem>().FirstOrDefault();

        if (!enableMultiSelect)
        {
            TaskGrid.SelectedItems.Clear();
        }

        _viewModel.IsMultiSelectMode = enableMultiSelect;
        SelectionColumn.Visibility = enableMultiSelect ? Visibility.Visible : Visibility.Collapsed;
        TaskGrid.SelectionMode = enableMultiSelect ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single;

        if (!enableMultiSelect && selectedTodo is not null)
        {
            TaskGrid.SelectedItem = selectedTodo;
        }

        _viewModel.SelectedCount = TaskGrid.SelectedItems.Count;
    }

    private void SoftDeleteTodo(TodoItem todo)
    {
        var dialog = new ConfirmDialogWindow
        {
            Owner = this,
            TitleText = "确认删除",
            MessageText = "确定要删除这个任务吗？任务会移入已删除列表。",
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
        var dialog = new ConfirmDialogWindow
        {
            Owner = this,
            TitleText = "彻底删除",
            MessageText = "确定要彻底删除这个任务吗？此操作不可恢复。",
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
        _viewModel.SelectedCount = TaskGrid.SelectedItems.Count;
    }

    private void TaskGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        var sortMemberPath = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(sortMemberPath))
        {
            return;
        }

        e.Handled = true;
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

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            _wasMinimized = true;
            return;
        }

        if (_wasMinimized && WindowState == WindowState.Normal)
        {
            _wasMinimized = false;
            if (_suppressFloatingOnRestore)
            {
                _suppressFloatingOnRestore = false;
                return;
            }

            ShowFloatingOrOpenManager();
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
            ConfirmDialogWindow.ShowInfo(this, "不能添加子任务", "只有未完成的一级任务可以添加子任务。");
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
}
