using System.Windows;
using System.Windows.Threading;
using TodoDesktopApp.Data;
using TodoDesktopApp.Services;
using TodoDesktopApp.ViewModels;

namespace TodoDesktopApp;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstanceService;
    private TrayService? _trayService;
    private MainWindow? _mainWindow;
    private FloatingTaskWindow? _floatingWindow;
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceService = SingleInstanceService.Start(ShutdownFromReplacementRequest);
        if (_singleInstanceService is null)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var repository = new TodoRepository();
        repository.Initialize();

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        LocalizationService.ApplyLanguage(settings.Language);

        var todoService = new TodoService(repository);
        var windowLevelService = new WindowLevelService();

        var floatingViewModel = new FloatingViewModel(todoService);
        _floatingWindow = new FloatingTaskWindow(floatingViewModel, settingsService, windowLevelService);

        _mainWindow = new MainWindow(todoService, _floatingWindow, windowLevelService);
        _trayService = new TrayService(
            openManager: () => Dispatcher.Invoke(() => _mainWindow.OpenFromUserRequest()),
            toggleFloating: () => Dispatcher.Invoke(() => _floatingWindow.ToggleFromUserRequest()),
            addTodo: () => Dispatcher.Invoke(() => _mainWindow.AddTodoFromUserRequest()),
            exit: () => Dispatcher.Invoke(ShutdownApplication),
            changeLanguage: language => Dispatcher.Invoke(() =>
            {
                var normalizedLanguage = LocalizationService.NormalizeLanguage(language);
                var currentSettings = settingsService.Load();
                currentSettings.Language = normalizedLanguage;
                settingsService.Save(currentSettings);
                LocalizationService.ApplyLanguage(normalizedLanguage);
            }),
            isFloatingVisible: () => _floatingWindow.IsVisible);

        _mainWindow.Show();
        _singleInstanceService.StartListening();
        Dispatcher.BeginInvoke(new Action(_mainWindow.OpenFromUserRequest), DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        _singleInstanceService?.Dispose();
        base.OnExit(e);
    }

    private void ShutdownFromReplacementRequest()
    {
        Dispatcher.BeginInvoke(new Action(ShutdownApplication), DispatcherPriority.Send);
    }

    private void ShutdownApplication()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _trayService?.Dispose();
        _mainWindow?.AllowClose();
        _floatingWindow?.AllowClose();
        Shutdown();
    }
}
