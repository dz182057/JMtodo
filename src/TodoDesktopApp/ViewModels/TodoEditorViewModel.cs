using System.Collections.ObjectModel;
using TodoDesktopApp.Models;

namespace TodoDesktopApp.ViewModels;

public sealed class TodoEditorViewModel : ViewModelBase
{
    private string _title = string.Empty;
    private string? _note;
    private DateTime? _startDate = DateTime.Today;
    private DateTime? _dueDate;
    private bool _isNoDue = true;
    private string? _titleError;
    private string? _errorMessage;
    private TodoGroupOption? _selectedGroup;
    private bool _showTitleError;
    private bool _isTitleValid;

    public TodoEditorViewModel(IEnumerable<TodoGroup>? groups = null)
    {
        LoadGroupOptions(groups, null);
        UpdateValidationState();
    }

    public TodoEditorViewModel(TodoItem item)
    {
        Id = item.Id;
        ParentId = item.ParentId;
        ParentTitle = item.ParentTitle;
        IsSubtask = item.IsSubtask;
        Title = item.Title;
        Note = item.Note;
        StartDate = item.StartDate.ToDateTime(TimeOnly.MinValue);
        DueDate = item.DueDate?.ToDateTime(TimeOnly.MinValue);
        IsNoDue = !item.DueDate.HasValue;
        LoadGroupOptions(null, item.GroupId);
        UpdateValidationState();
    }

    public TodoEditorViewModel(TodoItem parent, bool isNewSubtask)
    {
        if (!isNewSubtask)
        {
            throw new InvalidOperationException("子任务编辑器参数无效。");
        }

        ParentId = parent.Id;
        ParentTitle = parent.Title;
        IsSubtask = true;
        LoadGroupOptions(null, parent.GroupId);
        UpdateValidationState();
    }

    public string? Id { get; }

    public string? ParentId { get; }

    public string? ParentTitle { get; }

    public bool IsSubtask { get; }

    public bool IsGroupSelectorVisible => string.IsNullOrWhiteSpace(Id) && !IsSubtask;

    public ObservableCollection<TodoGroupOption> GroupOptions { get; } = new();

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                UpdateValidationState();
            }
        }
    }

    public string? Note
    {
        get => _note;
        set
        {
            if (SetProperty(ref _note, value))
            {
                UpdateValidationState();
            }
        }
    }

    public DateTime? StartDate
    {
        get => _startDate;
        set
        {
            if (SetProperty(ref _startDate, value))
            {
                UpdateValidationState();
            }
        }
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            if (SetProperty(ref _dueDate, value))
            {
                if (value.HasValue && IsNoDue)
                {
                    _isNoDue = false;
                    OnPropertyChanged(nameof(IsNoDue));
                    OnPropertyChanged(nameof(IsDueDateEnabled));
                }

                UpdateValidationState();
            }
        }
    }

    public bool IsNoDue
    {
        get => _isNoDue;
        set
        {
            if (SetProperty(ref _isNoDue, value))
            {
                if (value)
                {
                    DueDate = null;
                }

                OnPropertyChanged(nameof(IsDueDateEnabled));
                UpdateValidationState();
            }
        }
    }

    public bool IsDueDateEnabled => !IsNoDue;

    public TodoGroupOption? SelectedGroup
    {
        get => _selectedGroup;
        set => SetProperty(ref _selectedGroup, value);
    }

    public string? SelectedGroupId => SelectedGroup?.Id;

    public string? TitleError
    {
        get => _titleError;
        private set => SetProperty(ref _titleError, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(InfoBarText));
            }
        }
    }

    public string InfoBarText => ErrorMessage ?? (IsSubtask
        ? $"子任务将添加到「{ParentTitle}」下，暂时只支持二级子任务。"
        : "新任务默认状态为未完成，可在主列表或悬浮窗中快速标记完成。");

    public bool CanSave =>
        _isTitleValid &&
        string.IsNullOrWhiteSpace(ErrorMessage);

    public bool Validate()
    {
        _showTitleError = true;
        UpdateValidationState();

        return CanSave;
    }

    public void MarkTitleTouched()
    {
        _showTitleError = true;
        UpdateValidationState();
    }

    public DateOnly GetStartDate() => DateOnly.FromDateTime(StartDate!.Value);

    public DateOnly? GetDueDate() => IsNoDue || !DueDate.HasValue ? null : DateOnly.FromDateTime(DueDate.Value);

    private void LoadGroupOptions(IEnumerable<TodoGroup>? groups, string? selectedGroupId)
    {
        GroupOptions.Clear();
        var noGroup = new TodoGroupOption { Name = "不加入任务组" };
        GroupOptions.Add(noGroup);

        if (groups is not null)
        {
            foreach (var group in groups.OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase))
            {
                GroupOptions.Add(new TodoGroupOption { Id = group.Id, Name = group.Name });
            }
        }

        SelectedGroup = GroupOptions.FirstOrDefault(group => group.Id == selectedGroupId) ?? noGroup;
    }

    private void UpdateValidationState()
    {
        var titleError = string.IsNullOrWhiteSpace(Title) ? "请输入任务标题" : null;

        if (Title?.Length > 80)
        {
            titleError = "标题不能超过 80 字";
        }

        _isTitleValid = titleError is null;
        TitleError = _showTitleError ? titleError : null;

        if (Note?.Length > 500)
        {
            ErrorMessage = "备注不能超过 500 字";
        }
        else if (!StartDate.HasValue)
        {
            ErrorMessage = "请选择起始日期";
        }
        else if (!IsNoDue && DueDate.HasValue && DueDate.Value.Date < StartDate.Value.Date)
        {
            ErrorMessage = "计划完成日期不能早于开始日期";
        }
        else
        {
            ErrorMessage = null;
        }

        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(InfoBarText));
    }
}

public sealed class TodoGroupOption
{
    public string? Id { get; init; }
    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}
