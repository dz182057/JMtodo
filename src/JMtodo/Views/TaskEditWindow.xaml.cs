using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using TodoDesktopApp.Dialogs;
using TodoDesktopApp.Services;
using TodoDesktopApp.ViewModels;
using Clipboard = System.Windows.Clipboard;

namespace TodoDesktopApp.Views;

public partial class TaskEditWindow : Window
{
    private const string ClipboardPngFormat = "image/png";
    private const string WindowsClipboardPngFormat = "PNG";

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

        AddAttachmentFilesWithFeedback(dialog.FileNames);
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var filePaths = Clipboard.GetFileDropList().Cast<string>().ToList();
                if (filePaths.Count == 0)
                {
                    return;
                }

                e.Handled = true;
                AddAttachmentFilesWithFeedback(filePaths);
                return;
            }

            var dataObject = Clipboard.GetDataObject();
            if (dataObject is null || !ContainsClipboardImage(dataObject))
            {
                return;
            }

            e.Handled = true;
            AddClipboardImageWithFeedback(dataObject);
        }
        catch (ExternalException)
        {
            e.Handled = true;
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.AddFileFailed.Title"), T("Dialog.AddFileFailed.Clipboard"));
        }
    }

    private void AddAttachmentFilesWithFeedback(IEnumerable<string> filePaths)
    {
        try
        {
            _viewModel.AddAttachmentFiles(filePaths);
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

    private void AddClipboardImageWithFeedback(System.Windows.IDataObject dataObject)
    {
        if (!_viewModel.CanAddAttachment)
        {
            ConfirmDialogWindow.ShowInfo(
                this,
                T("Dialog.AddFileFailed.Title"),
                LocalizationService.Format("Editor.MaxAttachmentFormat", TodoService.MaxAttachmentCount));
            return;
        }

        try
        {
            AddAttachmentFilesWithFeedback(new[] { SaveClipboardImage(dataObject) });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.AddFileFailed.Title"), T("Dialog.AddFileFailed.ClipboardImage"));
        }
    }

    private static bool ContainsClipboardImage(System.Windows.IDataObject dataObject)
    {
        return dataObject.GetDataPresent(ClipboardPngFormat) ||
               dataObject.GetDataPresent(WindowsClipboardPngFormat) ||
               dataObject.GetDataPresent(System.Windows.DataFormats.Bitmap) ||
               Clipboard.ContainsImage();
    }

    private static string SaveClipboardImage(System.Windows.IDataObject dataObject)
    {
        var folder = Path.Combine(Path.GetTempPath(), "JMtodo", "clipboard");
        Directory.CreateDirectory(folder);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var filePath = Path.Combine(folder, $"JMtodo-screenshot-{DateTime.Now:yyyyMMdd-HHmmssfff}-{suffix}.png");

        if (TrySaveRawClipboardPngAsOpaque(dataObject, filePath, ClipboardPngFormat) ||
            TrySaveRawClipboardPngAsOpaque(dataObject, filePath, WindowsClipboardPngFormat))
        {
            return filePath;
        }

        var image = Clipboard.GetImage() ?? throw new InvalidOperationException(T("Dialog.AddFileFailed.ClipboardImage"));
        SaveOpaquePng(image, filePath);
        return filePath;
    }

    private static bool TrySaveRawClipboardPngAsOpaque(System.Windows.IDataObject dataObject, string filePath, string format)
    {
        if (!dataObject.GetDataPresent(format))
        {
            return false;
        }

        using var imageStream = CreateClipboardImageStream(dataObject.GetData(format));
        if (imageStream is null)
        {
            return false;
        }

        var decoder = new PngBitmapDecoder(
            imageStream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            return false;
        }

        SaveOpaquePng(decoder.Frames[0], filePath);
        return true;
    }

    private static MemoryStream? CreateClipboardImageStream(object? data)
    {
        var memoryStream = new MemoryStream();
        switch (data)
        {
            case byte[] bytes:
                memoryStream.Write(bytes, 0, bytes.Length);
                break;
            case Stream stream:
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                stream.CopyTo(memoryStream);
                break;
            default:
                memoryStream.Dispose();
                return null;
        }

        memoryStream.Position = 0;
        return memoryStream;
    }

    private static void SaveOpaquePng(BitmapSource image, string filePath)
    {
        using var stream = File.Create(filePath);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(CreateOpaqueBitmap(image)));
        encoder.Save(stream);
    }

    private static BitmapSource CreateOpaqueBitmap(BitmapSource image)
    {
        var bitmap = image.Format == PixelFormats.Bgra32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        bitmap.CopyPixels(pixels, stride, 0);
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
        }

        return BitmapSource.Create(
            width,
            height,
            bitmap.DpiX,
            bitmap.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
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

    private void CopyEditorAttachmentItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TodoEditorAttachmentItem attachment)
        {
            return;
        }

        e.Handled = true;
        CopyFilePath(attachment.OpenPath);
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

    private void CopyFilePath(string filePath)
    {
        try
        {
            FileClipboardService.CopyFile(filePath);
        }
        catch (InvalidOperationException ex)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.CopyFileFailed.Title"), ex.Message);
        }
        catch (ExternalException)
        {
            ConfirmDialogWindow.ShowInfo(this, T("Dialog.CopyFileFailed.Title"), T("Dialog.CopyFileFailed.Clipboard"));
        }
    }

    private static string T(string key) => LocalizationService.Text(key);
}
