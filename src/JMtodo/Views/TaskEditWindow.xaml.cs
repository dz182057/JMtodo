using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TodoDesktopApp.Dialogs;
using TodoDesktopApp.Services;
using TodoDesktopApp.ViewModels;

namespace TodoDesktopApp.Views;

public partial class TaskEditWindow : Window
{
    private readonly TodoEditorViewModel _viewModel;
    private readonly string _initialTitle;
    private readonly string? _initialNote;
    private readonly DateTime? _initialStartDate;
    private readonly DateTime? _initialDueDate;
    private readonly bool _initialIsNoDue;
    private readonly string? _initialSelectedGroupId;
    private readonly string _initialAttachmentState;

    public TaskEditWindow(TodoEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _initialTitle = viewModel.Title;
        _initialNote = viewModel.Note;
        _initialStartDate = viewModel.StartDate;
        _initialDueDate = viewModel.DueDate;
        _initialIsNoDue = viewModel.IsNoDue;
        _initialSelectedGroupId = viewModel.SelectedGroupId;
        _initialAttachmentState = viewModel.AttachmentStateSignature;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
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
        if (!_viewModel.Validate())
        {
            return;
        }

        DialogResult = true;
    }

    private void TitleTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.MarkTitleTouched();
    }

    private void SelectFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = T("Dialog.SelectTaskFiles.Title"),
            Filter = T("Dialog.SelectTaskFiles.Filter"),
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _viewModel.AddAttachmentFiles(dialog.FileNames);
        }
        catch (InvalidOperationException ex)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.AddFileFailed.Title"), ex.Message);
        }
        catch (IOException)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.AddFileFailed.Title"), T("Dialog.AddFileFailed.Read"));
        }
        catch (UnauthorizedAccessException)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.AddFileFailed.Title"), T("Dialog.AddFileFailed.Access"));
        }
    }

    private void OpenEditorAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TodoEditorAttachmentItem attachment)
        {
            return;
        }

        OpenFilePath(attachment.OpenPath);
    }

    private void RemoveAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TodoEditorAttachmentItem attachment)
        {
            _viewModel.RemoveAttachment(attachment);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CloseIfAllowed();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseIfAllowed();
    }

    private void CloseIfAllowed()
    {
        if (IsDirty())
        {
            var dialog = new ConfirmDialogWindow
            {
                Owner = this,
                TitleText = T("Dialog.DefaultTitle"),
                MessageText = T("Dialog.DefaultMessage"),
                ConfirmText = T("Dialog.DefaultConfirm"),
                CancelText = T("Dialog.DefaultCancel")
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }
        }

        DialogResult = false;
    }

    private bool IsDirty()
    {
        return _viewModel.Title != _initialTitle ||
               _viewModel.Note != _initialNote ||
               _viewModel.StartDate != _initialStartDate ||
               _viewModel.DueDate != _initialDueDate ||
               _viewModel.IsNoDue != _initialIsNoDue ||
               _viewModel.SelectedGroupId != _initialSelectedGroupId ||
               _viewModel.AttachmentStateSignature != _initialAttachmentState;
    }

    private void OpenFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.OpenFileFailed.Title"), T("Dialog.OpenFileFailed.Missing"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.OpenFileFailed.Title"), T("Dialog.OpenFileFailed.System"));
        }
    }

    private static string T(string key) => LocalizationService.Text(key);
}
