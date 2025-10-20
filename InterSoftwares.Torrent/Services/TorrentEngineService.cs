namespace InterSoftwares.Torrent.Services
{
    using MonoTorrent;
    using MonoTorrent.Client;
    using System.Collections.Concurrent;
    using System.Text.Json;
    using InterSoftwares.Torrent.Models;

    public interface ITorrentEngineService
    {
        Task InitializeAsync();
        Task<TorrentItemDto> AddTorrentAsync(string torrentFilePath, string savePath, IList<FileSelection>? selection = null);
        IReadOnlyCollection<TorrentItemDto> Items { get; }
        Task PauseAsync(string infoHash);
        Task ResumeAsync(string infoHash);
        Task StopAsync(string infoHash);
        Task RemoveAsync(string infoHash, bool removeData = false);
        Task UpdateSelectionAsync(string infoHash, IList<FileSelection> selection, bool replace = true);
        Task<IReadOnlyList<TorrentFileEntry>> GetFilesAsync(string infoHash);
    }

    public sealed class FileSelection
    {
        public required string Path { get; init; }
        public bool Download { get; init; } = true;
        public Priority Priority { get; init; } = Priority.DoNotDownload;
    }

    public sealed record TorrentFileEntry(string Path, long Length, Priority Priority, bool Download);

    public sealed class TorrentEngineService : ITorrentEngineService, IAsyncDisposable
    {
        private readonly ClientEngine _engine;
        private readonly ConcurrentDictionary<string, (TorrentManager Mgr, TorrentItemDto Dto)> _items = new();
        private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(1));
        private readonly SemaphoreSlim _indexLock = new(1, 1);

        public IReadOnlyCollection<TorrentItemDto> Items => _items.Values.Select(v => v.Dto).ToList();

        public TorrentEngineService()
        {
            Directory.CreateDirectory(Paths.AppDataRoot);
            Directory.CreateDirectory(Paths.MetadataCacheDir);
            Directory.CreateDirectory(Path.Combine(Paths.AppDataRoot, "fastresume"));
            Directory.CreateDirectory(Path.Combine(Paths.AppDataRoot, "torrents"));

            var settings = new EngineSettingsBuilder
            {
                AutoSaveLoadFastResume = false,
                AllowPortForwarding = true,
                ListenEndPoints = new() { { "ipv4", new System.Net.IPEndPoint(System.Net.IPAddress.Any, 55123) } },
                CacheDirectory = Paths.MetadataCacheDir,
            }.ToSettings();

            _engine = new ClientEngine(settings);
            _ = PumpStatsAsync();
        }

        public async Task InitializeAsync()
        {
            Directory.CreateDirectory(Paths.AppDataRoot);
            var indexPath = Path.Combine(Paths.AppDataRoot, "index.json");
            List<TorrentItemDto> list;
            try
            {
                list = File.Exists(indexPath)
                    ? (JsonSerializer.Deserialize<List<TorrentItemDto>>(await File.ReadAllTextAsync(indexPath)) ?? [])
                    : [];
            }
            catch { list = []; }

            foreach (var dto in list)
            {
                try
                {
                    var copy = Paths.TorrentCopyPath(dto.InfoHash);
                    if (!File.Exists(copy)) continue;

                    var torrent = await Torrent.LoadAsync(copy);
                    var mgr = await CreateManagerAsync(torrent, dto.SavePath);

                    await TryLoadFastResumeAsync(mgr);

                    _items[dto.InfoHash] = (mgr, dto);
                    if (!dto.Paused) await mgr.StartAsync();
                }
                catch {  }
            }
        }

        public async Task<IReadOnlyList<TorrentFileEntry>> GetFilesAsync(string infoHash)
        {
            if (!_items.TryGetValue(infoHash, out var v))
                return Array.Empty<TorrentFileEntry>();

            return v.Mgr.Files.Select(f =>
                new TorrentFileEntry(
                    f.Path,
                    (long)f.Length,
                    f.Priority,
                    f.Priority != Priority.DoNotDownload
                )).ToList();
        }

        public async Task<TorrentItemDto> AddTorrentAsync(string torrentFilePath, string savePath, IList<FileSelection>? selection = null)
        {
            var torrent = await Torrent.LoadAsync(torrentFilePath);
            var mgr = await CreateManagerAsync(torrent, savePath);
            static string N(string p) => p.Replace('\\', '/');

            foreach (var file in mgr.Files)
                await mgr.SetFilePriorityAsync(file, Priority.DoNotDownload);

            if (selection is not null && selection.Count > 0)
            {
                var wanted = new HashSet<string>(
                    selection.Where(s => s.Download).Select(s => N(s.Path)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var f in mgr.Files)
                {
                    if (wanted.Contains(N(f.Path)))
                    {
                        var sel = selection.First(s =>
                            string.Equals(N(s.Path), N(f.Path), StringComparison.OrdinalIgnoreCase));

                        await mgr.SetFilePriorityAsync(f, sel.Priority);
                    }
                }
            }

            await SaveFastResumeAsync(mgr);

            await mgr.StartAsync();

            var dto = new TorrentItemDto
            {
                InfoHash = mgr.InfoHashes.V1OrV2.ToHex(),
                Name = torrent.Name,
                TorrentPath = SaveTorrentCopy(torrent, torrentFilePath, mgr),
                SavePath = savePath,
                TotalSize = torrent.Files.Sum(f => (long)f.Length),
                Status = "Downloading",
                Paused = false
            };

            _items[dto.InfoHash] = (mgr, dto);
            await PersistIndexAsync();
            return dto;
        }
        public async Task UpdateSelectionAsync(string infoHash, IList<FileSelection> selection, bool replace = true)
        {
            if (!_items.TryGetValue(infoHash, out var v)) return;
            var mgr = v.Mgr;

            static string N(string p) => p.Replace('\\', '/');

            if (replace)
            {
                foreach (var f in mgr.Files)
                    await mgr.SetFilePriorityAsync(f, Priority.DoNotDownload);
            }

            foreach (var sel in selection)
            {
                var f = mgr.Files.FirstOrDefault(x =>
                    string.Equals(N(x.Path), N(sel.Path), StringComparison.OrdinalIgnoreCase));
                if (f is null) continue;

                await mgr.SetFilePriorityAsync(f, sel.Download ? sel.Priority : Priority.DoNotDownload);
            }

            await SaveFastResumeAsync(mgr);
        }

        public async Task PauseAsync(string infoHash)
        {
            if (!_items.TryGetValue(infoHash, out var v)) return;
            await v.Mgr.PauseAsync();
            v.Dto.Paused = true;
            v.Dto.Status = "Paused";
            await SaveFastResumeAsync(v.Mgr);
            await PersistIndexAsync();
        }

        public async Task ResumeAsync(string infoHash)
        {
            if (!_items.TryGetValue(infoHash, out var v)) return;
            await v.Mgr.StartAsync();
            v.Dto.Paused = false;
            v.Dto.Status = "Downloading";
            await PersistIndexAsync();
        }

        public async Task StopAsync(string infoHash)
        {
            if (!_items.TryGetValue(infoHash, out var v)) return;

            try
            {
                await v.Mgr.StopAsync();
                await SaveFastResumeAsync(v.Mgr);
            }
            catch { }

            v.Dto.Paused = true;
            v.Dto.Status = "Stopped";

            await PersistIndexAsync();
        }

        public async Task RemoveAsync(string infoHash, bool removeData = false)
        {
            if (!_items.TryRemove(infoHash, out var v)) return;
            try { await v.Mgr.StopAsync(); } catch { }
            try { await _engine.RemoveAsync(v.Mgr); } catch { }

            if (removeData && Directory.Exists(v.Dto.SavePath))
            {
                try { Directory.Delete(v.Dto.SavePath, true); } catch { }
            }

            TryDelete(Paths.FastResumePath(infoHash));
            TryDelete(Paths.TorrentCopyPath(infoHash));
            await PersistIndexAsync();

            static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
        }

        private async Task<TorrentManager> CreateManagerAsync(Torrent torrent, string savePath)
        {
            var mgr = await _engine.AddAsync(
                torrent,
                savePath,
                new TorrentSettingsBuilder
                {
                    UploadSlots = 8,
                    MaximumConnections = 60
                }.ToSettings());

            mgr.TorrentStateChanged += async (_, e) =>
            {
                try
                {
                    if (e.NewState == TorrentState.Stopped || e.NewState == TorrentState.Error)
                        await SaveFastResumeAsync(mgr);
                    await PersistIndexAsync();
                }
                catch (Exception ex) { }
            };

            await TryLoadFastResumeAsync(mgr);
            return mgr;
        }

        private async Task TryLoadFastResumeAsync(TorrentManager mgr)
        {
            var frPath = Paths.FastResumePath(mgr.InfoHashes.V1OrV2.ToHex());
            if (!File.Exists(frPath)) return;

            try
            {
                using var s = File.OpenRead(frPath);
                if (FastResume.TryLoad(s, out FastResume? fr) && fr is not null)
                {
                    await mgr.LoadFastResumeAsync(fr);
                }
                else
                {
                    try { File.Delete(frPath); } catch { }
                }
            }
            catch
            {
                try { File.Delete(frPath); } catch { }
            }
        }

        private async Task SaveFastResumeAsync(TorrentManager mgr)
        {
            try
            {
                var fr = await mgr.SaveFastResumeAsync();
                await using var s = File.Create(Paths.FastResumePath(mgr.InfoHashes.V1OrV2.ToHex()));
                fr.Encode(s);
            }
            catch { }
        }

        private static string SaveTorrentCopy(Torrent torrent, string originalPath, TorrentManager mgr)
        {
            var path = Paths.TorrentCopyPath(mgr.InfoHashes.V1OrV2.ToHex());
            if (!File.Exists(path))
                File.Copy(originalPath, path, overwrite: true);
            return path;
        }

        private async Task PersistIndexAsync()
        {
            await _indexLock.WaitAsync();
            try
            {
                var indexPath = Path.Combine(Paths.AppDataRoot, "index.json");
                var tempPath = indexPath + ".tmp";

                var list = _items.Values.Select(v => v.Dto).ToList();
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(tempPath, json);

                if (File.Exists(indexPath))
                    File.Replace(tempPath, indexPath, null);
                else
                    File.Move(tempPath, indexPath);
            }
            catch (Exception ex)
            {
                try
                {
                    var tmp = Path.Combine(Paths.AppDataRoot, "index.json.tmp");
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch { }
            }
            finally
            {
                _indexLock.Release();
            }
        }

        private async Task PumpStatsAsync()
        {
            while (await _timer.WaitForNextTickAsync())
            {
                try
                {
                    foreach (var (mgr, dto) in _items.Values.ToArray())
                    {
                        if (mgr.Engine is null) continue;

                        var m = mgr.Monitor;
                        dto.DownSpeed = m.DownloadRate;
                        dto.UpSpeed = m.UploadRate;
                        dto.Downloaded = m.DataBytesReceived + m.ProtocolBytesReceived;
                        dto.Uploaded = m.DataBytesSent + m.ProtocolBytesSent;
                        dto.Progress = mgr.PartialProgress;
                        dto.Status = mgr.State.ToString();
                    }
                }
                catch { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var v in _items.Values)
            {
                try { await SaveFastResumeAsync(v.Mgr); } catch { }
                try { await v.Mgr.StopAsync(); } catch { }
            }
            try { _engine.Dispose(); } catch { }
            _timer.Dispose();
        }
    }
}
