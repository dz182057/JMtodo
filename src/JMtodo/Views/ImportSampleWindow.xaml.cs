using System.Windows;
using System.Windows.Input;

namespace TodoDesktopApp.Views;

public partial class ImportSampleWindow : Window
{
    public ImportSampleWindow(string sampleText)
    {
        InitializeComponent();
        SampleTextBox.Text = sampleText;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(SampleTextBox.Text);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
