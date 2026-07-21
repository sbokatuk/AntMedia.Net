using ObjCRuntime;

namespace AntMedia.Net.iOS;

/// <summary>
/// The role a client takes for a session. Mirrors the SDK's <c>AntMediaClientMode</c>.
/// </summary>
[Native]
public enum AMSMode : long
{
    /// <summary>Peer-to-peer call.</summary>
    Join = 1,

    /// <summary>Play a stream published to the server.</summary>
    Play = 2,

    /// <summary>Publish a stream to the server.</summary>
    Publish = 3,

    /// <summary>Multi-track conference room.</summary>
    Conference = 4,

    Unspecified = 5,
}
