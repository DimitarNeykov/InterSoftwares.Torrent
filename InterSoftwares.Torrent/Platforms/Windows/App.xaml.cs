using InterSoftwares.Torrent.Services;
using Microsoft.Windows.AppLifecycle;
using System.Collections.Concurrent;
using Windows.ApplicationModel.Activation;

namespace InterSoftwares.Torrent.WinUI
{
    public partial class App : MauiWinUIApplication
    {
        private static readonly ConcurrentQueue<Func<Task>> _pending = new();
        private static volatile bool _uiReady;

        public App()
        {
            InitializeComponent();

            try { AppInstance.GetCurrent().Activated += OnAppActivated; } catch { }
        }

        protected override MauiApp CreateMauiApp()
        {
            var app = MauiProgram.CreateMauiApp();
            MauiProgram.Services = app.Services;
            return app;
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            base.OnLaunched(args);

            _uiReady = true;
            _ = DrainPendingAsync();

            var argv = Environment.GetCommandLineArgs();
            if (argv.Length >= 2)
            {
                var arg = argv[1].Trim('"');
                if (File.Exists(arg))
                    EnqueueActivation(() => OpenTorrentsAsync(new[] { arg }));
                else if (arg.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                    EnqueueActivation(() => OpenMagnetAsync(arg));
            }
        }

        private void OnAppActivated(object? sender, AppActivationArguments e)
            => EnqueueActivation(() => DispatchActivationAsync(e));

        private static void EnqueueActivation(Func<Task> action)
        {
            _pending.Enqueue(action);
            if (_uiReady)
                _ = DrainPendingAsync();
        }

        private static async Task DrainPendingAsync()
        {
            while (_uiReady && _pending.TryDequeue(out var action))
            {
                await MainThread.InvokeOnMainThreadAsync(action);
            }
        }

        private static async Task DispatchActivationAsync(AppActivationArguments e)
        {
            switch (e.Kind)
            {
                case ExtendedActivationKind.File:
                    if (e.Data is IFileActivatedEventArgs fae)
                    {
                        var paths = fae.Files.OfType<Windows.Storage.StorageFile>().Select(f => f.Path);
                        await OpenTorrentsAsync(paths);
                    }
                    break;

                case ExtendedActivationKind.Protocol:
                    if (e.Data is IProtocolActivatedEventArgs pae &&
                        pae.Uri is not null &&
                        pae.Uri.Scheme.Equals("magnet", StringComparison.OrdinalIgnoreCase))
                    {
                        await OpenMagnetAsync(pae.Uri.AbsoluteUri);
                    }
                    break;
            }
        }

        private static Task OpenTorrentsAsync(IEnumerable<string> paths)
            => MauiProgram.Services.GetRequiredService<TorrentOpenCoordinator>()
                                   .OpenTorrentsAsync(paths);

        private static Task OpenMagnetAsync(string magnet)
            => MauiProgram.Services.GetRequiredService<TorrentOpenCoordinator>()
                                   .OpenMagnetAsync(magnet);
    }
}
