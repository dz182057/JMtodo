namespace TodoDesktopApp.Models;

public sealed class TodoItem
{
    public string Id { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string? ParentTitle { get; set; }
    public string? GroupId { get; set; }
    public string? GroupName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public TodoStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int SortOrder { get; set; }
    public bool HasSubtasks { get; set; }
    public bool IsExpanded { get; set; } = true;

    public string StatusText => Status switch
    {
        TodoStatus.Active => "未完成",
        TodoStatus.Completed => "已完成",
        TodoStatus.Deleted => "已删除",
        _ => Status.ToString()
    };

    public string DueDateText => DueDate?.ToString("yyyy-MM-dd") ?? "不限期";

    public string StartDateText => StartDate.ToString("yyyy-MM-dd");

    public bool IsSubtask => !string.IsNullOrWhiteSpace(ParentId);

    public bool CanCreateSubtask => !IsSubtask && Status == TodoStatus.Active;

    public string TitleDisplayText => IsSubtask ? $"  - {Title}" : Title;

    public string ExpandButtonText => HasSubtasks ? IsExpanded ? "▾" : "▸" : string.Empty;

    public string LevelText => IsSubtask ? "子任务" : "一级任务";

    public string GroupDisplayText => string.IsNullOrWhiteSpace(GroupName) ? "未分组" : GroupName;

    public string FloatingRelationText =>
        IsSubtask
            ? $"子任务 · {ParentTitle ?? "未找到父任务"}"
            : GroupDisplayText;

    public string FloatingStatusText =>
        Status == TodoStatus.Completed
            ? "已完成"
            : DueDate.HasValue
                ? StatusText
                : "不限期";

    public string FloatingDateText => DueDate?.ToString("yyyy-MM-dd") ?? "不限期";

    public bool IsCompleted => Status == TodoStatus.Completed;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public bool IsOverdue =>
        Status == TodoStatus.Active &&
        DueDate.HasValue &&
        DueDate.Value < DateOnly.FromDateTime(DateTime.Now);

    public TodoItem Clone()
    {
        return new TodoItem
        {
            Id = Id,
            ParentId = ParentId,
            ParentTitle = ParentTitle,
            GroupId = GroupId,
            GroupName = GroupName,
            Title = Title,
            Note = Note,
            StartDate = StartDate,
            DueDate = DueDate,
            Status = Status,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            CompletedAt = CompletedAt,
            DeletedAt = DeletedAt,
            SortOrder = SortOrder,
            HasSubtasks = HasSubtasks,
            IsExpanded = IsExpanded
        };
    }
}
