using Microsoft.Maui.Handlers;

namespace AntMedia.Net.Sample.Streaming;

/// <summary>
/// A MAUI view backed by whatever native view the platform's SDK renders into — an
/// <c>org.webrtc.SurfaceViewRenderer</c> on Android, a plain <c>UIView</c> on iOS.
///
/// A custom view rather than a wrapped <c>ContentView</c> because both SDKs want the native view
/// itself, not a container: Android's builder takes the renderer, and the iOS facade's
/// SetLocalView/SetRemoteView take a UIView.
/// </summary>
public class VideoSurface : View
{
}

/// <summary>
/// Shared half of the handler. Each platform's half declares the base class, which is what binds
/// <see cref="VideoSurface" /> to that platform's native view type — see
/// Platforms/Android/VideoSurfaceHandler.cs and Platforms/iOS/VideoSurfaceHandler.cs.
/// </summary>
public partial class VideoSurfaceHandler
{
    public static readonly IPropertyMapper<VideoSurface, VideoSurfaceHandler> SurfaceMapper =
        new PropertyMapper<VideoSurface, VideoSurfaceHandler>(ViewHandler.ViewMapper);

    public VideoSurfaceHandler() : base(SurfaceMapper)
    {
    }
}
