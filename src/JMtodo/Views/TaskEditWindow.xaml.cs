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
               _viewModel.SelectedGroupId != _initialSelectedGroupId;
    }
}
