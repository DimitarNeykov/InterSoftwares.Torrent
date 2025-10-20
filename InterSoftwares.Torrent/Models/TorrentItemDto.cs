namespace InterSoftwares.Torrent.Models
{
    public sealed class TorrentItemDto
    {
        public string InfoHash { get; init; } = "";
        public string Name { get; init; } = "";
        public string TorrentPath { get; init; } = "";
        public string SavePath { get; set; } = "";
        public double Progress { get; set; }
        public long Downloaded { get; set; }
        public long Uploaded { get; set; }
        public long TotalSize { get; set; }
        public double DownSpeed { get; set; }
        public double UpSpeed { get; set; }
        public string Status { get; set; } = "Queued";
        public bool Paused { get; set; }
    }
}
