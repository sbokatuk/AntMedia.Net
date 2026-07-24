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
    private string? _clientUrl;

    public MainPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Built lazily rather than in the constructor: on Android the SDK needs the current activity,
    /// and the video views need their handlers, neither of which exists until the page is on screen.
    /// Rebuilt when the server url changes, so correcting a typo'd url takes effect instead of the
    /// old client being reused.
    /// </summary>
    private IAntMediaClient Client()
    {
        var serverUrl = ServerUrlEntry.Text?.Trim() ?? string.Empty;

        if (_client is not null && serverUrl == _clientUrl)
        {
            return _client;
        }

        _client?.Dispose();

        var client = new AntMediaOptions
        {
            ServerUrl = serverUrl,
            VideoWidth = 640,
            VideoHeight = 480,
        }.CreateClient();

        client.SetLocalView(LocalView);
        client.SetRemoteView(RemoteView);

        // The SDKs raise callbacks on their own threads, so anything touching the UI hops back.
        client.PublishStarted += (_, e) => Append($"publish started: {e.StreamId}");
        client.PlayStarted += (_, e) => Append($"play started: {e.StreamId}");
        client.Error += (_, e) => Append($"error: {e.Message}");
        client.TrackAdded += (_, e) => Append($"track added: {e.Kind} {e.TrackId}");

        // A session the *server* ends — a drop, or the far side going away — re-enables the
        // buttons, not only the local Stop button.
        client.PublishFinished += (_, e) => OnSessionEnded($"publish finished: {e.StreamId}");
        client.PlayFinished += (_, e) => OnSessionEnded($"play finished: {e.StreamId}");
        client.Disconnected += (_, e) => OnSessionEnded($"disconnected: {e.StreamId}");

        _clientUrl = serverUrl;
        return _client = client;
    }

    private void OnSessionEnded(string message)
    {
        Append(message);
        MainThread.BeginInvokeOnMainThread(() => SetStreaming(false));
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
            // Code is the stable way to react ("stream taken, pick another id"); Message is for
            // the log.
            Append($"failed to start ({exception.Code}): {exception.Message}");
            SetStreaming(false);
        }
        catch (OperationCanceledException)
        {
            // Stop() was pressed while the start was still awaiting confirmation.
            Append("start cancelled");
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
            _clientUrl = null;
        }
    }
}
