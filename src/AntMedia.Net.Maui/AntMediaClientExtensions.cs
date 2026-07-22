namespace AntMedia.Net.Maui;

/// <summary>
/// Creates clients and attaches views without the caller writing platform code.
///
/// This is the whole reason the MAUI package exists. On Android the SDK needs an
/// <c>Activity</c>, and the native video view types differ per platform, so a MAUI app would
/// otherwise need a <c>#if ANDROID</c> block for construction and another for rendering.
/// </summary>
public static class AntMediaClientExtensions
{
    /// <summary>
    /// Creates a client for the current platform.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Android only, and only when called before the activity exists — from a page constructor,
    /// for example. Create the client once the page has appeared.
    /// </exception>
    public static IAntMediaClient CreateClient(this AntMediaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

#if ANDROID
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException(
                "no current activity. The Ant Media SDK needs one for the camera and the video " +
                "renderers, so create the client after the page has appeared rather than in its " +
                "constructor.");

        return new AntMediaClient(options, activity);
#else
        return new AntMediaClient(options);
#endif
    }

    /// <summary>
    /// Renders the local camera preview into <paramref name="view" />. Call before publishing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The view has no handler yet, which means it is not on screen. Attach it after the page has
    /// appeared, not from its constructor.
    /// </exception>
    public static void SetLocalView(this IAntMediaClient client, AntMediaVideoView view)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(view);

        var native = PlatformView(view);

#if ANDROID
        ((AntMediaClient)client).SetLocalRenderer(native);
#elif IOS || MACCATALYST
        ((AntMediaClient)client).SetLocalView(native);
#endif
    }

    /// <summary>Renders the stream being played into <paramref name="view" />.</summary>
    /// <inheritdoc cref="SetLocalView" />
    public static void SetRemoteView(this IAntMediaClient client, AntMediaVideoView view)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(view);

        var native = PlatformView(view);

#if ANDROID
        ((AntMediaClient)client).SetRemoteRenderer(native);
#elif IOS || MACCATALYST
        ((AntMediaClient)client).SetRemoteView(native);
#endif
    }

#if ANDROID
    private static Org.Webrtc.SurfaceViewRenderer PlatformView(AntMediaVideoView view) =>
        view.Handler?.PlatformView as Org.Webrtc.SurfaceViewRenderer ?? throw NotRealised();
#elif IOS || MACCATALYST
    // One branch for both: UIKit is what Mac Catalyst renders with, so the facade's
    // SetLocalView/SetRemoteView take the same UIView there as on iOS.
    private static UIKit.UIView PlatformView(AntMediaVideoView view) =>
        view.Handler?.PlatformView as UIKit.UIView ?? throw NotRealised();
#else
    private static object PlatformView(AntMediaVideoView view) => throw NotRealised();
#endif

    private static InvalidOperationException NotRealised() =>
        new("the AntMediaVideoView has no handler yet, so its native view does not exist. " +
            "Attach it once the page has appeared rather than from its constructor.");
}
