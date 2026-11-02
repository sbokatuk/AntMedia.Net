using Android.App;
using Android.OS;
using DE.Tavendo.Autobahn;
using Java.Net;
using Java.Util.Concurrent;

using Observer = DE.Tavendo.Autobahn.IWebSocket.IWebSocketConnectionObserver;

namespace AntMedia.Net.Android.DeviceTests;

/// <summary>
/// Checks that the websocket connect stays off the main thread and survives the SDK's lost
/// wake-up; see <see cref="BackgroundWebSocketConnection" />, which is compiled into this app from
/// the same source because the shipped assembly has no way to inject a stall.
///
/// These run on the main thread, like the app's real calls, so a connect that blocked it would
/// stop the app reaching its verdict at all. The runner treats that as a failure.
/// </summary>
public static class WebSocketConnectTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    public static IReadOnlyList<(string Name, Func<Activity, Task<string>> Run)> All =>
    [
        ("connect recovers from a stall in createReader", _ => RecoversFromStallAsync(StallSite.Reader)),
        ("connect recovers from a stall in createWriter", _ => RecoversFromStallAsync(StallSite.Writer)),
        ("a refused connection fails the publish", RefusedConnectionFailsPublishAsync),
        ("publish completes over a background connect", PublishCompletesAsync),
    ];

    /// <summary>
    /// A real stall cannot be arranged from in here, because the race sits between two bytecodes
    /// of the SDK's connect(). So the stall is added straight after the real createReader() or
    /// createWriter() returns: same thread, same point in the connect, and a wait nothing will
    /// ever end but an interrupt. docs/BUILD.md describes forcing the real one with a debugger.
    /// </summary>
    private static async Task<string> RecoversFromStallAsync(StallSite site)
    {
        using var server = new FakeSignallingServer();

        // Left for the collector: Java can still call into these after the test is over, and that
        // takes the process down if the managed side has been disposed.
        var connection = new StallingConnection(site);
        var observer = new ConnectionObserver();

        connection.Connect(new URI(server.Url), observer);

        await connection.Stalled.Task.WaitAsync(Limit);
        await observer.Opened.Task.WaitAsync(Limit);

        if (!observer.OpenedOnMainThread)
        {
            throw new InvalidOperationException("onOpen was not delivered on the main thread.");
        }

        connection.Disconnect();
        return $"stalled in {site}, then opened on the main thread";
    }

    /// <summary>
    /// A failed connect is the one callback that starts on the connector thread, so this is what
    /// checks that it finds its way back and still fails the operation.
    /// </summary>
    private static async Task<string> RefusedConnectionFailsPublishAsync(Activity activity)
    {
        string url;

        using (var closed = new FakeSignallingServer())
        {
            url = closed.Url;
        }

        using var client = new AntMediaClient(Options(url, reconnection: false), activity);

        try
        {
            await client.PublishAsync("refused").WaitAsync(Limit);
        }
        catch (AntMediaException exception) when (exception.Code == AntMediaErrorCode.Disconnected)
        {
            return exception.Message;
        }

        throw new InvalidOperationException("the publish succeeded with nothing listening.");
    }

    /// <summary>
    /// The whole packaged path through the public API, default reconnection included, which is
    /// the one thing here that the trimmed run proves about the shipped assembly.
    /// </summary>
    private static async Task<string> PublishCompletesAsync(Activity activity)
    {
        const string streamId = "fakesignalling";

        using var server = new FakeSignallingServer { StreamId = streamId };
        using var client = new AntMediaClient(Options(server.Url, reconnection: true), activity);

        await client.PublishAsync(streamId).WaitAsync(Limit);

        if (!client.IsStreaming)
        {
            throw new InvalidOperationException(
                "PublishAsync returned but the client does not consider itself streaming.");
        }

        client.Stop();
        return $"published '{streamId}' to {server.Url}";
    }

    // Audio and video off: signalling is the point here, and the offline CI emulator has neither.
    private static AntMediaOptions Options(string url, bool reconnection) => new()
    {
        ServerUrl = url,
        VideoEnabled = false,
        AudioEnabled = false,
        ReconnectionEnabled = reconnection,
        Timeout = Limit,
    };

    private enum StallSite
    {
        Reader,
        Writer,
    }

    private sealed class StallingConnection(StallSite site) : BackgroundWebSocketConnection
    {
        public TaskCompletionSource Stalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void CreateReader()
        {
            base.CreateReader();

            if (site == StallSite.Reader)
            {
                Stall();
            }
        }

        protected override void CreateWriter()
        {
            base.CreateWriter();

            if (site == StallSite.Writer)
            {
                Stall();
            }
        }

        private void Stall()
        {
            Stalled.TrySetResult();

            try
            {
                new CountDownLatch(1).Await();
            }
            catch (Java.Lang.InterruptedException)
            {
                // Swallowed and carried on from, as the SDK's own waits do.
            }
        }
    }

    private sealed class ConnectionObserver : Java.Lang.Object, Observer
    {
        public TaskCompletionSource Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool OpenedOnMainThread { get; private set; }

        public void OnOpen()
        {
            OpenedOnMainThread = Looper.MyLooper() == Looper.MainLooper;
            Opened.TrySetResult();
        }

        public void OnClose(Observer.WebSocketCloseNotification? notification, string? reason) =>
            Opened.TrySetException(new InvalidOperationException($"closed: {notification} {reason}"));

        public void OnTextMessage(string? payload)
        {
        }

        public void OnRawTextMessage(byte[]? payload)
        {
        }

        public void OnBinaryMessage(byte[]? payload)
        {
        }
    }
}
