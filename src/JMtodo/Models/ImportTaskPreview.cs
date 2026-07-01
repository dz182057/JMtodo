using System.Collections.ObjectModel;
using TodoDesktopApp.Services;

namespace TodoDesktopApp.Models;

public sealed class ImportTaskPreview
{
    public string Title { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? GroupName { get; set; }

    public ObservableCollection<ImportAttachmentPreview> Attachments { get; } = new();

    public ObservableCollection<ImportTaskPreview> Subtasks { get; } = new();

    public string StartDateText => StartDate.ToString("yyyy-MM-dd");

    public string DueDateText => DueDate?.ToString("yyyy-MM-dd") ?? LocalizationService.Text("Todo.NoDue");

    public string StartDateDisplayText => LocalizationService.Format("Import.StartDateFormat", StartDateText);

    public string DueDateDisplayText => LocalizationService.Format("Import.DueDateFormat", DueDateText);

    public string GroupText => string.IsNullOrWhiteSpace(GroupName)
        ? LocalizationService.Text("Group.NoGroup")
        : GroupName;

    public string NoteText => string.IsNullOrWhiteSpace(Note)
        ? LocalizationService.Text("Import.NoNote")
        : Note;

    public string AttachmentCountText => Attachments.Count == 0
        ? LocalizationService.Text("Attachment.None")
        : LocalizationService.Format("Attachment.CountFormat", Attachments.Count);
}
