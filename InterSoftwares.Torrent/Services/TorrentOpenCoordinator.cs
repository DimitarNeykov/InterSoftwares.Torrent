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
            await Task.CompletedTask;
        }
    }
}
