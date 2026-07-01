using System.Windows;
using System.Windows.Input;

namespace TodoDesktopApp.Views;

public partial class ExportTasksOptionsWindow : Window
{
    public ExportTasksOptionsWindow()
    {
        InitializeComponent();
    }

    public bool IncludeDeleted => IncludeDeletedCheckBox.IsChecked == true;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
