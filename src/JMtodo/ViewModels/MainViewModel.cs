using System.ComponentModel;
using System.Collections.ObjectModel;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;

namespace TodoDesktopApp.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    public const string DefaultSortMemberPath = nameof(TodoItem.DueDate);
    private const string StatusFilterAll = "All";
    private const string StatusFilterActive = "Active";
    private const string StatusFilterCompleted = "Completed";
    private const string StatusFilterDeleted = "Deleted";

    private readonly TodoService _todoService;
    private readonly HashSet<string> _collapsedTodoIds = new();
    private readonly HashSet<string> _currentRootTodoIds = new();
    private TodoItem? _selectedTodo;
    private int _selectedCount;
    private int _selectedSubtaskCount;
    private string? _keyword;
    private string _selectedStatusFilter = StatusFilterActive;
    private string? _selectedGroupId;
    private bool _filterNoGroup;
    private bool _isUpdatingFilters;
    private bool _isMultiSelectMode;
    private bool _includeNoDue = true;
    private bool _showStartDateFilter;
    private bool _showDueDateFilter = true;
    private bool _showCreatedAtFilter;
    private bool _showUpdatedAtFilter;
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
        LocalizationService.LanguageChanged += (_, _) => RefreshLocalizedProperties();
        LoadStatusFilters();
        RefreshCommand = new RelayCommand(_ => Refresh());
        ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
        Refresh();
    }

    public ObservableCollection<TodoItem> Todos { get; } = new();

    public ObservableCollection<TodoGroupSummary> GroupSummaries { get; } = new();

    public ObservableCollection<StatusFilterOption> StatusFilters { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ClearFiltersCommand { get; }

    public TodoItem? SelectedTodo
    {
        get => _selectedTodo;
        set
        {
            if (EqualityComparer<TodoItem?>.Default.Equals(_selectedTodo, value))
            {
                return;
            }

            _selectedTodo = value;
            if (!IsMultiSelectMode)
            {
                OnPropertyChanged();
            }

            OnPropertyChanged(nameof(SelectedCountText));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasSingleSelection));
            OnPropertyChanged(nameof(CanAddSubtask));
            OnPropertyChanged(nameof(CanApplyBatchStatusAction));
            OnPropertyChanged(nameof(CanMoveSelectedTodosToGroup));
            OnPropertyChanged(nameof(ShowMoveToGroupButton));
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
                OnPropertyChanged(nameof(CanAddSubtask));
                OnPropertyChanged(nameof(CanApplyBatchStatusAction));
                OnPropertyChanged(nameof(CanMoveSelectedTodosToGroup));
                OnPropertyChanged(nameof(ShowMoveToGroupButton));
            }
        }
    }

    public int SelectedSubtaskCount
    {
        get => _selectedSubtaskCount;
        set
        {
            if (SetProperty(ref _selectedSubtaskCount, value))
            {
                OnPropertyChanged(nameof(HasSelectedSubtask));
                OnPropertyChanged(nameof(CanAddSubtask));
                OnPropertyChanged(nameof(CanMoveSelectedTodosToGroup));
                OnPropertyChanged(nameof(ShowMoveToGroupButton));
            }
        }
    }

    public string SelectedCountText => LocalizationService.Format("Count.SelectedFormat", SelectedCount);

    public string TotalCountText => LocalizationService.Format("Count.TotalFormat", Todos.Count);

    public bool HasSelection => SelectedCount > 0;

    public bool HasSingleSelection => SelectedCount == 1;

    public bool HasSelectedSubtask => SelectedSubtaskCount > 0;

    public bool CanAddSubtask => SelectedCount == 1 && !HasSelectedSubtask && SelectedTodo is { IsSubtask: false, Status: TodoStatus.Active };

    public bool CanApplyBatchStatusAction => HasSelection && SelectedStatusFilter != StatusFilterDeleted;

    public bool CanMoveSelectedTodosToGroup => HasSelection && !HasSelectedSubtask;

    public bool ShowMoveToGroupButton => !HasSelectedSubtask;

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

    public string MultiSelectButtonText => IsMultiSelectMode
        ? LocalizationService.Text("Action.Cancel")
        : LocalizationService.Text("Action.MultiSelect");

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
            var nextFilter = NormalizeStatusFilter(value);
            if (SetProperty(ref _selectedStatusFilter, nextFilter))
            {
                OnPropertyChanged(nameof(CanApplyBatchStatusAction));
                if (!_isUpdatingFilters)
                {
                    Refresh();
                }
            }
        }
    }

    public bool IncludeNoDue
    {
        get => _includeNoDue;
        set => SetProperty(ref _includeNoDue, value);
    }

    public bool ShowStartDateFilter
    {
        get => _showStartDateFilter;
        set => SetProperty(ref _showStartDateFilter, value);
    }

    public bool ShowDueDateFilter
    {
        get => _showDueDateFilter;
        set => SetProperty(ref _showDueDateFilter, value);
    }

    public bool ShowCreatedAtFilter
    {
        get => _showCreatedAtFilter;
        set => SetProperty(ref _showCreatedAtFilter, value);
    }

    public bool ShowUpdatedAtFilter
    {
        get => _showUpdatedAtFilter;
        set => SetProperty(ref _showUpdatedAtFilter, value);
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
        var sortedResults = SortTodos(_todoService.Search(criteria))
            .Where(todo => SelectedStatusFilter == StatusFilterDeleted || todo.Status != TodoStatus.Deleted)
            .ToList();
        var searchResults = ArrangeByHierarchy(sortedResults);
        ApplyHierarchyState(searchResults);

        Todos.Clear();
        foreach (var todo in searchResults.Where(IsVisibleInHierarchy))
        {
            Todos.Add(todo);
        }

        SelectedTodo = Todos.FirstOrDefault(todo => todo.Id == selectedId);
        SelectedCount = SelectedTodo is null ? 0 : 1;
        SelectedSubtaskCount = SelectedTodo?.IsSubtask == true ? 1 : 0;
        RefreshGroupSummaries();
        OnPropertyChanged(nameof(TotalCountText));
    }

    public void ApplyManualSort(string memberPath, ListSortDirection direction)
    {
        _manualSortMemberPath = memberPath;
        _manualSortDirection = direction;
        Refresh();
    }

    public void ClearManualSort()
    {
        _manualSortMemberPath = null;
        _manualSortDirection = ListSortDirection.Ascending;
        Refresh();
    }

    public void ToggleGroupFilter(TodoGroupSummary summary)
    {
        if (summary.IsNoGroup)
        {
            _filterNoGroup = !_filterNoGroup;
            _selectedGroupId = null;
        }
        else
        {
            var isSelected = _selectedGroupId == summary.GroupId;
            _selectedGroupId = isSelected ? null : summary.GroupId;
            _filterNoGroup = false;
        }

        _isUpdatingFilters = true;
        try
        {
            SelectedStatusFilter = StatusFilterActive;
        }
        finally
        {
            _isUpdatingFilters = false;
        }

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
        _isUpdatingFilters = true;
        try
        {
            Keyword = null;
            SelectedStatusFilter = StatusFilterAll;
            _selectedGroupId = null;
            _filterNoGroup = false;
            IncludeNoDue = true;
            StartDateFrom = null;
            StartDateTo = null;
            DueDateFrom = null;
            DueDateTo = null;
            CreatedAtFrom = null;
            CreatedAtTo = null;
            UpdatedAtFrom = null;
            UpdatedAtTo = null;
        }
        finally
        {
            _isUpdatingFilters = false;
        }

        Refresh();
    }

    private TodoSearchCriteria BuildCriteria()
    {
        return new TodoSearchCriteria
        {
            Keyword = Keyword,
            Status = SelectedStatusFilter switch
            {
                StatusFilterActive => TodoStatus.Active,
                StatusFilterCompleted => TodoStatus.Completed,
                StatusFilterDeleted => TodoStatus.Deleted,
                _ => null
            },
            GroupId = _selectedGroupId,
            FilterNoGroup = _filterNoGroup,
            IncludeNoDue = IncludeNoDue,
            StartDateFrom = ShowStartDateFilter ? ToDateOnly(StartDateFrom) : null,
            StartDateTo = ShowStartDateFilter ? ToDateOnly(StartDateTo) : null,
            DueDateFrom = ShowDueDateFilter ? ToDateOnly(DueDateFrom) : null,
            DueDateTo = ShowDueDateFilter ? ToDateOnly(DueDateTo) : null,
            CreatedAtFrom = ShowCreatedAtFilter ? CreatedAtFrom : null,
            CreatedAtTo = ShowCreatedAtFilter ? CreatedAtTo : null,
            UpdatedAtFrom = ShowUpdatedAtFilter ? UpdatedAtFrom : null,
            UpdatedAtTo = ShowUpdatedAtFilter ? UpdatedAtTo : null
        };
    }

    private void LoadStatusFilters()
    {
        if (StatusFilters.Count == 0)
        {
            StatusFilters.Add(new StatusFilterOption(StatusFilterAll, "Status.All"));
            StatusFilters.Add(new StatusFilterOption(StatusFilterActive, "Status.Active"));
            StatusFilters.Add(new StatusFilterOption(StatusFilterCompleted, "Status.Completed"));
            StatusFilters.Add(new StatusFilterOption(StatusFilterDeleted, "Status.Deleted"));
            return;
        }

        foreach (var option in StatusFilters)
        {
            option.RefreshDisplayName();
        }
    }

    private static string NormalizeStatusFilter(string? statusFilter)
    {
        return statusFilter is StatusFilterAll or StatusFilterActive or StatusFilterCompleted or StatusFilterDeleted
            ? statusFilter
            : StatusFilterActive;
    }

    private void RefreshLocalizedProperties()
    {
        LoadStatusFilters();
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(TotalCountText));
        OnPropertyChanged(nameof(MultiSelectButtonText));
        Refresh();
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

        var subtaskCountsByParentId = todos
            .Where(todo => todo.IsSubtask && !string.IsNullOrWhiteSpace(todo.ParentId))
            .GroupBy(todo => todo.ParentId!)
            .ToDictionary(group => group.Key, group => group.Count());
        var rootIdsWithSubtasks = subtaskCountsByParentId.Keys.ToHashSet();

        _collapsedTodoIds.RemoveWhere(id => !rootIdsWithSubtasks.Contains(id));

        foreach (var todo in todos)
        {
            todo.HasSubtasks = !todo.IsSubtask && rootIdsWithSubtasks.Contains(todo.Id);
            todo.SubtaskCount = !todo.IsSubtask && subtaskCountsByParentId.TryGetValue(todo.Id, out var subtaskCount) ? subtaskCount : 0;
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

        // Search 已按 SortOrder 优先返回默认顺序，避免主界面覆盖拖拽后的顺序。
        return todos.ToList();
    }

    private static IReadOnlyList<TodoItem> ArrangeByHierarchy(IReadOnlyList<TodoItem> sortedTodos)
    {
        var rootIds = sortedTodos
            .Where(todo => !todo.IsSubtask)
            .Select(todo => todo.Id)
            .ToHashSet();
        var childrenByParentId = sortedTodos
            .Where(todo => todo.IsSubtask &&
                           !string.IsNullOrWhiteSpace(todo.ParentId) &&
                           rootIds.Contains(todo.ParentId))
            .GroupBy(todo => todo.ParentId!)
            .ToDictionary(group => group.Key, group => group.ToList());
        var result = new List<TodoItem>(sortedTodos.Count);
        var addedIds = new HashSet<string>();

        foreach (var todo in sortedTodos)
        {
            if (todo.IsSubtask &&
                !string.IsNullOrWhiteSpace(todo.ParentId) &&
                rootIds.Contains(todo.ParentId))
            {
                continue;
            }

            AddTodo(todo);

            if (!todo.IsSubtask && childrenByParentId.TryGetValue(todo.Id, out var children))
            {
                foreach (var child in children)
                {
                    AddTodo(child);
                }
            }
        }

        return result;

        void AddTodo(TodoItem todo)
        {
            if (addedIds.Add(todo.Id))
            {
                result.Add(todo);
            }
        }
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
        var activeTaskIds = _todoService.Search(new TodoSearchCriteria { Status = TodoStatus.Active, IncludeNoDue = true })
            .Select(todo => todo.Id)
            .ToHashSet();

        foreach (var group in groups)
        {
            var groupItems = activeItems.Where(todo => todo.GroupId == group.Id).ToList();
            GroupSummaries.Add(new TodoGroupSummary(
                group.Id,
                group.Name,
                group.IconKey,
                groupItems.Count,
                groupItems.Count(todo => activeTaskIds.Contains(todo.Id)),
                _selectedGroupId == group.Id,
                isNoGroup: false));
        }

        var noGroupItems = activeItems.Where(todo => string.IsNullOrWhiteSpace(todo.GroupId)).ToList();
        if (noGroupItems.Count > 0)
        {
            GroupSummaries.Add(new TodoGroupSummary(
                null,
                LocalizationService.Text("Group.NoGroup"),
                "inbox",
                noGroupItems.Count,
                noGroupItems.Count(todo => activeTaskIds.Contains(todo.Id)),
                _filterNoGroup,
                isNoGroup: true));
        }
    }
}

public sealed class TodoGroupSummary
{
    public TodoGroupSummary(
        string? groupId,
        string name,
        string iconKey,
        int totalCount,
        int activeCount,
        bool isSelected,
        bool isNoGroup)
    {
        GroupId = groupId;
        Name = name;
        IconKey = string.IsNullOrWhiteSpace(iconKey) ? "folder" : iconKey;
        TotalCount = totalCount;
        ActiveCount = activeCount;
        IsSelected = isSelected;
        IsNoGroup = isNoGroup;
    }

    public string? GroupId { get; }

    public string Name { get; }

    public string IconKey { get; }

    public int TotalCount { get; }

    public int ActiveCount { get; }

    public bool IsSelected { get; }

    public bool IsNoGroup { get; }

    public string CountText => LocalizationService.Format("Count.GroupSummaryFormat", ActiveCount, TotalCount);

    public System.Windows.Media.Geometry IconGeometry => IconOption.Geometry;

    public System.Windows.Media.Brush IconForeground => IconOption.Foreground;

    public System.Windows.Media.Brush IconBackground => IconOption.Background;

    private TaskGroupIconOption IconOption => TaskGroupIconCatalog.Get(IconKey);
}

public sealed class StatusFilterOption : ViewModelBase
{
    public StatusFilterOption(string key, string resourceKey)
    {
        Key = key;
        ResourceKey = resourceKey;
    }

    public string Key { get; }

    public string ResourceKey { get; }

    public string DisplayName => LocalizationService.Text(ResourceKey);

    public void RefreshDisplayName()
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    public override string ToString() => DisplayName;
}
