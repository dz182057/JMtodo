using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TodoDesktopApp.Dialogs;
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
            Title = "选择任务文件",
            Filter = "所有文件 (*.*)|*.*",
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
            ConfirmDialogWindow.ShowInfo(this, "无法添加文件", ex.Message);
        }
        catch (IOException)
        {
            ConfirmDialogWindow.ShowInfo(this, "无法添加文件", "文件读取失败，请检查文件是否仍存在或是否有权限访问。");
        }
        catch (UnauthorizedAccessException)
        {
            ConfirmDialogWindow.ShowInfo(this, "无法添加文件", "没有权限读取选择的文件。");
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
                TitleText = "确认关闭",
                MessageText = "当前内容尚未保存，确定关闭吗？",
                ConfirmText = "确认关闭",
                CancelText = "继续编辑"
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
            ConfirmDialogWindow.ShowInfo(this, "无法打开文件", "文件不存在，可能已被移动或删除。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            ConfirmDialogWindow.ShowInfo(this, "无法打开文件", "系统未能打开该文件，请确认文件类型有关联的默认程序。");
        }
    }
}
