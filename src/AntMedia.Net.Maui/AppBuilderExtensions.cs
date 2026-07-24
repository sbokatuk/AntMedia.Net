// Lets XAML use a stable url instead of a clr-namespace string:
//   xmlns:antmedia="http://schemas.sbokatuk.com/antmedia/maui"
[assembly: Microsoft.Maui.Controls.XmlnsDefinition(
    "http://schemas.sbokatuk.com/antmedia/maui", "AntMedia.Net.Maui")]
[assembly: Microsoft.Maui.Controls.XmlnsPrefix(
    "http://schemas.sbokatuk.com/antmedia/maui", "antmedia")]

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
