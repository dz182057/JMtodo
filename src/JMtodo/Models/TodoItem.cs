using Brush = System.Windows.Media.Brush;
using TodoDesktopApp.Services;

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
    public int SubtaskCount { get; set; }
    public bool IsExpanded { get; set; } = true;
    public List<TodoAttachment> Attachments { get; set; } = new();

    public string StatusText => Status switch
    {
        TodoStatus.Active => LocalizationService.Text("Status.Active"),
        TodoStatus.Completed => LocalizationService.Text("Status.Completed"),
        TodoStatus.Deleted => LocalizationService.Text("Status.Deleted"),
        _ => Status.ToString()
    };

    public string DueDateText => DueDate?.ToString("yyyy-MM-dd") ?? LocalizationService.Text("Todo.NoDue");

    public string StartDateText => StartDate.ToString("yyyy-MM-dd");

    public bool IsSubtask => !string.IsNullOrWhiteSpace(ParentId);

    public bool CanCreateSubtask => !IsSubtask && Status == TodoStatus.Active;

    public string ExpandButtonText => HasSubtasks ? IsExpanded ? "▾" : "▸" : string.Empty;

    public string LevelText => IsSubtask ? LocalizationService.Text("Todo.Subtask") : LocalizationService.Text("Todo.MainTask");

    public string SubtaskCountText => SubtaskCount > 0 ? LocalizationService.Format("Todo.SubtaskCountFormat", SubtaskCount) : string.Empty;

    public string GroupDisplayText => string.IsNullOrWhiteSpace(GroupName) ? LocalizationService.Text("Group.NoGroup") : GroupName;

    public string ParentDisplayText =>
        IsSubtask
            ? LocalizationService.Format(
                "Todo.ParentDisplayFormat",
                string.IsNullOrWhiteSpace(ParentTitle) ? LocalizationService.Text("Todo.ParentMissing") : ParentTitle)
            : string.Empty;

    public string FloatingRelationText =>
        IsSubtask
            ? LocalizationService.Format("Todo.FloatingRelationFormat", ParentTitle ?? LocalizationService.Text("Todo.ParentMissing"))
            : GroupDisplayText;

    public string FloatingDateText => DueDate?.ToString("yyyy-MM-dd") ?? LocalizationService.Text("Todo.NoDue");

    public bool IsCompleted => Status == TodoStatus.Completed;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public bool HasAttachments => Attachments.Count > 0;

    public int AttachmentCount => Attachments.Count;

    public string AttachmentCountText => HasAttachments ? LocalizationService.Format("Attachment.CountFormat", AttachmentCount) : string.Empty;

    public string AttachmentSummaryText => AttachmentCount switch
    {
        0 => LocalizationService.Text("Attachment.None"),
        1 => Attachments[0].DisplayFileName,
        _ => LocalizationService.Format("Attachment.CountFormat", AttachmentCount)
    };

    public string AttachmentIconLabel => AttachmentCount == 1
        ? Attachments[0].FileTypeLabel
        : LocalizationService.Text("Attachment.MultipleLabel");

    public Brush AttachmentIconForeground => AttachmentCount == 1
        ? Attachments[0].FileTypeForeground
        : AttachmentFileTypeIconCatalog.GetMultiple().Foreground;

    public Brush AttachmentIconBackground => AttachmentCount == 1
        ? Attachments[0].FileTypeBackground
        : AttachmentFileTypeIconCatalog.GetMultiple().Background;

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
            SubtaskCount = SubtaskCount,
            IsExpanded = IsExpanded,
            Attachments = Attachments.Select(attachment => attachment.Clone()).ToList()
        };
    }
}
