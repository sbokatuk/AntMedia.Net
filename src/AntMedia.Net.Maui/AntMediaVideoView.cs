using Microsoft.Maui.Handlers;

namespace AntMedia.Net.Maui;

/// <summary>
/// A MAUI view that the SDK renders video into — an <c>org.webrtc.SurfaceViewRenderer</c> on
/// Android, a plain <c>UIView</c> on iOS.
///
/// A custom view rather than a wrapped <c>ContentView</c> because both SDKs want the native view
/// itself: Android's builder takes the renderer, and the iOS facade puts its own renderer inside
/// the UIView you hand it.
/// </summary>
public class AntMediaVideoView : View
{
}

/// <summary>
/// Shared half of the handler. Each platform's half declares the base class, which is what binds
/// <see cref="AntMediaVideoView" /> to that platform's native view type.
/// </summary>
public partial class AntMediaVideoViewHandler
{
    /// <summary>
    /// The view has no properties of its own; the mapper exists because a handler needs one.
    /// </summary>
    public static readonly IPropertyMapper<AntMediaVideoView, AntMediaVideoViewHandler> VideoMapper =
        new PropertyMapper<AntMediaVideoView, AntMediaVideoViewHandler>(ViewHandler.ViewMapper);

    /// <summary>Creates the handler. Registered by <see cref="AppBuilderExtensions.UseAntMedia" />.</summary>
    public AntMediaVideoViewHandler() : base(VideoMapper)
    {
    }
}
