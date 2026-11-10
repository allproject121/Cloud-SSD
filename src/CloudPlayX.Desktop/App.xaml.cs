using System.Threading;
using System.Windows;
using CloudPlayX.App;
using CloudPlayX.Desktop.ViewModels;
using CloudPlayX.Desktop.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CloudPlayX.Desktop;

public partial class App : Application
{
    private Mutex? _single;
    private AppHost? _host;
    private bool _shuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dbgLog = @"C:\Users\rosha\AppData\Local\Cloud-SSD\startup_debug.txt";
        try
        {
            System.IO.File.AppendAllText(dbgLog, $"[{DateTime.Now:O}] OnStartup entered\n");
            bool hasOwnership = false;
            try
            {
                _single = new Mutex(true, @"Local\CloudSSD.Desktop.SingleInstance", out var created);
                hasOwnership = created;
                if (!hasOwnership)
                {
                    hasOwnership = _single.WaitOne(TimeSpan.FromMilliseconds(500), false);
                }
            }
            catch (AbandonedMutexException)
            {
                hasOwnership = true;
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(dbgLog, $"[{DateTime.Now:O}] Mutex exception: {ex}\n");
                hasOwnership = true;
            }

            System.IO.File.AppendAllText(dbgLog, $"[{DateTime.Now:O}] hasOwnership={hasOwnership}\n");
            if (!hasOwnership)
            {
                System.IO.File.AppendAllText(dbgLog, $"[{DateTime.Now:O}] Exiting: already running\n");
                MessageBox.Show("Cloud Drive is already running.", "Cloud Drive", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            _host = AppHost.Create();
            var lockAcquired = _host.TryAcquireInstanceLock();
            System.IO.File.AppendAllText(dbgLog, $"[{DateTime.Now:O}] lockAcquired={lockAcquired}\n");
            if (!lockAcquired)
            {
                System.IO.File.AppendAllText(dbgLog, $"[{DateTime.Now:O}] Exiting: lock not acquired\n");
                MessageBox.Show("Another Cloud Drive process (for example the command-line tool) is using the data folder.", "Cloud Drive");
                Shutdown();
                return;
            }
            DispatcherUnhandledException += (_, args) =>
            {
                System.IO.File.AppendAllText(dbgLog, $"[{DateTime.Now:O}] DispatcherUnhandledException: {args.Exception}\n");
                _host.LoggerFactory.CreateLogger("App").LogError(args.Exception, "Unhandled UI exception");
                MessageBox.Show(Diagnostics.Redactor.Redact(args.Exception.Message), "Cloud Drive error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            SystemEvents.PowerModeChanged += (_, args) =>
            {
                if (args.Mode == PowerModes.Resume && _host != null) _ = _host.Mounts.OnSystemResumeAsync();
            };

            var vm = new MainViewModel(_host);
            var window = new MainWindow { DataContext = vm };
            MainWindow = window;
            window.Closing += OnClosing;
            window.Show();
            System.IO.File.AppendAllText(dbgLog, $"[{DateTime.Now:O}] Window shown\n");

            try
            {
                await _host.InitializeAsync();
                System.IO.File.AppendAllText(dbgLog, $"[{DateTime.Now:O}] Host initialized\n");
                // Keep exactly the two standard drives (each account / all accounts); also tidies up older layouts.
                if (_host.Remotes.Count > 0 || _host.Settings.Mounts.Count > 0) await _host.SyncStandardDrivesAsync(removeUnfinishedLogins: true);
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(dbgLog, $"[{DateTime.Now:O}] Host init exception: {ex}\n");
                MessageBox.Show(window, "Startup problem: " + Diagnostics.Redactor.Redact(ex.Message), "Cloud Drive", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (!_host.Settings.FirstRunCompleted)
            {
                var first = new FirstRunWindow(_host) { Owner = window };
                first.ShowDialog();
            }
            vm.Selected.OnShown();
            System.IO.File.AppendAllText(dbgLog, $"[{DateTime.Now:O}] Startup completed successfully!\n");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(dbgLog, $"[{DateTime.Now:O}] FATAL STARTUP EXCEPTION: {ex}\n");
            throw;
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shuttingDown || _host is null) return;
        e.Cancel = true;
        var keep = _host.Settings.KeepMountsAfterExit;
        if (_host.Mounts.Sessions.Count > 0)
        {
            var running = _host.CurrentLaunch is { State: Games.LaunchState.Running };
            var answer = MessageBox.Show(MainWindow!,
                (running ? "A game is still running.\n\n" : "") +
                "Keep the cloud drives mounted after closing Cloud Drive?\n\nYes: drives stay available and are re-attached next time.\nNo: drives are unmounted (pending uploads get up to 60 s to finish).",
                "Close Cloud Drive", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, keep || running ? MessageBoxResult.Yes : MessageBoxResult.No);
            if (answer == MessageBoxResult.Cancel) return;
            keep = answer == MessageBoxResult.Yes;
        }
        _shuttingDown = true;
        MainWindow!.IsEnabled = false;
        MainWindow.Title = "Cloud Drive — closing…";
        try
        {
            await _host.ShutdownAsync(keep);
            await _host.DisposeAsync();
        }
        catch (Exception) { }
        _single?.ReleaseMutex();
        Shutdown();
    }
}
