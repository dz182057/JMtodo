using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TodoDesktopApp.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string InstanceMutexName = @"Local\JMtodo.SingleInstance";
    private const string ShutdownEventName = @"Local\JMtodo.ShutdownForReplacement";
    private const int ReplacementShutdownTimeoutMs = 8000;
    private const int ForcedReplacementTimeoutMs = 4000;
    private const int ProcessKillTimeoutMs = 3000;

    private readonly Action _shutdownCurrentInstance;
    private readonly Mutex _mutex = new(false, InstanceMutexName);
    private readonly EventWaitHandle _shutdownEvent = new(false, EventResetMode.AutoReset, ShutdownEventName);
    private bool _ownsMutex;
    private bool _disposed;

    private SingleInstanceService(Action shutdownCurrentInstance)
    {
        _shutdownCurrentInstance = shutdownCurrentInstance;
    }

    public static SingleInstanceService? Start(Action shutdownCurrentInstance)
    {
        var service = new SingleInstanceService(shutdownCurrentInstance);

        if (!service.TryBecomeOwner(0))
        {
            service.SignalCurrentOwner();
            if (!service.TryBecomeOwner(ReplacementShutdownTimeoutMs))
            {
                TerminateOtherInstances(includeCurrentExecutablePath: true);
                if (!service.TryBecomeOwner(ForcedReplacementTimeoutMs))
                {
                    service.Dispose();
                    return null;
                }
            }
        }

        // 已发布的旧版本没有接管监听；不同路径通常表示用户打开了新版目录。
        TerminateOtherInstances(includeCurrentExecutablePath: false);
        return service;
    }

    public void StartListening()
    {
        var listenerThread = new Thread(ListenForShutdownRequests)
        {
            IsBackground = true,
            Name = "JMtodo single instance listener"
        };
        listenerThread.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdownEvent.Dispose();

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
    }

    private void SignalCurrentOwner()
    {
        try
        {
            _shutdownEvent.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool TryBecomeOwner(int millisecondsTimeout)
    {
        try
        {
            _ownsMutex = _mutex.WaitOne(millisecondsTimeout);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        return _ownsMutex;
    }

    private void ListenForShutdownRequests()
    {
        while (!_disposed)
        {
            try
            {
                _shutdownEvent.WaitOne();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (_disposed)
            {
                return;
            }

            _shutdownCurrentInstance();
        }
    }

    private static void TerminateOtherInstances(bool includeCurrentExecutablePath)
    {
        using var currentProcess = Process.GetCurrentProcess();
        var currentPath = TryGetProcessPath(currentProcess);

        foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
        {
            using var _ = process;
            if (process.Id == currentProcess.Id)
            {
                continue;
            }

            var processPath = TryGetProcessPath(process);
            if (!includeCurrentExecutablePath && IsSamePath(processPath, currentPath))
            {
                continue;
            }

            KillProcess(process);
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(ProcessKillTimeoutMs);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
        }
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return null;
        }
    }

    private static bool IsSamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
