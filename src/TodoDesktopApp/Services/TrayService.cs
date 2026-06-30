using TodoDesktopApp.Views;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace TodoDesktopApp.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Action _openManager;
    private readonly Action _toggleFloating;
    private readonly Action _addTodo;
    private readonly Action _exit;
    private readonly Func<bool> _isFloatingVisible;
    private TrayMenuWindow? _trayMenuWindow;
    private bool _disposed;

    public TrayService(
        Action openManager,
        Action toggleFloating,
        Action addTodo,
        Action exit,
        Func<bool> isFloatingVisible)
    {
        _openManager = openManager;
        _toggleFloating = toggleFloating;
        _addTodo = addTodo;
        _exit = exit;
        _isFloatingVisible = isFloatingVisible;

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
            toggleFloating: _toggleFloating,
            addTask: _addTodo,
            exitApp: _exit,
            isFloatingVisible: _isFloatingVisible());

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
