using InterSoftwares.Torrent.Services;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace InterSoftwares.Torrent
{
    public static class MauiProgram
    {
        public static IServiceProvider Services { get; set; } = default!;

        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddMudServices();

            builder.Services.AddSingleton<ITorrentEngineService, TorrentEngineService>();
            builder.Services.AddSingleton<IUiReadyGate, UiReadyGate>();
            builder.Services.AddSingleton<TorrentOpenCoordinator>();
            builder.Services.AddSingleton<MagnetActivationService>();

            builder.Services.AddHostedService<TorrentEngineBootstrapper>();
#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif
            var app = builder.Build();
            Services = app.Services;
            return app;
        }
    }
}
