using Microsoft.Maui.Handlers;
using Org.Webrtc;

namespace AntMedia.Net.Maui;

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
