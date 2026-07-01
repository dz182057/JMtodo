using System.Windows;
using System.Windows.Input;
using TodoDesktopApp.Dialogs;
using TodoDesktopApp.Services;
using TodoDesktopApp.ViewModels;

namespace TodoDesktopApp.Views;

public partial class TaskGroupManagerWindow : Window
{
    private readonly TaskGroupManagerViewModel _viewModel;

    public TaskGroupManagerWindow(TodoService todoService)
    {
        InitializeComponent();
        _viewModel = new TaskGroupManagerViewModel(todoService);
        DataContext = _viewModel;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGroup is null)
        {
            _viewModel.DeleteSelectedGroup();
            return;
        }

        var dialog = new ConfirmDialogWindow
        {
            Owner = this,
            TitleText = LocalizationService.Text("Dialog.GroupDelete.Title"),
            MessageText = LocalizationService.Format("Dialog.GroupDelete.Message", _viewModel.SelectedGroup.Name),
            ConfirmText = LocalizationService.Text("Dialog.GroupDelete.Confirm"),
            CancelText = LocalizationService.Text("Dialog.Cancel")
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.DeleteSelectedGroup();
        }
    }

    private void NewGroupButton_Click(object sender, RoutedEventArgs e)
    {
        GroupNameTextBox.Focus();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
