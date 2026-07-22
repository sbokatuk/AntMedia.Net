namespace AntMedia.Net;

/// <summary>
/// Publishes to and plays from an Ant Media Server, with the same surface on Android and iOS.
///
/// The platform bindings underneath do not resemble each other — Android has a builder and a
/// 40-method listener interface, iOS has an @objc facade and a delegate — and this is the layer
/// that hides the difference. Reach for <c>AntMedia.WebRTC.*</c> (Android) or
/// <c>AntMedia.Net.iOS.*</c> (iOS) directly when you need something not exposed here.
/// </summary>
public interface IAntMediaClient : IDisposable
{
    /// <summary>The server accepted the publish and is receiving media.</summary>
    event EventHandler<AntMediaStreamEventArgs>? PublishStarted;

    /// <summary>Publishing ended, whether by <see cref="Stop" /> or by the server.</summary>
    event EventHandler<AntMediaStreamEventArgs>? PublishFinished;

    /// <summary>The server started sending the stream being played.</summary>
    event EventHandler<AntMediaStreamEventArgs>? PlayStarted;

    /// <summary>Playback ended, whether by <see cref="Stop" /> or by the server.</summary>
    event EventHandler<AntMediaStreamEventArgs>? PlayFinished;

    /// <summary>The peer connection dropped. Any active session is over.</summary>
    event EventHandler<AntMediaStreamEventArgs>? Disconnected;

    /// <summary>
    /// The SDK reported a problem. When it happens while starting, the pending
    /// <see cref="PublishAsync" /> or <see cref="PlayAsync" /> fails with the same message.
    /// </summary>
    event EventHandler<AntMediaErrorEventArgs>? Error;

    /// <summary>A remote track became available — the point at which video can be rendered.</summary>
    event EventHandler<AntMediaTrackEventArgs>? TrackAdded;

    /// <summary>A remote track went away.</summary>
    event EventHandler<AntMediaTrackEventArgs>? TrackRemoved;

    /// <summary>Requires <see cref="AntMediaOptions.DataChannelEnabled" /> on both ends.</summary>
    event EventHandler<AntMediaDataEventArgs>? DataReceived;

    /// <summary>True between a successful publish or play and <see cref="Stop" />.</summary>
    bool IsStreaming { get; }

    /// <summary>The stream id of the running session, or null when idle.</summary>
    string? StreamId { get; }

    /// <summary>
    /// Publishes this device's camera and microphone, completing when the server confirms.
    /// </summary>
    /// <param name="streamId">The id to publish under.</param>
    /// <param name="mainTrackId">
    /// The conference room to publish into, or empty for a standalone stream.
    /// </param>
    /// <param name="cancellationToken">
    /// Abandons the wait. The session is stopped, so a cancelled call leaves nothing running.
    /// </param>
    /// <exception cref="AntMediaException">
    /// The server reported an error, or did not confirm within
    /// <see cref="AntMediaOptions.Timeout" />.
    /// </exception>
    Task PublishAsync(string streamId, string mainTrackId = "", CancellationToken cancellationToken = default);

    /// <summary>Plays a stream, completing when the server starts sending it.</summary>
    /// <inheritdoc cref="PublishAsync" />
    Task PlayAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session and releases the camera, microphone and peer connection. Safe to call
    /// when nothing is running.
    /// </summary>
    void Stop();

    /// <summary>Switches between the front and rear cameras mid-session.</summary>
    void SwitchCamera();

    /// <summary>
    /// Mutes or unmutes the outgoing audio track. This does not release the microphone — the OS
    /// will still show the app as recording.
    /// </summary>
    void SetAudioEnabled(bool enabled);

    /// <summary>Pauses or resumes the outgoing video track.</summary>
    void SetVideoEnabled(bool enabled);

    /// <summary>
    /// Sends a message over the data channel. Requires
    /// <see cref="AntMediaOptions.DataChannelEnabled" /> and server-side support.
    /// </summary>
    void SendData(byte[] data, bool binary = false);

    /// <summary>Sends <paramref name="text" /> as UTF-8 over the data channel.</summary>
    void SendData(string text);
}

/// <summary>Raised when a session fails to start or the server reports an error.</summary>
public sealed class AntMediaException(string message) : Exception(message);
