using Android.App;
using Android.OS;
using AntMedia.WebRTC.Api;
using DE.Tavendo.Autobahn;
using Java.Net;
using Java.Util.Concurrent;
using Org.Webrtc;

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

    /// <summary>Long enough to catch one tick of the SDK's reconnector, which runs every 5s.</summary>
    private static readonly TimeSpan ReconnectorTick = TimeSpan.FromSeconds(6);

    public static IReadOnlyList<(string Name, Func<Activity, Task<string>> Run)> All =>
    [
        ("connect recovers from a stall in createReader", _ => RecoversFromStallAsync(StallSite.Reader)),
        ("connect recovers from a stall in createWriter", _ => RecoversFromStallAsync(StallSite.Writer)),
        ("a server hanging up during connect is survived", _ => SurvivesImmediateHangUpsAsync()),
        ("a connect finishing after the session ended is closed", LateConnectIsClosedAsync),
        ("a refused connection fails the publish", RefusedConnectionFailsPublishAsync),
        ("publish completes over a background connect", PublishCompletesAsync),
        ("a failed publish stops reconnecting", FailedPublishStopsReconnectingAsync),
        ("a rejected publish stops the stream and closes the socket", RejectedPublishClosesSocketAsync),
        ("stop waits for publish_finished, then closes the socket", StopWaitsForFinishedAsync),
        ("stop closes the socket when the server never answers", StopGivesUpOnSilentServerAsync),
        ("a failed publish can be retried on the same renderer", RetryKeepsRendererAsync),
        ("a new publish ends the running one and keeps the renderer", RestartKeepsRendererAsync),
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
    /// A race between the reader failing the connection on the main thread and the writer thread
    /// coming up, so one attempt proves little. Losing it takes the process down, and the verdict
    /// with it. With the reader started first, that took one or two attempts on an emulator.
    /// </summary>
    private static async Task<string> SurvivesImmediateHangUpsAsync()
    {
        const int attempts = 200;

        using var server = new FakeSignallingServer { DropBeforeUpgrade = true };

        for (var i = 0; i < attempts; i++)
        {
            var observer = new ConnectionObserver();

            new BackgroundWebSocketConnection().Connect(new URI(server.Url), observer);
            await observer.Closed.Task.WaitAsync(Limit);
        }

        return $"{attempts} connections dropped mid-connect";
    }

    /// <summary>
    /// A connect can't be cancelled, so it can finish after the session has ended, and nothing
    /// left would ever close that socket. Both calls run before the main thread can hear back from
    /// the connector, so the connect is always the one that comes second.
    /// </summary>
    private static async Task<string> LateConnectIsClosedAsync(Activity activity)
    {
        using var server = new FakeSignallingServer();

        var client = new WebRTCClientBuilder()
            .SetActivity(activity)
            .SetServerUrl(server.Url)
            .SetVideoCallEnabled(false)
            .SetAudioCallEnabled(false)
            .Build();

        var webSocket = new BackgroundWebSocketHandler(client, new Handler(Looper.MainLooper!));

        webSocket.Connect(server.Url);
        webSocket.StopReconnector();

        await WaitUntilAsync(() => server.Received.Contains("close"), "the late socket was left open");

        return "the socket opened after the session had ended and closed itself";
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

        using var server = new FakeSignallingServer();
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

    /// <summary>
    /// The reconnector used to outlive the session for the life of the process, with each attempt
    /// reporting Disconnected on a client that had already given up. Two ticks, because the first
    /// one can fall just outside a short wait.
    /// </summary>
    private static async Task<string> FailedPublishStopsReconnectingAsync(Activity activity)
    {
        using var server = new FakeSignallingServer { DropBeforeUpgrade = true };
        using var client = new AntMediaClient(Options(server.Url, reconnection: true), activity);

        var disconnects = 0;
        client.Disconnected += (_, _) => Interlocked.Increment(ref disconnects);

        await PublishFailsAsync(client, "dropped");

        var attempts = server.Connections;
        var reported = Volatile.Read(ref disconnects);

        await Task.Delay(ReconnectorTick * 2);

        if (server.Connections != attempts)
        {
            throw new InvalidOperationException(
                $"{server.Connections - attempts} connection attempts after the publish had failed.");
        }

        if (Volatile.Read(ref disconnects) != reported)
        {
            throw new InvalidOperationException("Disconnected was raised after the publish had failed.");
        }

        return $"{attempts} connection attempt, and none in the {(ReconnectorTick * 2).TotalSeconds:0}s after";
    }

    /// <summary>
    /// The server is reachable and says no, so the socket is up when the start fails. That failure
    /// arrives on a pool thread, where the SDK's websocket calls throw, so this also checks that
    /// the stop reaches the server at all.
    /// </summary>
    private static async Task<string> RejectedPublishClosesSocketAsync(Activity activity)
    {
        const string streamId = "rejected";

        using var server = new FakeSignallingServer { PublishError = "not_allowed" };
        using var client = new AntMediaClient(Options(server.Url, reconnection: true), activity);

        await PublishFailsAsync(client, streamId);

        await WaitUntilAsync(() => server.Received.Contains($"stop {streamId}"), "the server was never told to stop");
        await WaitUntilAsync(() => server.Open == 0, "the socket was left open");
        await StaysClosedAsync(server);

        return "stop reached the server, the socket closed and nothing reconnected";
    }

    /// <summary>
    /// Stop() can't close the socket straight away, because publish_finished comes back over it
    /// and PublishFinished is documented as following a stop.
    /// </summary>
    private static async Task<string> StopWaitsForFinishedAsync(Activity activity)
    {
        const string streamId = "stopped";

        using var server = new FakeSignallingServer();
        using var client = new AntMediaClient(Options(server.Url, reconnection: true), activity);

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PublishFinished += (_, _) => finished.TrySetResult();

        await client.PublishAsync(streamId).WaitAsync(Limit);
        client.Stop();

        await finished.Task.WaitAsync(Limit);
        await WaitUntilAsync(() => server.Open == 0, "the socket was left open after publish_finished");
        await StaysClosedAsync(server);

        return "PublishFinished was raised, then the socket closed and nothing reconnected";
    }

    private static async Task<string> StopGivesUpOnSilentServerAsync(Activity activity)
    {
        using var server = new FakeSignallingServer { AnswersStop = false };
        using var client = new AntMediaClient(Options(server.Url, reconnection: true), activity);

        await client.PublishAsync("unanswered").WaitAsync(Limit);
        client.Stop();

        await WaitUntilAsync(() => server.Open == 0, "the socket was left open after an unanswered stop");
        await StaysClosedAsync(server);

        return "the socket closed without an answer and nothing reconnected";
    }

    /// <summary>
    /// The session that failed has to hand the renderer back before the retry takes it: one still
    /// initialised makes the retry throw, and one released late goes dark under the new session.
    /// </summary>
    private static async Task<string> RetryKeepsRendererAsync(Activity activity)
    {
        const string streamId = "retried";

        using var server = new FakeSignallingServer { DropBeforeUpgrade = true };
        using var client = new AntMediaClient(Options(server.Url, reconnection: true), activity);

        var renderer = new SurfaceViewRenderer(activity);
        client.SetLocalRenderer(renderer);

        await PublishFailsAsync(client, streamId);

        server.DropBeforeUpgrade = false;
        await client.PublishAsync(streamId).WaitAsync(Limit);

        await RendererStaysInitialisedAsync(renderer);
        client.Stop();

        return "the retry published and kept the renderer";
    }

    /// <summary>The same hand-over as a retry, with the old session running when it happens.</summary>
    private static async Task<string> RestartKeepsRendererAsync(Activity activity)
    {
        using var server = new FakeSignallingServer();
        using var client = new AntMediaClient(Options(server.Url, reconnection: true), activity);

        var renderer = new SurfaceViewRenderer(activity);
        client.SetLocalRenderer(renderer);

        await client.PublishAsync("first").WaitAsync(Limit);
        await client.PublishAsync("second").WaitAsync(Limit);

        await WaitUntilAsync(() => server.Received.Contains("stop first"), "the first stream was never stopped");
        await WaitUntilAsync(() => server.Open == 1, "the first session's socket was left open");
        await RendererStaysInitialisedAsync(renderer);
        client.Stop();

        return "stopped the first stream, published the second and kept the renderer";
    }

    private static async Task PublishFailsAsync(AntMediaClient client, string streamId)
    {
        try
        {
            await client.PublishAsync(streamId).WaitAsync(Limit);
        }
        catch (AntMediaException)
        {
            return;
        }

        throw new InvalidOperationException($"publishing '{streamId}' should have failed.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string failure)
    {
        var deadline = DateTime.UtcNow + Limit;

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException(failure);
            }

            await Task.Delay(100);
        }
    }

    private static async Task StaysClosedAsync(FakeSignallingServer server)
    {
        var attempts = server.Connections;

        await Task.Delay(ReconnectorTick);

        if (server.Connections != attempts)
        {
            throw new InvalidOperationException("the reconnector was still running after the session ended.");
        }
    }

    /// <summary>
    /// A renderer only says it is initialised by refusing to be initialised again. The wait gives
    /// a release queued by the previous session the time to arrive.
    /// </summary>
    private static async Task RendererStaysInitialisedAsync(SurfaceViewRenderer renderer)
    {
        await Task.Delay(TimeSpan.FromSeconds(1));

        try
        {
            renderer.Init(null, null);
        }
        catch (Java.Lang.IllegalStateException)
        {
            return;
        }

        renderer.Release();
        throw new InvalidOperationException("the renderer was released under the running session.");
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
            // BackgroundWebSocketConnection reaches CreateWriter twice.
            if (!Stalled.TrySetResult())
            {
                return;
            }

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

        public TaskCompletionSource Closed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool OpenedOnMainThread { get; private set; }

        public void OnOpen()
        {
            OpenedOnMainThread = Looper.MyLooper() == Looper.MainLooper;
            Opened.TrySetResult();
        }

        public void OnClose(Observer.WebSocketCloseNotification? notification, string? reason)
        {
            Opened.TrySetException(new InvalidOperationException($"closed: {notification} {reason}"));
            Closed.TrySetResult();
        }

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
