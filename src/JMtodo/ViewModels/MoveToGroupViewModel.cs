using System.Collections.ObjectModel;
using System.Windows.Media;
using TodoDesktopApp.Models;
using Brush = System.Windows.Media.Brush;

namespace TodoDesktopApp.ViewModels;

public sealed class MoveToGroupViewModel : ViewModelBase
{
    private MoveTaskGroupOption? _selectedGroup;
    private bool _isCreatingNewGroup;
    private string _newGroupName = string.Empty;
    private string _newGroupIconKey = "folder";
    private string? _errorMessage;

    public MoveToGroupViewModel(IEnumerable<TodoGroup> groups, int selectedTaskCount)
    {
        SelectedTaskCount = selectedTaskCount;

        foreach (var group in groups.OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase))
        {
            Groups.Add(new MoveTaskGroupOption(group));
        }

        SelectedGroup = Groups.FirstOrDefault();
        IsCreatingNewGroup = Groups.Count == 0;
    }

    public ObservableCollection<MoveTaskGroupOption> Groups { get; } = new();

    public IReadOnlyList<TaskGroupIconOption> IconOptions => TaskGroupIconCatalog.Options
        .Where(option => option.Key is not "all" and not "inbox")
        .ToList();

    public int SelectedTaskCount { get; }

    public string SelectedTaskCountText => $"已选 {SelectedTaskCount} 个主任务";

    public MoveTaskGroupOption? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                if (value is not null)
                {
                    IsCreatingNewGroup = false;
                }

                ErrorMessage = null;
                OnPropertyChanged(nameof(CanMove));
                OnPropertyChanged(nameof(TargetGroupName));
            }
        }
    }

    public bool IsCreatingNewGroup
    {
        get => _isCreatingNewGroup;
        set
        {
            if (SetProperty(ref _isCreatingNewGroup, value))
            {
                ErrorMessage = null;
                OnPropertyChanged(nameof(CanMove));
                OnPropertyChanged(nameof(TargetGroupName));
            }
        }
    }

    public string NewGroupName
    {
        get => _newGroupName;
        set
        {
            if (SetProperty(ref _newGroupName, value))
            {
                ErrorMessage = null;
                OnPropertyChanged(nameof(NewGroupNameCountText));
                OnPropertyChanged(nameof(CanMove));
                OnPropertyChanged(nameof(TargetGroupName));
            }
        }
    }

    public string NewGroupIconKey
    {
        get => _newGroupIconKey;
        set
        {
            if (SetProperty(ref _newGroupIconKey, value))
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

    public string NewGroupNameCountText => $"{Math.Min(NewGroupName.Length, 20)}/20";

    public bool CanMove => IsCreatingNewGroup
        ? !string.IsNullOrWhiteSpace(NewGroupName)
        : SelectedGroup is not null;

    public string TargetGroupName => IsCreatingNewGroup
        ? NewGroupName.Trim()
        : SelectedGroup?.Name ?? string.Empty;

    public string? SelectedGroupId => SelectedGroup?.Id;

    public void UseExistingGroup()
    {
        if (Groups.Count == 0)
        {
            IsCreatingNewGroup = true;
            ErrorMessage = "还没有可选择的任务组，请先新建任务组。";
            return;
        }

        IsCreatingNewGroup = false;
        SelectedGroup ??= Groups.FirstOrDefault();
    }

    public void UseNewGroup()
    {
        IsCreatingNewGroup = true;
    }

    public bool Validate()
    {
        if (IsCreatingNewGroup)
        {
            if (string.IsNullOrWhiteSpace(NewGroupName))
            {
                ErrorMessage = "请输入任务组名称。";
                return false;
            }

            if (NewGroupName.Trim().Length > 20)
            {
                ErrorMessage = "任务组名称不能超过 20 字。";
                return false;
            }

            if (Groups.Any(group => string.Equals(group.Name, NewGroupName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                ErrorMessage = "已存在同名任务组。";
                return false;
            }

            return true;
        }

        if (SelectedGroup is null)
        {
            ErrorMessage = "请选择一个任务组。";
            return false;
        }

        return true;
    }
}

public sealed class MoveTaskGroupOption
{
    public MoveTaskGroupOption(TodoGroup group)
    {
        Group = group;
        Id = group.Id;
        Name = group.Name;
        IconKey = string.IsNullOrWhiteSpace(group.IconKey) ? "folder" : group.IconKey;
    }

    public TodoGroup Group { get; }

    public string Id { get; }

    public string Name { get; }

    public string IconKey { get; }

    public Geometry IconGeometry => IconOption.Geometry;

    public Brush IconForeground => IconOption.Foreground;

    public Brush IconBackground => IconOption.Background;

    private TaskGroupIconOption IconOption => TaskGroupIconCatalog.Get(IconKey);
}
