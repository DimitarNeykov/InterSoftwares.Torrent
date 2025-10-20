namespace InterSoftwares.Torrent.Services
{
    public static class DialogBridge
    {
        private static readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public static MudBlazor.IDialogService Current { get; private set; } = default!;

        public static void Initialize(MudBlazor.IDialogService dialogs)
        {
            if (Current is null)
            {
                Current = dialogs;
                _ready.TrySetResult();
            }
        }

        public static Task WhenReadyAsync() => _ready.Task;
    }
}
