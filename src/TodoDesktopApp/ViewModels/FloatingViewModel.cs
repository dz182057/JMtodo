using System.Collections.ObjectModel;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;

namespace TodoDesktopApp.ViewModels;

public sealed class FloatingViewModel : ViewModelBase
{
    private readonly TodoService _todoService;
    private string _quickTitle = string.Empty;
    private string? _errorMessage;

    public FloatingViewModel(TodoService todoService)
    {
        _todoService = todoService;
        AddQuickTodoCommand = new RelayCommand(_ => AddQuickTodo(), _ => CanAddQuickTodo);
        _todoService.Changed += (_, _) => Reload();
        Reload();
    }

    public event EventHandler? VisibleTasksChanged;

    public ObservableCollection<FloatingTaskItemViewModel> Todos { get; } = new();

    public RelayCommand AddQuickTodoCommand { get; }

    public string QuickTitle
    {
        get => _quickTitle;
        set
        {
            if (SetProperty(ref _quickTitle, value))
            {
                OnPropertyChanged(nameof(CanAddQuickTodo));
                AddQuickTodoCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanAddQuickTodo => !string.IsNullOrWhiteSpace(QuickTitle);

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string TotalCountText => $"共 {Todos.Count} 项任务";

    public string CompletedCountText => $"完成 {Todos.Count(todo => todo.IsCompleted)} 项";

    public IReadOnlyList<TodoGroup> GetGroups() => _todoService.GetGroups();

    public TodoGroup CreateGroup(string name) => _todoService.CreateGroup(name);

    public void Reload()
    {
        Todos.Clear();
        foreach (var todo in _todoService.GetFloatingTodos())
        {
            Todos.Add(new FloatingTaskItemViewModel(todo));
        }

        OnPropertyChanged(nameof(TotalCountText));
        OnPropertyChanged(nameof(CompletedCountText));
        VisibleTasksChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Complete(FloatingTaskItemViewModel todo)
    {
        _todoService.Complete(todo.TaskId);
    }

    public void Reopen(FloatingTaskItemViewModel todo)
    {
        _todoService.Reopen(todo.TaskId);
    }

    public void CreateSubtask(FloatingTaskItemViewModel parent, string title, string? note, DateOnly startDate, DateOnly? dueDate)
    {
        _todoService.CreateSubtask(parent.TaskId, title, note, startDate, dueDate);
    }

    public bool TryReorder(string draggedId, string targetId, bool insertBefore)
    {
        return _todoService.TryReorderActiveTodo(draggedId, targetId, insertBefore);
    }

    private void AddQuickTodo()
    {
        if (string.IsNullOrWhiteSpace(QuickTitle))
        {
            ErrorMessage = "请输入任务标题。";
            return;
        }

        _todoService.Create(QuickTitle, null, DateOnly.FromDateTime(DateTime.Now), null);
        QuickTitle = string.Empty;
        ErrorMessage = null;
    }
}

public sealed class FloatingTaskItemViewModel
{
    public FloatingTaskItemViewModel(TodoItem sourceTask)
    {
        SourceTask = sourceTask;
        TaskId = sourceTask.Id;
        ParentTaskId = sourceTask.ParentId;
        Title = sourceTask.Title;
        DisplayTitle = sourceTask.IsSubtask ? $"└  {sourceTask.Title}" : sourceTask.Title;
        IsSubTask = sourceTask.IsSubtask;
        ParentTitle = sourceTask.ParentTitle;
    }

    public string TaskId { get; }

    public string? ParentTaskId { get; }

    public string Title { get; }

    public string DisplayTitle { get; }

    public bool IsSubTask { get; }

    public string? ParentTitle { get; }

    public TodoItem SourceTask { get; }

    public string? Note => SourceTask.Note;

    public DateOnly StartDate => SourceTask.StartDate;

    public DateOnly? DueDate => SourceTask.DueDate;

    public bool IsCompleted => SourceTask.IsCompleted;

    public bool CanCreateSubtask => SourceTask.CanCreateSubtask;

    public TodoStatus Status => SourceTask.Status;

    public string FloatingRelationText => SourceTask.FloatingRelationText;

    public string FloatingStatusText => SourceTask.FloatingStatusText;

    public string FloatingDateText => SourceTask.FloatingDateText;

    public bool HasNote => SourceTask.HasNote;
}
