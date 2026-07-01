using System.Collections.ObjectModel;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;

namespace TodoDesktopApp.ViewModels;

public sealed class FloatingViewModel : ViewModelBase
{
    private readonly TodoService _todoService;
    private readonly Dictionary<string, FloatingTaskItemViewModel> _completingTodos = new();

    public FloatingViewModel(TodoService todoService)
    {
        _todoService = todoService;
        _todoService.Changed += (_, _) => Reload();
        LocalizationService.LanguageChanged += (_, _) => Reload();
        Reload();
    }

    public event EventHandler? VisibleTasksChanged;

    public ObservableCollection<FloatingTaskItemViewModel> Todos { get; } = new();

    public ObservableCollection<FloatingTaskGroupViewModel> TaskGroups { get; } = new();

    public string TotalCountText => LocalizationService.Format("Count.TotalTasksFormat", Todos.Count);

    public string CompletedCountText => LocalizationService.Format("Count.CompletedTasksFormat", Todos.Count(todo => todo.IsCompleted));

    public IReadOnlyList<TodoGroup> GetGroups() => _todoService.GetGroups();

    public TodoGroup CreateGroup(string name) => _todoService.CreateGroup(name);

    public void Reload()
    {
        var previousTodos = Todos.ToList();
        var groupsById = _todoService.GetGroups().ToDictionary(group => group.Id);
        var floatingTodos = _todoService.GetFloatingTodos()
            .Select(todo => new FloatingTaskItemViewModel(todo, ResolveIconKey(todo, groupsById)))
            .ToList();
        floatingTodos = KeepCompletingTodos(previousTodos, floatingTodos);

        Todos.Clear();
        TaskGroups.Clear();
        foreach (var todo in floatingTodos)
        {
            Todos.Add(todo);
        }

        foreach (var group in BuildTaskGroups(floatingTodos))
        {
            TaskGroups.Add(group);
        }

        OnPropertyChanged(nameof(TotalCountText));
        OnPropertyChanged(nameof(CompletedCountText));
        OnPropertyChanged(nameof(TaskGroups));
        VisibleTasksChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Complete(FloatingTaskItemViewModel todo, bool keepVisibleForAnimation = false)
    {
        if (!keepVisibleForAnimation)
        {
            _todoService.Complete(todo.TaskId);
            return;
        }

        todo.IsCompleting = true;
        _completingTodos[todo.TaskId] = todo;
        try
        {
            _todoService.Complete(todo.TaskId);
        }
        catch
        {
            _completingTodos.Remove(todo.TaskId);
            todo.IsCompleting = false;
            throw;
        }
    }

    public void FinishCompletionAnimation(string taskId)
    {
        _completingTodos.Remove(taskId);
        Reload();
    }

    public void Reopen(FloatingTaskItemViewModel todo)
    {
        _todoService.Reopen(todo.TaskId);
    }

    public void Create(
        string title,
        string? note,
        DateOnly startDate,
        DateOnly? dueDate,
        string? groupId,
        IEnumerable<string>? attachmentFilePaths)
    {
        _todoService.Create(title, note, startDate, dueDate, groupId, attachmentFilePaths);
    }

    public void CreateSubtask(
        FloatingTaskItemViewModel parent,
        string title,
        string? note,
        DateOnly startDate,
        DateOnly? dueDate,
        IEnumerable<string>? attachmentFilePaths)
    {
        _todoService.CreateSubtask(parent.TaskId, title, note, startDate, dueDate, attachmentFilePaths);
    }

    public void Update(
        FloatingTaskItemViewModel todo,
        string title,
        string? note,
        DateOnly startDate,
        DateOnly? dueDate,
        IReadOnlyCollection<string>? keptAttachmentIds,
        IEnumerable<string>? newAttachmentFilePaths)
    {
        var item = todo.SourceTask.Clone();
        item.Title = title;
        item.Note = note;
        item.StartDate = startDate;
        item.DueDate = dueDate;
        _todoService.Update(item, keptAttachmentIds, newAttachmentFilePaths);
    }

    public void OpenAttachment(TodoAttachment attachment)
    {
        _todoService.OpenAttachment(attachment);
    }

    public bool TryReorder(string draggedId, string targetId, bool insertBefore)
    {
        return _todoService.TryReorderActiveTodo(draggedId, targetId, insertBefore);
    }

    private static IReadOnlyList<FloatingTaskGroupViewModel> BuildTaskGroups(IReadOnlyList<FloatingTaskItemViewModel> todos)
    {
        var groupsByTaskId = new Dictionary<string, FloatingTaskGroupViewModel>();
        var result = new List<FloatingTaskGroupViewModel>();

        foreach (var todo in todos.Where(todo => !todo.IsSubTask))
        {
            var group = new FloatingTaskGroupViewModel(todo);
            groupsByTaskId[todo.TaskId] = group;
            result.Add(group);
        }

        foreach (var todo in todos.Where(todo => todo.IsSubTask))
        {
            if (todo.ParentTaskId is not null && groupsByTaskId.TryGetValue(todo.ParentTaskId, out var parentGroup))
            {
                parentGroup.Subtasks.Add(todo);
                continue;
            }

            result.Add(new FloatingTaskGroupViewModel(todo));
        }

        foreach (var group in result)
        {
            for (var i = 0; i < group.Subtasks.Count; i++)
            {
                group.Subtasks[i].IsLastSubtask = i == group.Subtasks.Count - 1;
            }
        }

        return result;
    }

    private List<FloatingTaskItemViewModel> KeepCompletingTodos(
        IReadOnlyList<FloatingTaskItemViewModel> previousTodos,
        List<FloatingTaskItemViewModel> currentTodos)
    {
        if (_completingTodos.Count == 0)
        {
            return currentTodos;
        }

        var currentIds = currentTodos.Select(todo => todo.TaskId).ToHashSet();
        var previousIndex = previousTodos
            .Select((todo, index) => new { todo.TaskId, Index = index })
            .ToDictionary(item => item.TaskId, item => item.Index);
        var currentIndex = currentTodos
            .Select((todo, index) => new { todo.TaskId, Index = index })
            .ToDictionary(item => item.TaskId, item => item.Index);

        foreach (var todo in previousTodos.Where(todo => _completingTodos.ContainsKey(todo.TaskId) && !currentIds.Contains(todo.TaskId)))
        {
            todo.IsCompleting = true;
            currentTodos.Add(todo);
        }

        return currentTodos
            .OrderBy(todo => previousIndex.TryGetValue(todo.TaskId, out var index) ? index : int.MaxValue)
            .ThenBy(todo => currentIndex.TryGetValue(todo.TaskId, out var index) ? index : int.MaxValue)
            .ToList();
    }

    private static string ResolveIconKey(TodoItem todo, IReadOnlyDictionary<string, TodoGroup> groupsById)
    {
        if (!string.IsNullOrWhiteSpace(todo.GroupId) &&
            groupsById.TryGetValue(todo.GroupId, out var group) &&
            !string.IsNullOrWhiteSpace(group.IconKey))
        {
            return group.IconKey;
        }

        return "inbox";
    }
}

public sealed class FloatingTaskGroupViewModel
{
    public FloatingTaskGroupViewModel(FloatingTaskItemViewModel mainTask)
    {
        MainTask = mainTask;
    }

    public FloatingTaskItemViewModel MainTask { get; }

    public ObservableCollection<FloatingTaskItemViewModel> Subtasks { get; } = new();

    public bool HasSubtasks => Subtasks.Count > 0;

    public int TotalSubtaskCount => Subtasks.Count;

    public int CompletedSubtaskCount => Subtasks.Count(todo => todo.IsCompleted);

    public string ProgressText => HasSubtasks ? $"{CompletedSubtaskCount}/{TotalSubtaskCount}" : string.Empty;
}

public sealed class FloatingTaskItemViewModel : ViewModelBase
{
    private bool _isCompleting;

    public FloatingTaskItemViewModel(TodoItem sourceTask, string iconKey)
    {
        SourceTask = sourceTask;
        TaskId = sourceTask.Id;
        ParentTaskId = sourceTask.ParentId;
        Title = sourceTask.Title;
        DisplayTitle = sourceTask.Title;
        IsSubTask = sourceTask.IsSubtask;
        ParentTitle = sourceTask.ParentTitle;
        IconKey = string.IsNullOrWhiteSpace(iconKey) ? "inbox" : iconKey;
    }

    public string TaskId { get; }

    public string? ParentTaskId { get; }

    public string Title { get; }

    public string DisplayTitle { get; }

    public bool IsSubTask { get; }

    public bool IsLastSubtask { get; internal set; }

    public string? ParentTitle { get; }

    public string IconKey { get; }

    public TodoItem SourceTask { get; }

    public string? Note => SourceTask.Note;

    public DateOnly StartDate => SourceTask.StartDate;

    public DateOnly? DueDate => SourceTask.DueDate;

    public bool IsCompleted => SourceTask.IsCompleted || IsCompleting;

    public bool CanCreateSubtask => !IsCompleting && SourceTask.CanCreateSubtask;

    public TodoStatus Status => IsCompleting ? TodoStatus.Completed : SourceTask.Status;

    public bool IsCompleting
    {
        get => _isCompleting;
        internal set
        {
            if (SetProperty(ref _isCompleting, value))
            {
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(CanCreateSubtask));
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public string FloatingRelationText => SourceTask.FloatingRelationText;

    public string FloatingDateText => SourceTask.FloatingDateText;

    public bool HasNote => SourceTask.HasNote;

    public bool HasAttachments => SourceTask.HasAttachments;

    public IReadOnlyList<TodoAttachment> Attachments => SourceTask.Attachments;

    public System.Windows.Media.Geometry IconGeometry => IconOption.Geometry;

    public System.Windows.Media.Brush IconForeground => IconOption.Foreground;

    public System.Windows.Media.Brush IconBackground => IconOption.Background;

    private TaskGroupIconOption IconOption => TaskGroupIconCatalog.Get(IconKey);
}
