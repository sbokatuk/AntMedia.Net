using Microsoft.Maui.Handlers;
using Org.Webrtc;

namespace AntMedia.Net.Sample.Streaming;

/// <summary>
/// Android half of the handler: the native view is org.webrtc's SurfaceViewRenderer, which is
/// what WebRTCClientBuilder takes for both the local preview and the remote stream.
/// </summary>
public partial class VideoSurfaceHandler : ViewHandler<VideoSurface, SurfaceViewRenderer>
{
    protected override SurfaceViewRenderer CreatePlatformView() => new(Context);

    protected override void DisconnectHandler(SurfaceViewRenderer platformView)
    {
        // The renderer holds an EGL context and a native surface; leaving it initialised leaks
        // both and, after a few navigations, exhausts the surface pool.
        platformView.Release();
        base.DisconnectHandler(platformView);
    }
}
