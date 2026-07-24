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
        // Fails now, when the mistake is a page-constructor call — but hands the client a
        // provider rather than this instance, so a client that lives across a configuration
        // change builds its next session against the then-current activity instead of a
        // destroyed one.
        _ = Platform.CurrentActivity ?? throw NoActivity();

        return new AntMediaClient(options, static () => Platform.CurrentActivity ?? throw NoActivity());
#else
        return new AntMediaClient(options);
#endif
    }

#if ANDROID
    private static InvalidOperationException NoActivity() =>
        new("no current activity. The Ant Media SDK needs one for the camera and the video " +
            "renderers, so create the client after the page has appeared rather than in its " +
            "constructor.");
#endif

    /// <summary>
    /// Renders the local camera preview into <paramref name="view" />. When the view is not on
    /// screen yet the attachment is deferred until its native view exists — but it must be on
    /// screen before <see cref="IAntMediaClient.PublishAsync" /> runs, because the SDK takes the
    /// renderer at session start.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The client did not come from <see cref="CreateClient" /> — a custom
    /// <see cref="IAntMediaClient" /> has no platform view plumbing to attach to.
    /// </exception>
    public static void SetLocalView(this IAntMediaClient client, AntMediaVideoView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var implementation = Implementation(client);

        WhenRealised(view, () =>
        {
#if ANDROID
            implementation.SetLocalRenderer(PlatformView(view));
#elif IOS || MACCATALYST
            implementation.SetLocalView(PlatformView(view), view.Scaling ?? AntMediaVideoScaling.Fill);
#endif
        });
    }

    /// <summary>
    /// Renders the stream being played into <paramref name="view" />, deferring as
    /// <see cref="SetLocalView" /> does.
    /// </summary>
    /// <inheritdoc cref="SetLocalView" />
    public static void SetRemoteView(this IAntMediaClient client, AntMediaVideoView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var implementation = Implementation(client);

        WhenRealised(view, () =>
        {
#if ANDROID
            implementation.SetRemoteRenderer(PlatformView(view));
#elif IOS || MACCATALYST
            implementation.SetRemoteView(PlatformView(view), view.Scaling ?? AntMediaVideoScaling.Fit);
#endif
        });
    }

    /// <summary>
    /// Runs <paramref name="attach" /> now when the view's native counterpart exists, or once
    /// the handler is created when it does not — which is what makes calling
    /// SetLocalView/SetRemoteView from a page constructor work.
    /// </summary>
    private static void WhenRealised(AntMediaVideoView view, Action attach)
    {
        if (view.Handler is not null)
        {
            attach();
            return;
        }

        view.HandlerChanged += OnHandlerChanged;

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            // HandlerChanged also fires when the handler is cleared; only creation matters here.
            if (view.Handler is null)
            {
                return;
            }

            view.HandlerChanged -= OnHandlerChanged;
            attach();
        }
    }

    private static AntMediaClient Implementation(IAntMediaClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client as AntMediaClient ?? throw new ArgumentException(
            $"the client is a {client.GetType().Name}, not an AntMediaClient, so it has no " +
            "platform view plumbing. SetLocalView and SetRemoteView work with clients created " +
            "by AntMediaOptions.CreateClient().",
            nameof(client));
    }

#if ANDROID
    private static Org.Webrtc.SurfaceViewRenderer PlatformView(AntMediaVideoView view) =>
        view.Handler?.PlatformView as Org.Webrtc.SurfaceViewRenderer ?? throw NotRealised();
#elif IOS || MACCATALYST
    // One branch for both: UIKit is what Mac Catalyst renders with, so the facade's
    // SetLocalView/SetRemoteView take the same UIView there as on iOS.
    private static UIKit.UIView PlatformView(AntMediaVideoView view) =>
        view.Handler?.PlatformView as UIKit.UIView ?? throw NotRealised();
#endif

    private static InvalidOperationException NotRealised() =>
        new("the AntMediaVideoView has no handler yet, so its native view does not exist. " +
            "Attach it once the page has appeared rather than from its constructor.");
}
