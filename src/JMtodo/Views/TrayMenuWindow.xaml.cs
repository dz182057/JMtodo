using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TodoDesktopApp.Services;
using Forms = System.Windows.Forms;

namespace TodoDesktopApp.Views;

public partial class TrayMenuWindow : Window
{
    private const int LeftButton = 0x01;
    private const int RightButton = 0x02;
    private readonly Action _openMain;
    private readonly Action _toggleFloating;
    private readonly Action _addTask;
    private readonly Action _exitApp;
    private readonly Action<string> _changeLanguage;
    private readonly DispatcherTimer _outsideClickTimer;
    private bool _isClosing;

    public TrayMenuWindow(
        Action openMain,
        Action toggleFloating,
        Action addTask,
        Action exitApp,
        Action<string> changeLanguage,
        bool isFloatingVisible)
    {
        InitializeComponent();

        _openMain = openMain;
        _toggleFloating = toggleFloating;
        _addTask = addTask;
        _exitApp = exitApp;
        _changeLanguage = changeLanguage;

        _outsideClickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _outsideClickTimer.Tick += OutsideClickTimer_Tick;

        ToggleFloatingText.Text = isFloatingVisible
            ? LocalizationService.Text("Tray.HideFloating")
            : LocalizationService.Text("Tray.ShowFloating");
        RefreshLanguageChecks();
    }

    public void ShowNearCursor()
    {
        Opacity = 0;
        Show();
        UpdateLayout();

        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor);
        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY;

        var cursorX = cursor.X / scaleX;
        var cursorY = cursor.Y / scaleY;
        var workLeft = screen.WorkingArea.Left / scaleX;
        var workTop = screen.WorkingArea.Top / scaleY;
        var workRight = screen.WorkingArea.Right / scaleX;
        var workBottom = screen.WorkingArea.Bottom / scaleY;

        var targetLeft = cursorX - Width + 18;
        var targetTop = cursorY - Height - 8;

        if (targetLeft < workLeft + 8)
        {
            targetLeft = workLeft + 8;
        }

        if (targetTop < workTop + 8)
        {
            targetTop = cursorY + 8;
        }

        if (targetLeft + Width > workRight - 8)
        {
            targetLeft = workRight - Width - 8;
        }

        if (targetTop + Height > workBottom - 8)
        {
            targetTop = workBottom - Height - 8;
        }

        Left = targetLeft;
        Top = targetTop;

        BeginOpenAnimation();
        Focus();
        _outsideClickTimer.Start();
    }

    private void BeginOpenAnimation()
    {
        MenuRoot.RenderTransform = new TranslateTransform(0, -4);

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var slide = new DoubleAnimation
        {
            From = -4,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        BeginAnimation(OpacityProperty, fade);
        ((TranslateTransform)MenuRoot.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        CloseMenu();
    }

    private void OutsideClickTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsMouseButtonDown(LeftButton) && !IsMouseButtonDown(RightButton))
        {
            return;
        }

        if (IsCursorInsideWindow())
        {
            return;
        }

        CloseMenu();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            CloseMenu();
        }
    }

    private void OpenMain_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        _openMain();
    }

    private void ToggleFloating_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        _toggleFloating();
    }

    private void AddTask_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        _addTask();
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string language)
        {
            return;
        }

        CloseMenu();
        _changeLanguage(language);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        _exitApp();
    }

    private void CloseMenu()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _outsideClickTimer.Stop();
        Close();
    }

    private void RefreshLanguageChecks()
    {
        var currentLanguage = LocalizationService.CurrentLanguage;
        ChineseLanguageCheck.Visibility = currentLanguage == "zh-CN" ? Visibility.Visible : Visibility.Hidden;
        EnglishLanguageCheck.Visibility = currentLanguage == "en-US" ? Visibility.Visible : Visibility.Hidden;
    }

    protected override void OnClosed(EventArgs e)
    {
        _outsideClickTimer.Stop();
        base.OnClosed(e);
    }

    private bool IsCursorInsideWindow()
    {
        var cursor = Forms.Cursor.Position;
        var dpi = VisualTreeHelper.GetDpi(this);
        var cursorX = cursor.X / dpi.DpiScaleX;
        var cursorY = cursor.Y / dpi.DpiScaleY;

        return cursorX >= Left &&
               cursorX <= Left + ActualWidth &&
               cursorY >= Top &&
               cursorY <= Top + ActualHeight;
    }

    private static bool IsMouseButtonDown(int button)
    {
        return (GetAsyncKeyState(button) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
