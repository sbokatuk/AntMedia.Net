namespace AntMedia.Net;

/// <summary>
/// Settings applied to a client before it connects. Everything here has a usable default except
/// <see cref="ServerUrl" />.
/// </summary>
/// <remarks>
/// The client keeps the instance you pass it and reads it again at the start of every publish or
/// play — so a value changed between sessions applies to the next one, and a value changed while
/// a session is running is picked up only after the next start. Validation happens at the same
/// point, not in the setters.
/// </remarks>
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
    /// Video bitrate in kbit/s, or 0 for the SDK's default. The two SDKs apply it differently —
    /// Android as the encoder's starting bitrate, Apple as a ceiling — but in both cases it is
    /// the knob that bounds video quality against bandwidth.
    /// </summary>
    public int VideoBitrateKbps { get; set; }

    /// <summary>
    /// Audio bitrate in kbit/s, or 0 for the SDK's default. Android only: the Apple SDK has no
    /// audio bitrate control, so the value is ignored there.
    /// </summary>
    public int AudioBitrateKbps { get; set; }

    /// <summary>
    /// Reconnect automatically after a network drop. On Apple platforms the SDK always
    /// reconnects and cannot be told not to, so false only takes effect on Android.
    /// </summary>
    public bool ReconnectionEnabled { get; set; } = true;

    /// <summary>
    /// How long <see cref="IAntMediaClient.PublishAsync" /> and
    /// <see cref="IAntMediaClient.PlayAsync" /> wait for the server to confirm before giving up.
    /// The SDKs report failures through callbacks and some conditions — an unreachable host, for
    /// example — produce no callback at all, so without a timeout those calls could wait forever.
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan" /> disables it.
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

        if (VideoWidth <= 0 || VideoHeight <= 0 || VideoFps <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(VideoWidth)}, {nameof(VideoHeight)} and {nameof(VideoFps)} must be " +
                $"positive (got {VideoWidth}x{VideoHeight}@{VideoFps}).");
        }

        if (VideoBitrateKbps < 0 || AudioBitrateKbps < 0)
        {
            throw new InvalidOperationException(
                $"{nameof(VideoBitrateKbps)} and {nameof(AudioBitrateKbps)} cannot be negative; " +
                "use 0 for the SDK default.");
        }

        if (Timeout <= TimeSpan.Zero && Timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new InvalidOperationException(
                $"{nameof(Timeout)} must be positive, or Timeout.InfiniteTimeSpan to disable it " +
                $"(got {Timeout}).");
        }
    }
}
