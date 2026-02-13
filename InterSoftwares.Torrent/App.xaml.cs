using System.Threading;
using InterSoftwares.Torrent.Services;

namespace InterSoftwares.Torrent;

public partial class App : Application
{
    private static Mutex? _mutex;
    private bool _isPrimaryInstance;
    private readonly CancellationTokenSource _ipcCts = new();

    public App()
    {
        InitializeComponent();
        MainPage = new MainPage();

        var startupArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

        // 1) Try to become primary
        _mutex = new Mutex(false, SingleInstanceIpc.MutexName);

        try
        {
            _isPrimaryInstance = _mutex.WaitOne(TimeSpan.Zero, false);
        }
        catch (AbandonedMutexException)
        {
            _isPrimaryInstance = true;
        }
        catch
        {
            _isPrimaryInstance = false;
        }

        if (!_isPrimaryInstance)
        {
            // 2) We think we're secondary -> try to forward to the primary
            _ = Task.Run(async () =>
            {
                // If there are args, try send them
                var sent = startupArgs.Length > 0
                    ? await SingleInstanceIpc.TrySendToPrimaryAsync(startupArgs)
                    : await SingleInstanceIpc.TrySendToPrimaryAsync(new[] { "__PING__" });

                // If we successfully talked to a primary instance -> exit
                if (sent)
                {
                    Environment.Exit(0);
                    return;
                }

                // 3) No primary is listening (pipe connect failed) -> fallback:
                // allow THIS instance to continue as primary
                _isPrimaryInstance = true;

                // Start IPC + handle initial args
                _ = Task.Run(() => SingleInstanceIpc.RunServerLoopAsync(HandleExternalArgsAsync, _ipcCts.Token));
                if (startupArgs.Length > 0)
                    _ = Task.Run(() => HandleExternalArgsAsync(startupArgs));
            });

            // IMPORTANT: do NOT return; we allow UI to start
            return;
        }

        // Primary instance: start IPC server loop
        _ = Task.Run(() => SingleInstanceIpc.RunServerLoopAsync(HandleExternalArgsAsync, _ipcCts.Token));

        if (startupArgs.Length > 0)
            _ = Task.Run(() => HandleExternalArgsAsync(startupArgs));
    }

    private async Task HandleExternalArgsAsync(string[] args)
    {
        var items = args
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Where(a => a != "__PING__")
            .Select(a => Uri.UnescapeDataString(a).Trim())
            .ToArray();

        if (items.Length == 0)
            return;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var sp = Current?.Handler?.MauiContext?.Services;
            if (sp is null) return;

            var coordinator = sp.GetService<TorrentOpenCoordinator>();
            if (coordinator is null) return;

            foreach (var s in items)
            {
                if (s.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                    await coordinator.OpenMagnetAsync(s);
                else if (File.Exists(s) && s.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                    await coordinator.OpenTorrentsAsync([s]);
            }
        });
    }

    protected override void CleanUp()
    {
        base.CleanUp();

        _ipcCts.Cancel();

        try
        {
            if (_isPrimaryInstance)
                _mutex?.ReleaseMutex();
        }
        catch { }

        _mutex?.Dispose();
    }
}
