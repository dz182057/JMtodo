using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using TodoDesktopApp.Dialogs;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;
using TodoDesktopApp.ViewModels;

namespace TodoDesktopApp.Views;

public partial class ImportTasksPreviewWindow : Window
{
    public ImportTasksPreviewWindow(IReadOnlyList<ImportTaskPreview> rootTasks, int skippedDuplicateCount)
    {
        InitializeComponent();
        DataContext = new ImportPreviewViewModel(rootTasks, skippedDuplicateCount);
        RootTasks = rootTasks;
    }

    public IReadOnlyList<ImportTaskPreview> RootTasks { get; }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RootTasks.Any(task => task.IsSelected))
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.ImportTasks.FailedTitle"), T("Validation.ImportSelectTask"));
            return;
        }

        if (!ValidateTasks(RootTasks, parentSelected: true))
        {
            return;
        }

        DialogResult = true;
    }

    private bool ValidateTasks(IEnumerable<ImportTaskPreview> tasks, bool parentSelected)
    {
        foreach (var task in tasks)
        {
            if (!parentSelected || !task.IsSelected)
            {
                continue;
            }

            if (!ValidateTask(task))
            {
                return false;
            }

            if (!ValidateTasks(task.Subtasks, parentSelected: true))
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidateTask(ImportTaskPreview task)
    {
        if (string.IsNullOrWhiteSpace(task.Title))
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.ImportTasks.FailedTitle"), T("Validation.TaskTitleRequired"));
            return false;
        }

        if (!DateOnly.TryParseExact(task.StartDateInput, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.ImportTasks.FailedTitle"), T("Validation.ImportStartDateInvalid"));
            return false;
        }

        if (!string.IsNullOrWhiteSpace(task.DueDateInput))
        {
            if (!DateOnly.TryParseExact(task.DueDateInput.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDate))
            {
                ConfirmDialogWindow.ShowInfo(this, T("Dialog.ImportTasks.FailedTitle"), T("Validation.ImportDueDateInvalid"));
                return false;
            }

            if (dueDate < startDate)
            {
                ConfirmDialogWindow.ShowInfo(this, T("Dialog.ImportTasks.FailedTitle"), T("Validation.DueBeforeStart"));
                return false;
            }
        }

        return true;
    }

    private void PreviewAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ImportAttachmentPreview attachment ||
            string.IsNullOrWhiteSpace(attachment.Path) ||
            !File.Exists(attachment.Path))
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.OpenFileFailed.Title"), T("Dialog.OpenFileFailed.Missing"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(attachment.Path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.OpenFileFailed.Title"), T("Dialog.OpenFileFailed.System"));
        }
    }

    private void EditPreviewTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ImportTaskPreview task)
        {
            return;
        }

        var dialog = new ImportTaskEditWindow(task) { Owner = this };
        dialog.ShowDialog();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string T(string key) => LocalizationService.Text(key);
}
