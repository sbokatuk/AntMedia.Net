using Microsoft.Maui.Handlers;
using UIKit;

namespace AntMedia.Net.Sample.Streaming;

/// <summary>
/// iOS half of the handler: the facade's SetLocalView/SetRemoteView take a plain UIView and put
/// their own renderer inside it, so there is nothing SDK-specific to create here.
/// </summary>
public partial class VideoSurfaceHandler : ViewHandler<VideoSurface, UIView>
{
    protected override UIView CreatePlatformView() => new() { BackgroundColor = UIColor.Black };
}
