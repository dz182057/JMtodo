using System.Windows;
using System.Windows.Input;

namespace TodoDesktopApp.Dialogs;

public partial class ConfirmDialogWindow : Window
{
    public static readonly DependencyProperty TitleTextProperty =
        DependencyProperty.Register(
            nameof(TitleText),
            typeof(string),
            typeof(ConfirmDialogWindow),
            new PropertyMetadata("确认关闭"));

    public static readonly DependencyProperty MessageTextProperty =
        DependencyProperty.Register(
            nameof(MessageText),
            typeof(string),
            typeof(ConfirmDialogWindow),
            new PropertyMetadata("当前内容尚未保存，确定关闭吗？"));

    public static readonly DependencyProperty ConfirmTextProperty =
        DependencyProperty.Register(
            nameof(ConfirmText),
            typeof(string),
            typeof(ConfirmDialogWindow),
            new PropertyMetadata("确认关闭"));

    public static readonly DependencyProperty CancelTextProperty =
        DependencyProperty.Register(
            nameof(CancelText),
            typeof(string),
            typeof(ConfirmDialogWindow),
            new PropertyMetadata("继续编辑"));

    public static readonly DependencyProperty CancelButtonVisibilityProperty =
        DependencyProperty.Register(
            nameof(CancelButtonVisibility),
            typeof(Visibility),
            typeof(ConfirmDialogWindow),
            new PropertyMetadata(Visibility.Visible));

    public ConfirmDialogWindow()
    {
        InitializeComponent();
    }

    public static void ShowInfo(Window owner, string title, string message, string confirmText = "知道了")
    {
        var dialog = new ConfirmDialogWindow
        {
            Owner = owner,
            TitleText = title,
            MessageText = message,
            ConfirmText = confirmText,
            CancelButtonVisibility = Visibility.Collapsed
        };

        dialog.ShowDialog();
    }

    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string MessageText
    {
        get => (string)GetValue(MessageTextProperty);
        set => SetValue(MessageTextProperty, value);
    }

    public string ConfirmText
    {
        get => (string)GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    public string CancelText
    {
        get => (string)GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    public Visibility CancelButtonVisibility
    {
        get => (Visibility)GetValue(CancelButtonVisibilityProperty);
        set => SetValue(CancelButtonVisibilityProperty, value);
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
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
        else if (e.Key == Key.Enter)
        {
            DialogResult = true;
            e.Handled = true;
        }
    }
}
