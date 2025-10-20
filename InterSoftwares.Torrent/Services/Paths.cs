namespace InterSoftwares.Torrent.Services
{
    public static class Paths
    {
        public static string AppDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TorrentDownloader");

        public static string MetadataCacheDir => Path.Combine(AppDataRoot, "cache", "metadata");

        public static string FastResumePath(string infoHash) => Path.Combine(AppDataRoot, "fastresume", $"{infoHash}.bin");

        public static string TorrentCopyPath(string infoHash) => Path.Combine(AppDataRoot, "torrents", $"{infoHash}.torrent");
    }

}
