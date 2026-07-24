using Microsoft.Maui.Handlers;

#if ANDROID
using Org.Webrtc;
#elif IOS || MACCATALYST
using CoreGraphics;
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
/// <remarks>
/// On Android the native view is a <c>SurfaceView</c>, which does not composite like ordinary
/// views: to overlap one video view on top of another — the classic small-preview-over-remote
/// layout — set <see cref="IsMediaOverlay" /> on the view on top, or the two will fight over one
/// z-position and mis-layer silently.
/// </remarks>
public class AntMediaVideoView : View
{
    /// <summary>Bindable <see cref="Scaling" />.</summary>
    public static readonly BindableProperty ScalingProperty = BindableProperty.Create(
        nameof(Scaling), typeof(AntMediaVideoScaling?), typeof(AntMediaVideoView));

    /// <summary>Bindable <see cref="Mirror" />.</summary>
    public static readonly BindableProperty MirrorProperty = BindableProperty.Create(
        nameof(Mirror), typeof(bool), typeof(AntMediaVideoView), false);

    /// <summary>Bindable <see cref="IsMediaOverlay" />.</summary>
    public static readonly BindableProperty IsMediaOverlayProperty = BindableProperty.Create(
        nameof(IsMediaOverlay), typeof(bool), typeof(AntMediaVideoView), false);

    /// <summary>
    /// How the video is fitted into the view. Null — the default — keeps each platform's own
    /// behaviour: local previews fill, playback fits.
    /// </summary>
    public AntMediaVideoScaling? Scaling
    {
        get => (AntMediaVideoScaling?)GetValue(ScalingProperty);
        set => SetValue(ScalingProperty, value);
    }

    /// <summary>
    /// Flips the video horizontally — the usual treatment for a front-camera preview, so users
    /// see themselves as in a mirror.
    /// </summary>
    public bool Mirror
    {
        get => (bool)GetValue(MirrorProperty);
        set => SetValue(MirrorProperty, value);
    }

    /// <summary>
    /// Android only: places this view's video surface above other video surfaces, which is what
    /// lets a preview overlap a full-screen remote view. Set it before the page appears — the
    /// platform applies z-order when the surface is attached to the window. No-op on iOS, where
    /// views composite normally.
    /// </summary>
    public bool IsMediaOverlay
    {
        get => (bool)GetValue(IsMediaOverlayProperty);
        set => SetValue(IsMediaOverlayProperty, value);
    }
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

    private static void MapScaling(AntMediaVideoViewHandler handler, AntMediaVideoView view)
    {
        // Null leaves the SDK's own default in place.
        if (view.Scaling is { } scaling)
        {
            handler.PlatformView.SetScalingType(scaling switch
            {
                AntMediaVideoScaling.Fit => RendererCommon.ScalingType.ScaleAspectFit,
                AntMediaVideoScaling.Balanced => RendererCommon.ScalingType.ScaleAspectBalanced,
                _ => RendererCommon.ScalingType.ScaleAspectFill,
            });
        }
    }

    private static void MapMirror(AntMediaVideoViewHandler handler, AntMediaVideoView view) =>
        handler.PlatformView.SetMirror(view.Mirror);

    private static void MapIsMediaOverlay(AntMediaVideoViewHandler handler, AntMediaVideoView view) =>
        handler.PlatformView.SetZOrderMediaOverlay(view.IsMediaOverlay);
}

#elif IOS || MACCATALYST

/// <summary>
/// iOS half, shared with Mac Catalyst: the facade's SetLocalView/SetRemoteView take a plain UIView
/// and add their own renderer inside it, so there is nothing SDK-specific to create here.
/// </summary>
public partial class AntMediaVideoViewHandler : ViewHandler<AntMediaVideoView, UIView>
{
    /// <inheritdoc />
    protected override UIView CreatePlatformView() => new() { BackgroundColor = UIColor.Black };

    private static void MapScaling(AntMediaVideoViewHandler handler, AntMediaVideoView view)
    {
        // Nothing to set here: the facade takes the content mode when the client attaches the
        // view, so AntMediaClientExtensions reads Scaling at that point.
    }

    private static void MapMirror(AntMediaVideoViewHandler handler, AntMediaVideoView view) =>
        // The SDK's renderer is a subview, so flipping the container flips the video.
        handler.PlatformView.Transform = view.Mirror
            ? CGAffineTransform.MakeScale(-1, 1)
            : CGAffineTransform.MakeIdentity();

    private static void MapIsMediaOverlay(AntMediaVideoViewHandler handler, AntMediaVideoView view)
    {
        // Android-only concept; UIKit composites views normally.
    }
}

#endif

/// <summary>
/// Shared half of the handler. The platform half above declares the base class, which is what
/// binds <see cref="AntMediaVideoView" /> to that platform's native view type.
/// </summary>
public partial class AntMediaVideoViewHandler
{
    /// <summary>Maps the view's properties onto the native renderer.</summary>
    public static readonly IPropertyMapper<AntMediaVideoView, AntMediaVideoViewHandler> VideoMapper =
        new PropertyMapper<AntMediaVideoView, AntMediaVideoViewHandler>(ViewHandler.ViewMapper)
        {
            [nameof(AntMediaVideoView.Scaling)] = MapScaling,
            [nameof(AntMediaVideoView.Mirror)] = MapMirror,
            [nameof(AntMediaVideoView.IsMediaOverlay)] = MapIsMediaOverlay,
        };

    /// <summary>Creates the handler. Registered by <see cref="AppBuilderExtensions.UseAntMedia" />.</summary>
    public AntMediaVideoViewHandler() : base(VideoMapper)
    {
    }
}
