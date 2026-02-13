using System.IO.Pipes;
using System.Text;

namespace InterSoftwares.Torrent.Services;

public static class SingleInstanceIpc
{
    public const string MutexName = "InterSoftwares.Torrent.SingleInstance";
    public const string PipeName = "InterSoftwares.Torrent.Pipe";

    // Second instance: send args to first instance
    public static async Task<bool> TrySendToPrimaryAsync(string[] args, CancellationToken ct = default)
    {
        if (args is null || args.Length == 0)
            return false;

        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: PipeName,
                direction: PipeDirection.Out);

            // ако първата инстанция не слуша - няма смисъл да чакаме много
            await client.ConnectAsync(1500, ct);

            var payload = string.Join('\n', args);
            var bytes = Encoding.UTF8.GetBytes(payload);

            await client.WriteAsync(bytes, 0, bytes.Length, ct);
            await client.FlushAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Primary instance: listen for args from other launches
    public static async Task RunServerLoopAsync(
        Func<string[], Task> onArgs,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                using var ms = new MemoryStream();
                await server.CopyToAsync(ms, ct);

                var text = Encoding.UTF8.GetString(ms.ToArray());
                var args = text
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (args.Length > 0)
                    await onArgs(args);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // ignore and keep listening
            }
        }
    }
}
