using ObjCRuntime;

#if MACCATALYST
// Same source, two packages: AntMedia.Net.iOS binds Ant Media's own build, AntMedia.Net.Mac binds
// the Mac Catalyst build produced by native/mac/fetch-mac.sh. The facade they expose is identical,
// so the ApiDefinition is linked into both projects and only the namespace differs.
namespace AntMedia.Net.Mac;
#else
namespace AntMedia.Net.iOS;
#endif

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
