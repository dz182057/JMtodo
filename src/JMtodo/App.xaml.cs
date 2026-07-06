using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using TodoDesktopApp.Data;
using TodoDesktopApp.Dialogs;
using TodoDesktopApp.Services;
using TodoDesktopApp.ViewModels;

namespace TodoDesktopApp;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstanceService;
    private TrayService? _trayService;
    private readonly UpdateService _updateService = new();
    private MainWindow? _mainWindow;
    private FloatingTaskWindow? _floatingWindow;
    private bool _isShuttingDown;
    private bool _isCheckingForUpdates;

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
            showFloating: () => Dispatcher.Invoke(() => _floatingWindow.ShowFromUserRequest()),
            hideFloating: () => Dispatcher.Invoke(() => _floatingWindow.HideFromUserRequest()),
            addTodo: () => Dispatcher.Invoke(() => _mainWindow.AddTodoFromUserRequest()),
            checkForUpdates: CheckForUpdatesFromTrayAsync,
            exit: () => Dispatcher.Invoke(ShutdownApplication),
            changeLanguage: language => Dispatcher.Invoke(() =>
            {
                var normalizedLanguage = LocalizationService.NormalizeLanguage(language);
                var currentSettings = settingsService.Load();
                currentSettings.Language = normalizedLanguage;
                settingsService.Save(currentSettings);
                LocalizationService.ApplyLanguage(normalizedLanguage);
            }));

        _mainWindow.Show();
        _singleInstanceService.StartListening();
        Dispatcher.BeginInvoke(new Action(_mainWindow.OpenFromUserRequest), DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        _updateService.Dispose();
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

    private async Task CheckForUpdatesFromTrayAsync()
    {
        if (_isCheckingForUpdates)
        {
            return;
        }

        _isCheckingForUpdates = true;
        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
            if (!result.UpdateAvailable)
            {
                ConfirmDialogWindow.ShowInfo(
                    GetDialogOwner(),
                    LocalizationService.Text("Dialog.Update.NoUpdateTitle"),
                    LocalizationService.Format("Dialog.Update.NoUpdateMessageFormat", result.CurrentVersion));
                return;
            }

            var message = LocalizationService.Format(
                "Dialog.Update.AvailableMessageFormat",
                result.LatestVersion,
                result.CurrentVersion,
                LimitReleaseNotes(result.ReleaseNotes));
            if (string.IsNullOrWhiteSpace(result.DownloadUrl))
            {
                ConfirmDialogWindow.ShowInfo(
                    GetDialogOwner(),
                    LocalizationService.Text("Dialog.Update.AvailableTitle"),
                    message);
                return;
            }

            var shouldDownload = ConfirmDialogWindow.ShowConfirm(
                GetDialogOwner(),
                LocalizationService.Text("Dialog.Update.AvailableTitle"),
                message,
                LocalizationService.Text("Dialog.Update.Download"),
                LocalizationService.Text("Dialog.Cancel"),
                "Button.Primary");

            if (shouldDownload)
            {
                OpenUrl(result.DownloadUrl);
            }
        }
        catch (Exception ex)
        {
            ConfirmDialogWindow.ShowInfo(
                GetDialogOwner(),
                LocalizationService.Text("Dialog.Update.FailedTitle"),
                GetUpdateFailureMessage(ex));
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }

    private Window? GetDialogOwner()
    {
        if (_mainWindow?.IsVisible == true)
        {
            return _mainWindow;
        }

        if (_floatingWindow?.IsVisible == true)
        {
            return _floatingWindow;
        }

        return null;
    }

    private static string LimitReleaseNotes(string releaseNotes)
    {
        const int maxLength = 420;
        if (releaseNotes.Length <= maxLength)
        {
            return releaseNotes;
        }

        return releaseNotes[..maxLength] + Environment.NewLine + LocalizationService.Text("Update.ReleaseNotesTruncated");
    }

    private static string GetUpdateFailureMessage(Exception exception)
    {
        return exception switch
        {
            TaskCanceledException => LocalizationService.Format("Update.TimeoutMessageFormat", UpdateService.ManifestUrl),
            HttpRequestException httpException when httpException.StatusCode is not null =>
                LocalizationService.Format("Update.HttpStatusMessageFormat", (int)httpException.StatusCode, UpdateService.ManifestUrl),
            HttpRequestException => LocalizationService.Format("Update.NetworkMessageFormat", UpdateService.ManifestUrl),
            JsonException => LocalizationService.Text("Update.InvalidManifest"),
            InvalidOperationException => exception.Message,
            _ => LocalizationService.Text("Update.UnknownFailure")
        };
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }
}
