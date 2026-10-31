using System.Diagnostics;
using Android.OS;
using Android.Util;
using AntMedia.WebRTC.Websocket;
using DE.Tavendo.Autobahn;
using Java.Net;

using JavaThread = Java.Lang.Thread;
using Observer = DE.Tavendo.Autobahn.IWebSocket.IWebSocketConnectionObserver;

namespace AntMedia.Net;

/// <summary>
/// Keeps the websocket connect off the thread that asked for it, because that thread is the main
/// one and the connect can leave it waiting for ever.
///
/// de.tavendo.autobahn.WebSocketConnection.connect() starts a thread and then calls a bare wait()
/// on it, with no condition to re-check, while the new thread calls notifyAll() once as it comes
/// up. It does that for the socket thread, the reader and the writer, and waits the same way for
/// the TCP connect. If the other thread gets there first the wake-up is lost, and the app freezes
/// with its main thread in Object.wait under connect(), createReader() or createWriter().
///
/// Running WebRTCClient.Init() elsewhere is not the way out. WebSocketHandler.connect() throws
/// unless it is on the thread that built the client, and building the client on a worker moves
/// every callback there too, while the renderers and the audio manager insist on the main thread.
/// So the SDK's own calls stay where they are and only the body of autobahn's connect() moves,
/// which has no thread rule of its own: its callbacks reach the thread that created the connection
/// through a Handler made in the constructor, and that is still the client's thread.
/// </summary>
internal sealed class BackgroundWebSocketHandler : WebSocketHandler
{
    private readonly Handler _handler;

    private BackgroundWebSocketConnection? _connection;

    public BackgroundWebSocketHandler(IAntMediaSignallingEvents events, Handler handler)
        : base(events, handler)
    {
        _handler = handler;
    }

    public override WebSocketConnection CreteWebSocket() =>
        _connection = new BackgroundWebSocketConnection();

    /// <summary>
    /// The SDK's reconnector calls this every five seconds while the socket is down. A blocking
    /// connect used to hold it off; without that, each tick would start another attempt.
    /// </summary>
    public override void Connect(string? wsUrl)
    {
        if (_connection is not { IsConnecting: true })
        {
            base.Connect(wsUrl);
        }
    }

    /// <summary>
    /// WebRTCClient.connectWebSocket() disconnects a handler it did not create before connecting
    /// it, and the SDK's disconnect() dereferences a connection that does not exist yet.
    /// </summary>
    public override void Disconnect(bool waitForComplete)
    {
        if (_connection is not null)
        {
            base.Disconnect(waitForComplete);
        }
    }

    /// <summary>
    /// A failed connect is reported on the thread that ran it, unlike every other callback. Sent
    /// back to the client's thread, which is where the SDK has always had it.
    /// </summary>
    public override void OnClose(Observer.WebSocketCloseNotification? notification, string? reason)
    {
        if (Looper.MyLooper() == _handler.Looper)
        {
            base.OnClose(notification, reason);
        }
        else
        {
            _handler.Post(() => base.OnClose(notification, reason));
        }
    }
}

/// <summary>
/// Runs connect() on its own thread, and interrupts that thread if it stalls. All four of the
/// SDK's waits catch InterruptedException and carry on, which is what makes this a recovery rather
/// than just a smaller hang: after a lost wake-up the thread it was waiting for is already up, so
/// the connect continues as though nothing had happened.
/// </summary>
internal class BackgroundWebSocketConnection : WebSocketConnection
{
    private const string Tag = "AntMediaClient";

    /// <summary>
    /// The SDK's own limit, from when it tried this itself (WebSocketHandler still has the code,
    /// commented out). A lost wake-up before the socket opens looks exactly like a slow TCP
    /// connect, so this covers both: a lost wake-up recovers, and a connect still going after this
    /// long fails. On the main thread, a connect that slow was long enough for an ANR anyway.
    /// </summary>
    private static readonly TimeSpan ConnectLimit =
        TimeSpan.FromMilliseconds(WebSocketConstants.WebsocketConnectionTimeout);

    /// <summary>
    /// How long the reader and writer get once the socket is open. Starting a thread takes
    /// milliseconds, and nothing else happens in these two waits.
    /// </summary>
    private static readonly TimeSpan StartLimit = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    private volatile bool _connecting;

    public bool IsConnecting => _connecting;

    public override void Connect(URI? webSocketURI, Observer? connectionObserver)
    {
        _connecting = true;

        var connector = new JavaThread(
            () =>
            {
                try
                {
                    base.Connect(webSocketURI, connectionObserver);
                }
                catch (Exception exception)
                {
                    // A url autobahn rejects, or a thread interrupted before it had started. An
                    // exception escaping here would take the process down.
                    Log.Error(Tag, $"websocket connect failed: {exception}");
                    connectionObserver?.OnClose(
                        Observer.WebSocketCloseNotification.InternalError,
                        exception.Message);
                }
                finally
                {
                    _connecting = false;
                }
            },
            "AntMediaConnector");

        connector.Start();
        _ = WatchAsync(connector);
    }

    private async Task WatchAsync(JavaThread connector)
    {
        var waited = Stopwatch.StartNew();
        var socketOpen = false;

        while (connector.IsAlive)
        {
            await Task.Delay(Poll).ConfigureAwait(false);

            if (!socketOpen && IsConnected)
            {
                socketOpen = true;
                waited.Restart();
            }

            if (waited.Elapsed >= (socketOpen ? StartLimit : ConnectLimit))
            {
                Log.Warn(Tag, $"websocket connect stalled after {waited.Elapsed.TotalSeconds:0.0}s, interrupting it.");
                connector.Interrupt();
                waited.Restart();
            }
        }
    }
}
