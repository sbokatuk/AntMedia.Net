namespace AntMedia.Net.Maui;

/// <summary>Wires the package into a MAUI app.</summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Registers the handler for <see cref="AntMediaVideoView" />. Without this the view has no
    /// native counterpart and renders nothing — MAUI does not discover third-party handlers on
    /// its own.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.UseMauiApp&lt;App&gt;().UseAntMedia();
    /// </code>
    /// </example>
    public static MauiAppBuilder UseAntMedia(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<AntMediaVideoView, AntMediaVideoViewHandler>());

        return builder;
    }
}
