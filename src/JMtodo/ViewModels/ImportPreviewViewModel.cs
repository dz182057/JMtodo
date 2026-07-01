using System.Collections.ObjectModel;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;

namespace TodoDesktopApp.ViewModels;

public sealed class ImportPreviewViewModel : ViewModelBase
{
    public ImportPreviewViewModel(IReadOnlyList<ImportTaskPreview> rootTasks, int skippedDuplicateCount)
    {
        RootTasks = new ObservableCollection<ImportTaskPreview>(rootTasks);
        SkippedDuplicateCount = skippedDuplicateCount;
    }

    public ObservableCollection<ImportTaskPreview> RootTasks { get; }

    public int SkippedDuplicateCount { get; }

    public int RootTaskCount => RootTasks.Count;

    public int SubtaskCount => RootTasks.Sum(task => task.Subtasks.Count);

    public int AttachmentCount => RootTasks.Sum(CountAttachments);

    public int MissingAttachmentCount => RootTasks.Sum(CountMissingAttachments);

    public bool HasMissingAttachments => MissingAttachmentCount > 0;

    public bool HasSkippedDuplicates => SkippedDuplicateCount > 0;

    public string SummaryText => LocalizationService.Format(
        "Import.PreviewSummaryFormat",
        RootTaskCount,
        SubtaskCount,
        AttachmentCount);

    public string MissingAttachmentText => LocalizationService.Format(
        "Import.MissingAttachmentFormat",
        MissingAttachmentCount);

    public string SkippedDuplicateText => LocalizationService.Format(
        "Import.SkippedDuplicateFormat",
        SkippedDuplicateCount);

    private static int CountAttachments(ImportTaskPreview task)
    {
        return task.Attachments.Count + task.Subtasks.Sum(CountAttachments);
    }

    private static int CountMissingAttachments(ImportTaskPreview task)
    {
        return task.Attachments.Count(attachment => !attachment.Exists) + task.Subtasks.Sum(CountMissingAttachments);
    }
}
