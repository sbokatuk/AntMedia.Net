using Microsoft.Maui.Handlers;

#if ANDROID
using Org.Webrtc;
#elif IOS || MACCATALYST
using UIKit;
#endif

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

// The handler's platform half is selected with #if rather than by putting each platform's file
// under Platforms/. MAUI's SingleProject owns that convention and re-adds its own Platforms/<id>
// globs after this project's items are evaluated, so a hand-rolled include for Mac Catalyst
// (which reuses the iOS half) was silently dropped and the type ended up with no base class.

#if ANDROID

/// <summary>
/// Android half: the native view is org.webrtc's SurfaceViewRenderer, which is what
/// WebRTCClientBuilder takes for both the local preview and the remote stream.
/// </summary>
public partial class AntMediaVideoViewHandler : ViewHandler<AntMediaVideoView, SurfaceViewRenderer>
{
    /// <inheritdoc />
    protected override SurfaceViewRenderer CreatePlatformView() => new(Context);

    /// <inheritdoc />
    protected override void DisconnectHandler(SurfaceViewRenderer platformView)
    {
        // The renderer holds an EGL context and a native surface. Leaving it initialised leaks
        // both, and after a few navigations exhausts the surface pool.
        platformView.Release();
        base.DisconnectHandler(platformView);
    }
}

#elif IOS || MACCATALYST

/// <summary>
/// iOS half, shared with Mac Catalyst: the facade's SetLocalView/SetRemoteView take a plain UIView
/// and add their own renderer inside it, so there is nothing SDK-specific to create here.
///
/// On Mac Catalyst the view is real and lays out normally — UIKit is present there — but no video
/// ever arrives, because Ant Media publishes no Catalyst build of its WebRTC framework.
/// </summary>
public partial class AntMediaVideoViewHandler : ViewHandler<AntMediaVideoView, UIView>
{
    /// <inheritdoc />
    protected override UIView CreatePlatformView() => new() { BackgroundColor = UIColor.Black };
}

#endif

/// <summary>
/// Shared half of the handler. The platform half above declares the base class, which is what
/// binds <see cref="AntMediaVideoView" /> to that platform's native view type.
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
