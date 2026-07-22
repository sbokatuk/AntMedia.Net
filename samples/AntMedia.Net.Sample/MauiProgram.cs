using AntMedia.Net.Maui;
using Microsoft.Extensions.Logging;

namespace AntMedia.Net.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            // Registers the handler for AntMediaVideoView. MAUI does not discover third-party
            // handlers on its own, so without this the view renders nothing.
            .UseAntMedia();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
