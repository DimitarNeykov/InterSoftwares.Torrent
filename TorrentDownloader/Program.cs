using System;
using System.Text;
using System.Threading.Tasks;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections;

namespace TorrentDownloader
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            Console.WriteLine("Моля, посочете пътя до .torrent файла като аргумент.");
            string torrentPath = Console.ReadLine()!;

            Console.WriteLine("Моля, посочете директория в която да се свали файла.");
            string downloadPath = Console.ReadLine()!;

            Torrent torrent = null;
            try
            {
                // Зареждаме торент файла асинхронно
                torrent = await Torrent.LoadAsync(torrentPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Грешка при зареждане на .torrent файла: {ex.Message}");
                return;
            }

            // Конфигурация на настройките за клиентския енджин
            EngineSettings engineSettings = new EngineSettings();

            TorrentSettings torrentSettings = new TorrentSettings();
            ClientEngine engine = new ClientEngine(engineSettings);
            TorrentManager manager = await engine.AddAsync(torrent, downloadPath, torrentSettings);

            await manager.StartAsync();

            Console.WriteLine($"Свалянето започна за: {torrent.Name}");

            // Главен цикъл за показване на прогреса
            while (manager.State != TorrentState.Seeding)
            {
                // Изчиства конзолата, за да се обновява информацията
                Console.Clear();
                Console.WriteLine($"Сваляне: {torrent.Name}");
                Console.WriteLine($"Прогрес: {manager.Progress:0.00}%");
                Console.WriteLine($"Скорост на сваляне: {manager.Monitor.DownloadRate / 1024.0:0.00} kB/s");
                Console.WriteLine($"Скорост на качване: {manager.Monitor.UploadRate / 1024.0:0.00} kB/s");
                Console.WriteLine($"Брой свързани пиринги: {manager.Peers.Seeds}");
                Console.WriteLine("Натиснете Q за изход, ако желаете да прекратите.");

                // Проверка за натискане на клавиш за изход
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                    {
                        Console.WriteLine("Прекратяване на свалянето...");
                        break;
                    }
                }

                await Task.Delay(1000);
            }

            // Спиране на мениджъра и енджина
            await manager.StopAsync();
            Console.WriteLine("Свалянето приключи. Натиснете всяка клавиш за изход.");
            Console.ReadKey();
        }
    }
}
