namespace AntMedia.Net;

/// <summary>
/// Settings applied to a client before it connects. Everything here has a usable default except
/// <see cref="ServerUrl" />.
/// </summary>
public sealed class AntMediaOptions
{
    /// <summary>
    /// The server's websocket endpoint, for example
    /// <c>wss://example.com:5443/WebRTCAppEE/websocket</c>. Required.
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// One-time token, when the server has token control enabled. Empty otherwise.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Capture and send video. Disable for audio-only streaming.</summary>
    public bool VideoEnabled { get; set; } = true;

    /// <summary>Capture and send audio.</summary>
    public bool AudioEnabled { get; set; } = true;

    /// <summary>
    /// Open a WebRTC data channel. Must also be enabled server-side, or
    /// <see cref="IAntMediaClient.SendData(byte[], bool)" /> will not deliver anything.
    /// </summary>
    public bool DataChannelEnabled { get; set; }

    /// <summary>Preferred capture width. The camera may pick the closest size it supports.</summary>
    public int VideoWidth { get; set; } = 640;

    /// <summary>Preferred capture height.</summary>
    public int VideoHeight { get; set; } = 480;

    /// <summary>Preferred capture frame rate.</summary>
    public int VideoFps { get; set; } = 30;

    /// <summary>Start on the front (selfie) camera rather than the rear one.</summary>
    public bool UseFrontCamera { get; set; } = true;

    /// <summary>
    /// How long <see cref="IAntMediaClient.PublishAsync" /> and
    /// <see cref="IAntMediaClient.PlayAsync" /> wait for the server to confirm before giving up.
    /// The SDKs report failures through callbacks and some conditions - an unreachable host, a
    /// stream id that does not exist - produce no callback at all, so without a timeout those
    /// calls could wait forever.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            throw new InvalidOperationException(
                $"{nameof(AntMediaOptions)}.{nameof(ServerUrl)} is required, e.g. " +
                "wss://example.com:5443/WebRTCAppEE/websocket");
        }
    }
}
