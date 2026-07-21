using AntMedia.Net.Sample.Streaming;
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
            .ConfigureMauiHandlers(handlers =>
            {
                // VideoSurface has no built-in handler; this is what maps it to the native view
                // each platform's SDK renders into.
                handlers.AddHandler<VideoSurface, VideoSurfaceHandler>();
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
