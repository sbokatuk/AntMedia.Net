using System.Text;
using AntMedia.Net.Maui;

namespace AntMedia.Net.Sample;

/// <summary>
/// Publishes and plays a stream. The whole app is this one file — there is no per-platform code,
/// because <c>AntMedia.Net</c> presents the same client on Android, iOS and Mac Catalyst and
/// <c>AntMedia.Net.Maui</c> supplies the video view and the Android <c>Activity</c>.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly StringBuilder _status = new();
    private IAntMediaClient? _client;

    public MainPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Built lazily rather than in the constructor: on Android the SDK needs the current activity,
    /// and the video views need their handlers, neither of which exists until the page is on screen.
    /// </summary>
    private IAntMediaClient Client()
    {
        if (_client is not null)
        {
            return _client;
        }

        var client = new AntMediaOptions
        {
            ServerUrl = ServerUrlEntry.Text?.Trim() ?? string.Empty,
            VideoWidth = 640,
            VideoHeight = 480,
        }.CreateClient();

        client.SetLocalView(LocalView);
        client.SetRemoteView(RemoteView);

        // The SDKs raise callbacks on their own threads, so anything touching the UI hops back.
        client.PublishStarted += (_, e) => Append($"publish started: {e.StreamId}");
        client.PublishFinished += (_, e) => Append($"publish finished: {e.StreamId}");
        client.PlayStarted += (_, e) => Append($"play started: {e.StreamId}");
        client.PlayFinished += (_, e) => Append($"play finished: {e.StreamId}");
        client.Disconnected += (_, e) => Append($"disconnected: {e.StreamId}");
        client.Error += (_, e) => Append($"error: {e.Message}");
        client.TrackAdded += (_, e) => Append($"track added: {e.Kind} {e.TrackId}");

        return _client = client;
    }

    private async void OnPublishClicked(object sender, EventArgs e) =>
        await StartAsync(publish: true);

    private async void OnPlayClicked(object sender, EventArgs e) =>
        await StartAsync(publish: false);

    private async Task StartAsync(bool publish)
    {
        var streamId = StreamIdEntry.Text?.Trim();

        if (string.IsNullOrEmpty(ServerUrlEntry.Text?.Trim()) || string.IsNullOrEmpty(streamId))
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

        SetBusy(true);

        try
        {
            var client = Client();

            // Completes when the server confirms, so there is no callback to wire up for the
            // common case, and a failure to start surfaces as an exception right here.
            if (publish)
            {
                await client.PublishAsync(streamId);
            }
            else
            {
                await client.PlayAsync(streamId);
            }

            Append(publish ? $"publishing {streamId}" : $"playing {streamId}");
            SetStreaming(true);
        }
        catch (AntMediaException exception)
        {
            Append($"failed to start: {exception.Message}");
            SetStreaming(false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnStopClicked(object sender, EventArgs e)
    {
        _client?.Stop();
        Append("stopped");
        SetStreaming(false);
    }

    private static async Task<bool> RequestCapturePermissionsAsync()
    {
        var camera = await Permissions.RequestAsync<Permissions.Camera>();
        var microphone = await Permissions.RequestAsync<Permissions.Microphone>();

        return camera == PermissionStatus.Granted && microphone == PermissionStatus.Granted;
    }

    private void SetBusy(bool busy)
    {
        PublishButton.IsEnabled = !busy;
        PlayButton.IsEnabled = !busy;
    }

    private void SetStreaming(bool streaming)
    {
        PublishButton.IsEnabled = !streaming;
        PlayButton.IsEnabled = !streaming;
        StopButton.IsEnabled = streaming;
    }

    private void Append(string message) => MainThread.BeginInvokeOnMainThread(() =>
    {
        _status.AppendLine($"{DateTime.Now:HH:mm:ss}  {message}");
        StatusLabel.Text = _status.ToString();
        StatusScroll.ScrollToAsync(0, StatusLabel.Height, animated: false);
    });

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // Releases the camera and the native renderers when the page goes away; both SDKs hold a
        // GL context that managed collection alone does not reclaim.
        if (Handler is null)
        {
            _client?.Dispose();
            _client = null;
        }
    }
}
