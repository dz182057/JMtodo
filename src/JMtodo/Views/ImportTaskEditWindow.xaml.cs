using System.Globalization;
using System.Windows;
using System.Windows.Input;
using TodoDesktopApp.Dialogs;
using TodoDesktopApp.Models;
using TodoDesktopApp.Services;

namespace TodoDesktopApp.Views;

public partial class ImportTaskEditWindow : Window
{
    private readonly ImportTaskPreview _task;

    public ImportTaskEditWindow(ImportTaskPreview task)
    {
        InitializeComponent();
        _task = task;
        TitleTextBox.Text = task.Title;
        GroupNameTextBox.Text = task.GroupName;
        GroupNameTextBox.IsEnabled = task.CanEditGroup;
        StartDateTextBox.Text = task.StartDateInput;
        DueDateTextBox.Text = task.DueDateInput;
        NoteTextBox.Text = task.Note;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.ImportTasks.FailedTitle"), T("Validation.TaskTitleRequired"));
            return;
        }

        var startDateInput = StartDateTextBox.Text.Trim();
        if (!DateOnly.TryParseExact(startDateInput, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.ImportTasks.FailedTitle"), T("Validation.ImportStartDateInvalid"));
            return;
        }

        var dueDateInput = DueDateTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(dueDateInput))
        {
            if (!DateOnly.TryParseExact(dueDateInput, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDate))
            {
                ConfirmDialogWindow.ShowInfo(this, T("Dialog.ImportTasks.FailedTitle"), T("Validation.ImportDueDateInvalid"));
                return;
            }

            if (dueDate < startDate)
            {
                ConfirmDialogWindow.ShowInfo(this, T("Dialog.ImportTasks.FailedTitle"), T("Validation.DueBeforeStart"));
                return;
            }
        }

        _task.Title = title;
        if (_task.CanEditGroup)
        {
            _task.GroupName = string.IsNullOrWhiteSpace(GroupNameTextBox.Text) ? null : GroupNameTextBox.Text.Trim();
        }

        _task.StartDateInput = startDateInput;
        _task.DueDateInput = dueDateInput;
        _task.Note = string.IsNullOrWhiteSpace(NoteTextBox.Text) ? null : NoteTextBox.Text.Trim();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string T(string key) => LocalizationService.Text(key);
}
