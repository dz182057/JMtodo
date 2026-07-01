using System.Diagnostics;
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
        DialogResult = true;
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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string T(string key) => LocalizationService.Text(key);
}
