namespace AntMedia.Net;

/// <summary>
/// The half compiled for a target framework with no platform — plain net8.0, net9.0 or net10.0.
/// Nothing streams here, and nothing can: Ant Media publishes WebRTC SDKs for Android and iOS
/// only, so there is no Windows or desktop native library to bind.
///
/// It exists so that referencing this package does not break a build that merely *mentions*
/// another platform. `dotnet new maui` on a Windows machine adds a Windows target framework, and
/// without an asset it can resolve, restore fails for the entire project — including the Android
/// leg the developer was actually building. With this, the app restores, compiles and runs, and
/// only a call that would need the SDK throws.
/// </summary>
public sealed partial class AntMediaClient
{
    private const string NotSupported =
        "AntMedia.Net cannot stream on this platform: Ant Media publishes WebRTC SDKs for " +
        "Android and iOS only, so there is no native library to bind here. This build resolved " +
        "the fallback assembly, which exists so an app that also targets Windows still restores " +
        "and builds. Guard streaming code with #if ANDROID || IOS || MACCATALYST.";

    /// <summary>
    /// Creates a client. Constructing one is deliberately allowed: a page can be built and shown
    /// on an unsupported platform, and only <see cref="PublishAsync" />, <see cref="PlayAsync" />
    /// and <see cref="JoinAsync" /> report that there is nothing to stream through.
    /// </summary>
    /// <param name="options">Connection settings, validated the same way as anywhere else.</param>
    public AntMediaClient(AntMediaOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    private static void PublishCore(string streamId, string mainTrackId) =>
        throw new PlatformNotSupportedException(NotSupported);

    private static void PlayCore(string streamId) =>
        throw new PlatformNotSupportedException(NotSupported);

    private static void JoinCore(string streamId) =>
        throw new PlatformNotSupportedException(NotSupported);

    // Teardown stays silent rather than throwing. Stop() is called from Dispose(), and from
    // RunAsync's failure path the moment PublishCore throws - a throwing StopCore there would
    // replace the explanation above with a second, more confusing exception.
    private static void StopCore()
    {
    }

    private static void DisposeCore()
    {
    }

    /// <inheritdoc />
    public void SwitchCamera()
    {
    }

    /// <inheritdoc />
    public void SetAudioEnabled(bool enabled)
    {
    }

    /// <inheritdoc />
    public void SetVideoEnabled(bool enabled)
    {
    }

    /// <inheritdoc />
    public void SendData(byte[] data, bool binary = true)
    {
    }
}
