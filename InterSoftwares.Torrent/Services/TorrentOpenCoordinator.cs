using InterSoftwares.Torrent.Components.Pages.Components;
using MonoTorrent;
using MudBlazor;

namespace InterSoftwares.Torrent.Services
{
    public sealed class TorrentOpenCoordinator
    {
        private readonly ITorrentEngineService _engine;

        public TorrentOpenCoordinator(ITorrentEngineService engine)
        {
            _engine = engine;
        }

        public async Task OpenTorrentsAsync(IEnumerable<string> torrentPaths)
        {
            await DialogBridge.WhenReadyAsync();

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var dialogs = DialogBridge.Current;

                foreach (var path in torrentPaths)
                {
                    var t = await MonoTorrent.Torrent.LoadAsync(path);

                    var dialogRef = await dialogs.ShowAsync<SelectFilesDialog>(
                        "Файлове",
                        new DialogParameters { { nameof(SelectFilesDialog.Torrent), t } },
                        new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Large, FullWidth = true });

                    var result = await dialogRef.Result;
                    if (result.Canceled || result.Data is not IList<FileSelection> selection)
                        continue;

                    var savePath = await Platforms.Windows.WinPickers.PickFolderAsync();
                    if (string.IsNullOrWhiteSpace(savePath))
                        continue;

                    await _engine.AddTorrentAsync(path, savePath!, selection);
                }
            });
        }

        public async Task OpenMagnetAsync(string magnet)
        {
            if (string.IsNullOrWhiteSpace(magnet))
                return;

            await DialogBridge.WhenReadyAsync();

            // 1) Показваме loading веднага (UI thread)
            IDialogReference? loadingRef = null;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var dialogs = DialogBridge.Current;

                loadingRef = await dialogs.ShowAsync<LoadingDialog>(
                    "Loading metadata…",
                    new DialogOptions
                    {
                        CloseOnEscapeKey = false,
                        BackdropClick = false,
                        MaxWidth = MaxWidth.Small,
                        FullWidth = true,
                    });
            });

            MonoTorrent.Torrent torrent;
            try
            {
                // 2) Metadata в background (НЕ на UI thread)
                torrent = await _engine.DownloadMetadataAsync(magnet);
            }
            catch
            {
                // 3) Затвори loading при грешка
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    try { loadingRef?.Close(); } catch { }
                });

                throw; // или покажи snackbar и return
            }

            // 4) Затваряме loading и показваме SelectFilesDialog (UI thread)
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try { loadingRef?.Close(); } catch { }

                var dialogs = DialogBridge.Current;

                var dialogRef = await dialogs.ShowAsync<SelectFilesDialog>(
                    "Файлове",
                    new DialogParameters { { nameof(SelectFilesDialog.Torrent), torrent } },
                    new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Large, FullWidth = true });

                var result = await dialogRef.Result;
                if (result.Canceled || result.Data is not IList<FileSelection> selection)
                    return;

                var savePath = await Platforms.Windows.WinPickers.PickFolderAsync();
                if (string.IsNullOrWhiteSpace(savePath))
                    return;

                await _engine.AddMagnetAsync(magnet, savePath!, selection);
            });
        }
    }
}
