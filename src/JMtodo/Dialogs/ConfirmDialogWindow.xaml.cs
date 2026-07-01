using System.Windows;
using System.Windows.Input;
using TodoDesktopApp.Services;

namespace TodoDesktopApp.Dialogs;

public partial class ConfirmDialogWindow : Window
{
    public static readonly DependencyProperty TitleTextProperty =
        DependencyProperty.Register(
            nameof(TitleText),
            typeof(string),
            typeof(ConfirmDialogWindow),
            new PropertyMetadata(LocalizationService.Text("Dialog.DefaultTitle")));

    public static readonly DependencyProperty MessageTextProperty =
        DependencyProperty.Register(
            nameof(MessageText),
            typeof(string),
            typeof(ConfirmDialogWindow),
            new PropertyMetadata(LocalizationService.Text("Dialog.DefaultMessage")));

    public static readonly DependencyProperty ConfirmTextProperty =
        DependencyProperty.Register(
            nameof(ConfirmText),
            typeof(string),
            typeof(ConfirmDialogWindow),
            new PropertyMetadata(LocalizationService.Text("Dialog.DefaultConfirm")));

    public static readonly DependencyProperty CancelTextProperty =
        DependencyProperty.Register(
            nameof(CancelText),
            typeof(string),
            typeof(ConfirmDialogWindow),
            new PropertyMetadata(LocalizationService.Text("Dialog.DefaultCancel")));

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

    public static void ShowInfo(Window owner, string title, string message, string? confirmText = null)
    {
        var dialog = new ConfirmDialogWindow
        {
            Owner = owner,
            TitleText = title,
            MessageText = message,
            ConfirmText = confirmText ?? LocalizationService.Text("Dialog.OK"),
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
