using Microsoft.Maui.Handlers;
using UIKit;

namespace AntMedia.Net.Maui;

/// <summary>
/// iOS half: the facade's SetLocalView/SetRemoteView take a plain UIView and add their own
/// renderer inside it, so there is nothing SDK-specific to create here.
/// </summary>
public partial class AntMediaVideoViewHandler : ViewHandler<AntMediaVideoView, UIView>
{
    /// <inheritdoc />
    protected override UIView CreatePlatformView() => new() { BackgroundColor = UIColor.Black };
}
