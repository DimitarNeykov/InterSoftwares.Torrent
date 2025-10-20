using Windows.Storage.Pickers;
using WinRT.Interop;

namespace InterSoftwares.Torrent.Platforms.Windows
{
    public static class WinPickers
    {
        public static async Task<string?> PickTorrentAsync()
        {
            var picker = new FileOpenPicker();
            var hwnd = GetWindowHandle();
            InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Add(".torrent");

            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }

        public static async Task<string?> PickFolderAsync()
        {
            var picker = new FolderPicker();
            var hwnd = GetWindowHandle();
            InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }

        private static nint GetWindowHandle()
        {
            var window = App.Current?.Windows?.FirstOrDefault();
            if (window?.Handler?.PlatformView is MauiWinUIWindow win)
                return WindowNative.GetWindowHandle(win);

            throw new InvalidOperationException("No WinUI window handle available.");
        }
    }
}
