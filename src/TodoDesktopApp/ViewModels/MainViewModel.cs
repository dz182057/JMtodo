using System.ComponentModel;
using System.Collections.ObjectModel;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;

namespace TodoDesktopApp.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    public const string DefaultSortMemberPath = nameof(TodoItem.DueDate);

    private readonly TodoService _todoService;
    private readonly HashSet<string> _collapsedTodoIds = new();
    private readonly HashSet<string> _currentRootTodoIds = new();
    private TodoItem? _selectedTodo;
    private int _selectedCount;
    private string? _keyword;
    private string _selectedStatusFilter = "未完成";
    private bool _isMultiSelectMode;
    private bool _includeNoDue = true;
    private DateTime? _startDateFrom;
    private DateTime? _startDateTo;
    private DateTime? _dueDateFrom;
    private DateTime? _dueDateTo;
    private DateTime? _createdAtFrom;
    private DateTime? _createdAtTo;
    private DateTime? _updatedAtFrom;
    private DateTime? _updatedAtTo;
    private string? _manualSortMemberPath;
    private ListSortDirection _manualSortDirection = ListSortDirection.Ascending;

    public MainViewModel(TodoService todoService)
    {
        _todoService = todoService;
        _todoService.Changed += (_, _) => Refresh();
        RefreshCommand = new RelayCommand(_ => Refresh());
        ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
        Refresh();
    }

    public ObservableCollection<TodoItem> Todos { get; } = new();

    public ObservableCollection<TodoGroupSummary> GroupSummaries { get; } = new();

    public string[] StatusFilters { get; } = ["全部", "未完成", "已完成", "已删除"];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ClearFiltersCommand { get; }

    public TodoItem? SelectedTodo
    {
        get => _selectedTodo;
        set
        {
            if (SetProperty(ref _selectedTodo, value))
            {
                OnPropertyChanged(nameof(SelectedCountText));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasSingleSelection));
                OnPropertyChanged(nameof(CanApplyBatchStatusAction));
            }
        }
    }

    public int SelectedCount
    {
        get => _selectedCount;
        set
        {
            if (SetProperty(ref _selectedCount, value))
            {
                OnPropertyChanged(nameof(SelectedCountText));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasSingleSelection));
                OnPropertyChanged(nameof(CanApplyBatchStatusAction));
            }
        }
    }

    public string SelectedCountText => $"已选择 {SelectedCount} 项";

    public string TotalCountText => $"共 {Todos.Count} 项";

    public bool HasSelection => SelectedCount > 0;

    public bool HasSingleSelection => SelectedCount == 1;

    public bool CanApplyBatchStatusAction => HasSelection && SelectedStatusFilter != "已删除";

    public bool IsMultiSelectMode
    {
        get => _isMultiSelectMode;
        set
        {
            if (SetProperty(ref _isMultiSelectMode, value))
            {
                OnPropertyChanged(nameof(MultiSelectButtonText));
            }
        }
    }

    public string MultiSelectButtonText => IsMultiSelectMode ? "取消" : "多选";

    public string? Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                OnPropertyChanged(nameof(CanApplyBatchStatusAction));
            }
        }
    }

    public bool IncludeNoDue
    {
        get => _includeNoDue;
        set => SetProperty(ref _includeNoDue, value);
    }

    public DateTime? StartDateFrom
    {
        get => _startDateFrom;
        set => SetProperty(ref _startDateFrom, value);
    }

    public DateTime? StartDateTo
    {
        get => _startDateTo;
        set => SetProperty(ref _startDateTo, value);
    }

    public DateTime? DueDateFrom
    {
        get => _dueDateFrom;
        set => SetProperty(ref _dueDateFrom, value);
    }

    public DateTime? DueDateTo
    {
        get => _dueDateTo;
        set => SetProperty(ref _dueDateTo, value);
    }

    public DateTime? CreatedAtFrom
    {
        get => _createdAtFrom;
        set => SetProperty(ref _createdAtFrom, value);
    }

    public DateTime? CreatedAtTo
    {
        get => _createdAtTo;
        set => SetProperty(ref _createdAtTo, value);
    }

    public DateTime? UpdatedAtFrom
    {
        get => _updatedAtFrom;
        set => SetProperty(ref _updatedAtFrom, value);
    }

    public DateTime? UpdatedAtTo
    {
        get => _updatedAtTo;
        set => SetProperty(ref _updatedAtTo, value);
    }

    public void Refresh()
    {
        var criteria = BuildCriteria();
        var selectedId = SelectedTodo?.Id;
        var searchResults = SortTodos(_todoService.Search(criteria))
            .Where(todo => SelectedStatusFilter == "已删除" || todo.Status != TodoStatus.Deleted)
            .ToList();
        ApplyHierarchyState(searchResults);

        Todos.Clear();
        foreach (var todo in searchResults.Where(IsVisibleInHierarchy))
        {
            Todos.Add(todo);
        }

        SelectedTodo = Todos.FirstOrDefault(todo => todo.Id == selectedId);
        SelectedCount = SelectedTodo is null ? 0 : 1;
        RefreshGroupSummaries();
        OnPropertyChanged(nameof(TotalCountText));
    }

    public void ApplyManualSort(string memberPath, ListSortDirection direction)
    {
        _manualSortMemberPath = memberPath;
        _manualSortDirection = direction;
        Refresh();
    }

    public void ToggleSubtasks(TodoItem todo)
    {
        if (todo.IsSubtask || !todo.HasSubtasks)
        {
            return;
        }

        if (!_collapsedTodoIds.Add(todo.Id))
        {
            _collapsedTodoIds.Remove(todo.Id);
        }

        Refresh();
    }

    public void ClearFilters()
    {
        Keyword = null;
        SelectedStatusFilter = "未完成";
        IncludeNoDue = true;
        StartDateFrom = null;
        StartDateTo = null;
        DueDateFrom = null;
        DueDateTo = null;
        CreatedAtFrom = null;
        CreatedAtTo = null;
        UpdatedAtFrom = null;
        UpdatedAtTo = null;
        Refresh();
    }

    private TodoSearchCriteria BuildCriteria()
    {
        return new TodoSearchCriteria
        {
            Keyword = Keyword,
            Status = SelectedStatusFilter switch
            {
                "未完成" => TodoStatus.Active,
                "已完成" => TodoStatus.Completed,
                "已删除" => TodoStatus.Deleted,
                _ => null
            },
            IncludeNoDue = IncludeNoDue,
            StartDateFrom = ToDateOnly(StartDateFrom),
            StartDateTo = ToDateOnly(StartDateTo),
            DueDateFrom = ToDateOnly(DueDateFrom),
            DueDateTo = ToDateOnly(DueDateTo),
            CreatedAtFrom = CreatedAtFrom,
            CreatedAtTo = CreatedAtTo,
            UpdatedAtFrom = UpdatedAtFrom,
            UpdatedAtTo = UpdatedAtTo
        };
    }

    private static DateOnly? ToDateOnly(DateTime? value)
    {
        return value.HasValue ? DateOnly.FromDateTime(value.Value) : null;
    }

    private void ApplyHierarchyState(IReadOnlyList<TodoItem> todos)
    {
        _currentRootTodoIds.Clear();
        foreach (var rootId in todos.Where(todo => !todo.IsSubtask).Select(todo => todo.Id))
        {
            _currentRootTodoIds.Add(rootId);
        }

        var rootIdsWithSubtasks = todos
            .Where(todo => todo.IsSubtask && !string.IsNullOrWhiteSpace(todo.ParentId))
            .Select(todo => todo.ParentId!)
            .ToHashSet();

        _collapsedTodoIds.RemoveWhere(id => !rootIdsWithSubtasks.Contains(id));

        foreach (var todo in todos)
        {
            todo.HasSubtasks = !todo.IsSubtask && rootIdsWithSubtasks.Contains(todo.Id);
            todo.IsExpanded = !todo.HasSubtasks || !_collapsedTodoIds.Contains(todo.Id);
        }
    }

    private bool IsVisibleInHierarchy(TodoItem todo)
    {
        return !todo.IsSubtask ||
               string.IsNullOrWhiteSpace(todo.ParentId) ||
               !_currentRootTodoIds.Contains(todo.ParentId) ||
               !_collapsedTodoIds.Contains(todo.ParentId);
    }

    private IReadOnlyList<TodoItem> SortTodos(IReadOnlyList<TodoItem> todos)
    {
        if (_manualSortMemberPath is not null)
        {
            return SortByManualField(todos, _manualSortMemberPath, _manualSortDirection);
        }

        return todos
            .OrderBy(todo => todo.DueDate.HasValue ? 0 : 1)
            .ThenBy(todo => todo.DueDate)
            .ThenBy(todo => todo.StartDate)
            .ThenBy(todo => todo.CreatedAt)
            .ToList();
    }

    private static IReadOnlyList<TodoItem> SortByManualField(
        IReadOnlyList<TodoItem> todos,
        string memberPath,
        ListSortDirection direction)
    {
        var ascending = direction == ListSortDirection.Ascending;

        return memberPath switch
        {
            nameof(TodoItem.Title) => SortByString(todos, todo => todo.Title, ascending),
            nameof(TodoItem.IsSubtask) => SortByComparable(todos, todo => todo.IsSubtask ? 1 : 0, ascending),
            nameof(TodoItem.GroupDisplayText) => SortByString(todos, todo => todo.GroupDisplayText, ascending),
            nameof(TodoItem.Note) => SortByString(todos, todo => todo.Note, ascending),
            nameof(TodoItem.Status) => SortByComparable(todos, todo => StatusSortValue(todo.Status), ascending),
            nameof(TodoItem.StartDate) => SortByComparable(todos, todo => todo.StartDate, ascending),
            nameof(TodoItem.DueDate) => SortByNullableDate(todos, todo => todo.DueDate, ascending),
            nameof(TodoItem.CreatedAt) => SortByComparable(todos, todo => todo.CreatedAt, ascending),
            nameof(TodoItem.UpdatedAt) => SortByComparable(todos, todo => todo.UpdatedAt, ascending),
            nameof(TodoItem.CompletedAt) => SortByNullableDateTime(todos, todo => todo.CompletedAt, ascending),
            nameof(TodoItem.DeletedAt) => SortByNullableDateTime(todos, todo => todo.DeletedAt, ascending),
            _ => todos.ToList()
        };
    }

    private static IReadOnlyList<TodoItem> SortByComparable<TKey>(
        IReadOnlyList<TodoItem> todos,
        Func<TodoItem, TKey> selector,
        bool ascending)
    {
        return ascending
            ? todos.OrderBy(selector).ToList()
            : todos.OrderByDescending(selector).ToList();
    }

    private static IReadOnlyList<TodoItem> SortByString(
        IReadOnlyList<TodoItem> todos,
        Func<TodoItem, string?> selector,
        bool ascending)
    {
        return ascending
            ? todos
                .OrderBy(todo => string.IsNullOrWhiteSpace(selector(todo)) ? 1 : 0)
                .ThenBy(selector, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
            : todos
                .OrderBy(todo => string.IsNullOrWhiteSpace(selector(todo)) ? 1 : 0)
                .ThenByDescending(selector, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
    }

    private static IReadOnlyList<TodoItem> SortByNullableDate(
        IReadOnlyList<TodoItem> todos,
        Func<TodoItem, DateOnly?> selector,
        bool ascending)
    {
        return ascending
            ? todos
                .OrderBy(todo => selector(todo).HasValue ? 0 : 1)
                .ThenBy(selector)
                .ToList()
            : todos
                .OrderBy(todo => selector(todo).HasValue ? 0 : 1)
                .ThenByDescending(selector)
                .ToList();
    }

    private static IReadOnlyList<TodoItem> SortByNullableDateTime(
        IReadOnlyList<TodoItem> todos,
        Func<TodoItem, DateTime?> selector,
        bool ascending)
    {
        return ascending
            ? todos
                .OrderBy(todo => selector(todo).HasValue ? 0 : 1)
                .ThenBy(selector)
                .ToList()
            : todos
                .OrderBy(todo => selector(todo).HasValue ? 0 : 1)
                .ThenByDescending(selector)
                .ToList();
    }

    private static int StatusSortValue(TodoStatus status)
    {
        return status switch
        {
            TodoStatus.Active => 0,
            TodoStatus.Completed => 1,
            TodoStatus.Deleted => 2,
            _ => 3
        };
    }

    private void RefreshGroupSummaries()
    {
        GroupSummaries.Clear();

        var groups = _todoService.GetGroups();
        var activeItems = _todoService.Search(new TodoSearchCriteria { IncludeNoDue = true })
            .Where(todo => todo.Status != TodoStatus.Deleted)
            .ToList();

        foreach (var group in groups)
        {
            var groupItems = activeItems.Where(todo => todo.GroupId == group.Id).ToList();
            GroupSummaries.Add(new TodoGroupSummary(
                group.Name,
                group.IconKey,
                groupItems.Count,
                groupItems.Count(todo => todo.Status == TodoStatus.Active)));
        }

        var noGroupItems = activeItems.Where(todo => string.IsNullOrWhiteSpace(todo.GroupId)).ToList();
        if (noGroupItems.Count > 0)
        {
            GroupSummaries.Add(new TodoGroupSummary(
                "未分组",
                "inbox",
                noGroupItems.Count,
                noGroupItems.Count(todo => todo.Status == TodoStatus.Active)));
        }
    }
}

public sealed class TodoGroupSummary
{
    public TodoGroupSummary(string name, string iconKey, int totalCount, int activeCount)
    {
        Name = name;
        IconKey = string.IsNullOrWhiteSpace(iconKey) ? "folder" : iconKey;
        TotalCount = totalCount;
        ActiveCount = activeCount;
    }

    public string Name { get; }

    public string IconKey { get; }

    public int TotalCount { get; }

    public int ActiveCount { get; }

    public string CountText => $"未完成 {ActiveCount} / 共 {TotalCount}";

    public System.Windows.Media.Geometry IconGeometry => IconOption.Geometry;

    public System.Windows.Media.Brush IconForeground => IconOption.Foreground;

    public System.Windows.Media.Brush IconBackground => IconOption.Background;

    private TaskGroupIconOption IconOption => TaskGroupIconCatalog.Get(IconKey);
}
