using System.Text;
using AntMedia.Net.Sample.Streaming;

namespace AntMedia.Net.Sample;

public partial class MainPage : ContentPage
{
    private readonly StringBuilder _status = new();
    private IStreamingSession? _session;

    public MainPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The session is created here rather than in the constructor because it needs the native
    /// views behind the two VideoSurfaces, and those do not exist until the page has a handler.
    /// </summary>
    private IStreamingSession Session()
    {
        if (_session is not null)
        {
            return _session;
        }

#if ANDROID
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("no current activity");
        _session = new StreamingSession(activity, LocalSurface, RemoteSurface);
#elif IOS
        _session = new StreamingSession(LocalSurface, RemoteSurface);
#else
        throw new PlatformNotSupportedException(
            "AntMedia.Net supports Android and iOS; see docs/BUILD.md for why not Mac Catalyst.");
#endif

        _session.Status += (_, message) => Append(message);
        return _session;
    }

    private async void OnPublishClicked(object sender, EventArgs e) =>
        await StartAsync(publish: true);

    private async void OnPlayClicked(object sender, EventArgs e) =>
        await StartAsync(publish: false);

    private async Task StartAsync(bool publish)
    {
        var serverUrl = ServerUrlEntry.Text?.Trim();
        var streamId = StreamIdEntry.Text?.Trim();

        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(streamId))
        {
            Append("enter a server url and a stream id first");
            return;
        }

        // Publishing needs both; playing needs neither, but asking once keeps the sample simple.
        if (publish && !await RequestCapturePermissionsAsync())
        {
            Append("camera and microphone permission denied");
            return;
        }

        try
        {
            var session = Session();

            if (publish)
            {
                session.Publish(serverUrl, streamId);
            }
            else
            {
                session.Play(serverUrl, streamId);
            }

            SetStreaming(true);
        }
        catch (Exception exception)
        {
            Append($"failed to start: {exception.Message}");
        }
    }

    private void OnStopClicked(object sender, EventArgs e)
    {
        _session?.Stop();
        SetStreaming(false);
    }

    private static async Task<bool> RequestCapturePermissionsAsync()
    {
        var camera = await Permissions.RequestAsync<Permissions.Camera>();
        var microphone = await Permissions.RequestAsync<Permissions.Microphone>();

        return camera == PermissionStatus.Granted && microphone == PermissionStatus.Granted;
    }

    private void SetStreaming(bool streaming)
    {
        PublishButton.IsEnabled = !streaming;
        PlayButton.IsEnabled = !streaming;
        StopButton.IsEnabled = streaming;
    }

    private void Append(string message)
    {
        _status.AppendLine($"{DateTime.Now:HH:mm:ss}  {message}");
        StatusLabel.Text = _status.ToString();
        StatusScroll.ScrollToAsync(0, StatusLabel.Height, animated: false);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // Releases the native renderers when the page goes away; both SDKs hold an EGL/GL context
        // that is not reclaimed by managed collection alone.
        if (Handler is null)
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
