namespace AntMedia.Net;

/// <summary>Identifies which stream an event refers to.</summary>
public class AntMediaStreamEventArgs(string streamId) : EventArgs
{
    /// <summary>
    /// The stream the event concerns. May differ from the id you asked for: when joining a room
    /// the server can assign a different one.
    /// </summary>
    public string StreamId { get; } = streamId;
}

/// <summary>Something went wrong. The session may or may not still be usable.</summary>
public sealed class AntMediaErrorEventArgs(string message, string streamId)
    : AntMediaStreamEventArgs(streamId)
{
    /// <summary>The SDK's description. Wording comes from the server and is not stable.</summary>
    public string Message { get; } = message;
}

/// <summary>A media track was added to or removed from the session.</summary>
public sealed class AntMediaTrackEventArgs(string trackId, string kind) : EventArgs
{
    /// <summary>The WebRTC track id, as the server assigned it.</summary>
    public string TrackId { get; } = trackId;

    /// <summary>Either <c>"audio"</c> or <c>"video"</c>.</summary>
    public string Kind { get; } = kind;
}

/// <summary>A message arrived over the data channel.</summary>
public sealed class AntMediaDataEventArgs(string streamId, byte[] data, bool binary)
    : AntMediaStreamEventArgs(streamId)
{
    /// <summary>The raw payload. See <see cref="Text" /> when the peer sent text.</summary>
    public byte[] Data { get; } = data;

    /// <summary>False when the peer sent text, in which case <see cref="Text" /> is meaningful.</summary>
    public bool IsBinary { get; } = binary;

    /// <summary>The payload decoded as UTF-8. Only meaningful when <see cref="IsBinary" /> is false.</summary>
    public string Text => System.Text.Encoding.UTF8.GetString(Data);
}
