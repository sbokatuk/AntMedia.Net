namespace AntMedia.Net;

/// <summary>
/// Mac Catalyst half. Every member throws.
///
/// This exists so a .NET MAUI app can reference the package at all. The MAUI template targets
/// maccatalyst by default, and a package with no assets for it fails the entire restore with
/// NU1202 — so without this, adopting AntMedia.Net starts with editing TargetFrameworks, and the
/// error says nothing about why.
///
/// It cannot stream, and the reason is upstream rather than here. Ant Media's iOS SDK links a
/// customised build of libwebrtc — it exports RTCAudioDeviceModule, which stock libwebrtc does
/// not — and they publish it for iOS only, with no Catalyst slice and no source for the fork.
/// Substituting a Catalyst-capable community build does not compile against their Swift code.
/// See the Catalyst section in docs/BUILD.md.
/// </summary>
public sealed partial class AntMediaClient
{
    private const string NotSupported =
        "AntMedia.Net does not support Mac Catalyst. Ant Media publishes its iOS WebRTC framework " +
        "for iOS only — there is no Mac Catalyst slice — so there is nothing to bind against on " +
        "this platform. Android and iOS are supported. This package targets Mac Catalyst purely " +
        "so that a multi-targeted MAUI app can reference it without the restore failing.";

    /// <summary>Always throws: see the type's remarks.</summary>
    /// <exception cref="PlatformNotSupportedException">Always.</exception>
    public AntMediaClient(AntMediaOptions options)
    {
        // Assigned before throwing so the shared half's readonly field is definitely assigned;
        // otherwise this leg builds with CS0649 and nothing is gained by leaving it unset.
        _options = options;

        throw new PlatformNotSupportedException(NotSupported);
    }

    /// <summary>
    /// Present so AntMedia.Net.Maui's view helpers compile for Catalyst, where UIKit exists and
    /// they resolve the same UIView they do on iOS. Unreachable: no client can be constructed.
    /// </summary>
    public void SetLocalView(UIKit.UIView? view) => throw new PlatformNotSupportedException(NotSupported);

    /// <inheritdoc cref="SetLocalView" />
    public void SetRemoteView(UIKit.UIView? view) => throw new PlatformNotSupportedException(NotSupported);

    // Thrown from the constructor, so nothing below is reachable. They exist because the shared
    // half of the partial class calls them, and a platform half has to supply them all.
    private void PublishCore(string streamId, string mainTrackId) => throw new PlatformNotSupportedException(NotSupported);

    private void PlayCore(string streamId) => throw new PlatformNotSupportedException(NotSupported);

    private void StopCore()
    {
        // Deliberately silent rather than throwing: Dispose() calls Stop(), and an object that
        // throws from Dispose turns one clear failure into a confusing second one.
    }

    private void DisposeCore()
    {
    }

    /// <inheritdoc />
    public void SwitchCamera() => throw new PlatformNotSupportedException(NotSupported);

    /// <inheritdoc />
    public void SetAudioEnabled(bool enabled) => throw new PlatformNotSupportedException(NotSupported);

    /// <inheritdoc />
    public void SetVideoEnabled(bool enabled) => throw new PlatformNotSupportedException(NotSupported);

    /// <inheritdoc />
    public void SendData(byte[] data, bool binary = false) => throw new PlatformNotSupportedException(NotSupported);
}
