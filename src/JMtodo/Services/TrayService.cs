using TodoDesktopApp.Views;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace TodoDesktopApp.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Action _openManager;
    private readonly Action _showFloating;
    private readonly Action _hideFloating;
    private readonly Action _addTodo;
    private readonly Func<Task> _checkForUpdates;
    private readonly Action _exit;
    private readonly Action<string> _changeLanguage;
    private TrayMenuWindow? _trayMenuWindow;
    private bool _disposed;

    public TrayService(
        Action openManager,
        Action showFloating,
        Action hideFloating,
        Action addTodo,
        Func<Task> checkForUpdates,
        Action exit,
        Action<string> changeLanguage)
    {
        _openManager = openManager;
        _showFloating = showFloating;
        _hideFloating = hideFloating;
        _addTodo = addTodo;
        _checkForUpdates = checkForUpdates;
        _exit = exit;
        _changeLanguage = changeLanguage;

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "JMtodo",
            Icon = CreateTrayIcon(),
            Visible = true
        };

        _notifyIcon.MouseUp += NotifyIcon_MouseUp;
    }

    private void NotifyIcon_MouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Right)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(ShowTrayMenu);
            return;
        }

        if (e.Button == Forms.MouseButtons.Left)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(_openManager);
        }
    }

    private void ShowTrayMenu()
    {
        _trayMenuWindow?.Close();

        var trayMenuWindow = new TrayMenuWindow(
            openMain: _openManager,
            showFloating: _showFloating,
            hideFloating: _hideFloating,
            addTask: _addTodo,
            checkForUpdates: _checkForUpdates,
            exitApp: _exit,
            changeLanguage: _changeLanguage);

        _trayMenuWindow = trayMenuWindow;
        trayMenuWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_trayMenuWindow, trayMenuWindow))
            {
                _trayMenuWindow = null;
            }
        };
        trayMenuWindow.ShowNearCursor();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayMenuWindow?.Close();
        _trayMenuWindow = null;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private static Icon CreateTrayIcon()
    {
        var streamInfo = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/JM_glass_alpha0_windows.ico"));
        if (streamInfo?.Stream is null)
        {
            return SystemIcons.Application;
        }

        using var stream = streamInfo.Stream;
        return new Icon(stream);
    }
}
