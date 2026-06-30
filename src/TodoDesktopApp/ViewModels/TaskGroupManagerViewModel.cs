using System.Collections.ObjectModel;
using System.Windows.Media;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;
using Brush = System.Windows.Media.Brush;

namespace TodoDesktopApp.ViewModels;

public sealed class TaskGroupManagerViewModel : ViewModelBase
{
    private const string AllTasksId = "__all";
    private const string NoGroupId = "__nogroup";
    private readonly TodoService _todoService;
    private TaskGroupListItem? _selectedGroup;
    private string _groupName = string.Empty;
    private string _groupDescription = string.Empty;
    private string _editingIconKey = "folder";
    private string? _errorMessage;
    private bool _isCreateMode;

    public TaskGroupManagerViewModel(TodoService todoService)
    {
        _todoService = todoService;
        NewCommand = new RelayCommand(_ => BeginCreate());
        SaveCommand = new RelayCommand(_ => SaveGroup(), _ => CanSaveGroup);
        ClearCommand = new RelayCommand(_ => ClearEditor());
        Reload();
    }

    public ObservableCollection<TaskGroupListItem> Groups { get; } = new();

    public IReadOnlyList<TaskGroupIconOption> IconOptions => TaskGroupIconCatalog.Options
        .Where(option => option.Key is not "all" and not "inbox")
        .ToList();

    public RelayCommand NewCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand ClearCommand { get; }

    public TaskGroupListItem? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                LoadSelectedGroup();
            }
        }
    }

    public string GroupName
    {
        get => _groupName;
        set
        {
            if (SetProperty(ref _groupName, value))
            {
                ErrorMessage = null;
                OnPropertyChanged(nameof(NameCountText));
                RaiseCommandState();
            }
        }
    }

    public string GroupDescription
    {
        get => _groupDescription;
        set
        {
            if (SetProperty(ref _groupDescription, value))
            {
                ErrorMessage = null;
                OnPropertyChanged(nameof(DescriptionCountText));
            }
        }
    }

    public string EditingIconKey
    {
        get => _editingIconKey;
        set
        {
            if (SetProperty(ref _editingIconKey, value))
            {
                ErrorMessage = null;
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsCreateMode
    {
        get => _isCreateMode;
        private set
        {
            if (SetProperty(ref _isCreateMode, value))
            {
                OnPropertyChanged(nameof(EditorTitle));
                OnPropertyChanged(nameof(SaveButtonText));
                OnPropertyChanged(nameof(CanDeleteSelected));
            }
        }
    }

    public string EditorTitle => IsCreateMode ? "新建任务组" : "编辑任务组";

    public string SaveButtonText => IsCreateMode ? "新增任务组" : "保存修改";

    public string NameCountText => $"{Math.Min(GroupName.Length, 20)}/20";

    public string DescriptionCountText => $"{Math.Min(GroupDescription.Length, 100)}/100";

    public bool CanSaveGroup => !string.IsNullOrWhiteSpace(GroupName) && (IsCreateMode || SelectedGroup?.Group is not null);

    public bool CanDeleteSelected => !IsCreateMode && SelectedGroup?.Group is not null;

    public void DeleteSelectedGroup()
    {
        if (SelectedGroup?.Group is null)
        {
            ErrorMessage = "请选择可删除的任务组。";
            return;
        }

        try
        {
            _todoService.DeleteGroup(SelectedGroup.Group.Id);
            ClearEditor();
            Reload(NoGroupId);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public void BeginCreate()
    {
        IsCreateMode = true;
        SelectedGroup = null;
        GroupName = string.Empty;
        GroupDescription = string.Empty;
        EditingIconKey = "folder";
        ErrorMessage = null;
        RaiseCommandState();
    }

    private void SaveGroup()
    {
        try
        {
            if (IsCreateMode)
            {
                var group = _todoService.CreateGroup(GroupName, EditingIconKey, GroupDescription);
                Reload(group.Id);
                return;
            }

            if (SelectedGroup?.Group is null)
            {
                ErrorMessage = "请选择要编辑的任务组。";
                return;
            }

            _todoService.UpdateGroup(SelectedGroup.Group.Id, GroupName, EditingIconKey, GroupDescription);
            Reload(SelectedGroup.Group.Id);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void LoadSelectedGroup()
    {
        if (SelectedGroup?.Group is null)
        {
            if (SelectedGroup is null)
            {
                return;
            }

            IsCreateMode = false;
            GroupName = SelectedGroup.Name;
            GroupDescription = string.Empty;
            EditingIconKey = SelectedGroup.IconKey;
            ErrorMessage = null;
            RaiseCommandState();
            return;
        }

        IsCreateMode = false;
        GroupName = SelectedGroup.Group.Name;
        GroupDescription = SelectedGroup.Group.Description ?? string.Empty;
        EditingIconKey = SelectedGroup.Group.IconKey;
        ErrorMessage = null;
        RaiseCommandState();
    }

    private void Reload(string? selectedId = null)
    {
        Groups.Clear();

        var groups = _todoService.GetGroups();
        var activeItems = _todoService.Search(new TodoSearchCriteria { IncludeNoDue = true })
            .Where(todo => todo.Status != TodoStatus.Deleted)
            .ToList();

        Groups.Add(TaskGroupListItem.CreateSystem(
            AllTasksId,
            "全部任务",
            "all",
            activeItems.Count,
            activeItems.Count(todo => todo.Status == TodoStatus.Active)));

        foreach (var group in groups)
        {
            var groupItems = activeItems.Where(todo => todo.GroupId == group.Id).ToList();
            Groups.Add(TaskGroupListItem.CreateGroup(
                group,
                groupItems.Count,
                groupItems.Count(todo => todo.Status == TodoStatus.Active)));
        }

        var noGroupItems = activeItems.Where(todo => string.IsNullOrWhiteSpace(todo.GroupId)).ToList();
        Groups.Add(TaskGroupListItem.CreateSystem(
            NoGroupId,
            "未分组",
            "inbox",
            noGroupItems.Count,
            noGroupItems.Count(todo => todo.Status == TodoStatus.Active)));

        SelectedGroup = Groups.FirstOrDefault(group => group.Id == selectedId) ?? Groups.FirstOrDefault();
    }

    private void ClearEditor()
    {
        BeginCreate();
    }

    private void RaiseCommandState()
    {
        SaveCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanSaveGroup));
        OnPropertyChanged(nameof(CanDeleteSelected));
    }
}

public sealed class TaskGroupListItem
{
    private TaskGroupListItem(
        string id,
        string name,
        string iconKey,
        int totalCount,
        int activeCount,
        TodoGroup? group)
    {
        Id = id;
        Name = name;
        IconKey = string.IsNullOrWhiteSpace(iconKey) ? "folder" : iconKey;
        TotalCount = totalCount;
        ActiveCount = activeCount;
        Group = group;
    }

    public string Id { get; }

    public string Name { get; }

    public string IconKey { get; }

    public int TotalCount { get; }

    public int ActiveCount { get; }

    public TodoGroup? Group { get; }

    public string CountText => TotalCount.ToString();

    public Geometry IconGeometry => IconOption.Geometry;

    public Brush IconForeground => IconOption.Foreground;

    public Brush IconBackground => IconOption.Background;

    private TaskGroupIconOption IconOption => TaskGroupIconCatalog.Get(IconKey);

    public static TaskGroupListItem CreateGroup(TodoGroup group, int totalCount, int activeCount)
    {
        return new TaskGroupListItem(group.Id, group.Name, group.IconKey, totalCount, activeCount, group);
    }

    public static TaskGroupListItem CreateSystem(string id, string name, string iconKey, int totalCount, int activeCount)
    {
        return new TaskGroupListItem(id, name, iconKey, totalCount, activeCount, null);
    }
}
