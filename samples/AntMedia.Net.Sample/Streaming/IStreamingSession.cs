namespace AntMedia.Net.Sample.Streaming;

/// <summary>
/// The bit of the sample worth reading: one interface over two quite different SDKs.
///
/// The Android and iOS bindings do not look alike — Android has a builder plus a listener
/// interface, iOS has our @objc facade plus a delegate — so the sample keeps a thin
/// platform-specific implementation of this interface behind a shared UI.
/// </summary>
public interface IStreamingSession : IDisposable
{
    /// <summary>Raised on the UI thread with a human-readable progress message.</summary>
    event EventHandler<string>? Status;

    /// <summary>True once a publish or play session is running.</summary>
    bool IsStreaming { get; }

    /// <summary>Publishes the device's camera and microphone as <paramref name="streamId" />.</summary>
    void Publish(string serverUrl, string streamId);

    /// <summary>Plays <paramref name="streamId" /> from the server.</summary>
    void Play(string serverUrl, string streamId);

    void Stop();
}
