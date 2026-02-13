namespace InterSoftwares.Torrent.Services
{
    public class MagnetActivationService
    {
        public event Func<string, Task>? MagnetReceived;

        public async Task RaiseMagnet(string magnet)
        {
            if (MagnetReceived != null)
                await MagnetReceived.Invoke(magnet);
        }
    }
}
