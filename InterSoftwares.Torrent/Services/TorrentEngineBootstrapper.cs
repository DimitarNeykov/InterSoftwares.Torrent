using Microsoft.Extensions.Hosting;

namespace InterSoftwares.Torrent.Services
{
    public sealed class TorrentEngineBootstrapper : IHostedService
    {
        private readonly ITorrentEngineService _engine;

        public TorrentEngineBootstrapper(ITorrentEngineService engine) => _engine = engine;

        public async Task StartAsync(CancellationToken ct) => await _engine.InitializeAsync();

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
