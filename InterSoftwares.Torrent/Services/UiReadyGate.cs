namespace InterSoftwares.Torrent.Services
{
    public interface IUiReadyGate
    {
        Task WhenReadyAsync();
        void SignalReady();
    }

    public sealed class UiReadyGate : IUiReadyGate
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WhenReadyAsync() => _tcs.Task;
        public void SignalReady() { if (!_tcs.Task.IsCompleted) _tcs.SetResult(); }
    }
}
