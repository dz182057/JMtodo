using System.Windows;
using TodoDesktopApp.Data;
using TodoDesktopApp.Services;
using TodoDesktopApp.ViewModels;

namespace TodoDesktopApp;

public partial class App : System.Windows.Application
{
    private TrayService? _trayService;
    private MainWindow? _mainWindow;
    private FloatingTaskWindow? _floatingWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var repository = new TodoRepository();
        repository.Initialize();

        var todoService = new TodoService(repository);
        var settingsService = new SettingsService();
        var windowLevelService = new WindowLevelService();

        var floatingViewModel = new FloatingViewModel(todoService);
        _floatingWindow = new FloatingTaskWindow(floatingViewModel, settingsService, windowLevelService);

        _mainWindow = new MainWindow(todoService, _floatingWindow, windowLevelService);
        _trayService = new TrayService(
            openManager: () => Dispatcher.Invoke(() => _mainWindow.OpenFromUserRequest()),
            toggleFloating: () => Dispatcher.Invoke(() => _floatingWindow.ToggleFromUserRequest()),
            addTodo: () => Dispatcher.Invoke(() => _mainWindow.AddTodoFromUserRequest()),
            exit: () => Dispatcher.Invoke(() =>
            {
                _trayService?.Dispose();
                _mainWindow.AllowClose();
                _floatingWindow.AllowClose();
                Shutdown();
            }),
            isFloatingVisible: () => _floatingWindow.IsVisible);

        _mainWindow.Show();
        _floatingWindow.RefreshVisibilityFromTasks(activate: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        base.OnExit(e);
    }
}
