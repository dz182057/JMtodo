using System.Windows;
using System.Windows.Input;
using TodoDesktopApp.Models;
using TodoDesktopApp.ViewModels;

namespace TodoDesktopApp.Dialogs;

public partial class MoveToGroupDialogWindow : Window
{
    private readonly MoveToGroupViewModel _viewModel;

    public MoveToGroupDialogWindow(IEnumerable<TodoGroup> groups, int selectedTaskCount)
    {
        InitializeComponent();
        _viewModel = new MoveToGroupViewModel(groups, selectedTaskCount);
        DataContext = _viewModel;
    }

    public bool CreateNewGroup => _viewModel.IsCreatingNewGroup;

    public string? SelectedGroupId => _viewModel.SelectedGroupId;

    public string NewGroupName => _viewModel.NewGroupName.Trim();

    public string NewGroupIconKey => _viewModel.NewGroupIconKey;

    public string TargetGroupName => _viewModel.TargetGroupName;

    private void UseExistingButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.UseExistingGroup();
    }

    private void UseNewButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.UseNewGroup();
        NewGroupNameTextBox.Focus();
    }

    private void MoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Validate())
        {
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _viewModel.Validate())
        {
            DialogResult = true;
            e.Handled = true;
        }
    }
}
