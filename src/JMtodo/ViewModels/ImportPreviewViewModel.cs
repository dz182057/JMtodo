using System.Collections.ObjectModel;
using System.ComponentModel;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;

namespace TodoDesktopApp.ViewModels;

public sealed class ImportPreviewViewModel : ViewModelBase
{
    public ImportPreviewViewModel(IReadOnlyList<ImportTaskPreview> rootTasks, int skippedDuplicateCount)
    {
        RootTasks = new ObservableCollection<ImportTaskPreview>(rootTasks);
        SkippedDuplicateCount = skippedDuplicateCount;
        foreach (var task in RootTasks)
        {
            SubscribeTask(task);
        }
    }

    public ObservableCollection<ImportTaskPreview> RootTasks { get; }

    public int SkippedDuplicateCount { get; }

    public int RootTaskCount => RootTasks.Count(task => task.IsSelected);

    public int SubtaskCount => RootTasks
        .Where(task => task.IsSelected)
        .Sum(task => task.Subtasks.Count(subtask => subtask.IsSelected));

    public int AttachmentCount => RootTasks.Where(task => task.IsSelected).Sum(CountAttachments);

    public int MissingAttachmentCount => RootTasks.Where(task => task.IsSelected).Sum(CountMissingAttachments);

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
        return task.Attachments.Count + task.Subtasks.Where(subtask => subtask.IsSelected).Sum(CountAttachments);
    }

    private static int CountMissingAttachments(ImportTaskPreview task)
    {
        return task.Attachments.Count(attachment => !attachment.Exists) +
               task.Subtasks.Where(subtask => subtask.IsSelected).Sum(CountMissingAttachments);
    }

    private void SubscribeTask(ImportTaskPreview task)
    {
        task.PropertyChanged += Task_PropertyChanged;
        foreach (var subtask in task.Subtasks)
        {
            SubscribeTask(subtask);
        }
    }

    private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ImportTaskPreview.IsSelected))
        {
            return;
        }

        OnPropertyChanged(nameof(RootTaskCount));
        OnPropertyChanged(nameof(SubtaskCount));
        OnPropertyChanged(nameof(AttachmentCount));
        OnPropertyChanged(nameof(MissingAttachmentCount));
        OnPropertyChanged(nameof(HasMissingAttachments));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(MissingAttachmentText));
    }
}
