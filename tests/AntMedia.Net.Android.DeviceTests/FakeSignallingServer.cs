using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AntMedia.Net.Android.DeviceTests;

/// <summary>
/// Just enough of an Ant Media Server on loopback to get through the websocket: it answers the
/// upgrade, confirms a publish, finishes it on a stop, and answers a close. There is no media, so
/// a pass here says the signalling path works and nothing about streaming; that is the live
/// check's job, and this one runs on the offline CI job, which has no server.
///
/// Given an <see cref="Offer" /> it plays as well, which the community server can't: the client
/// gets the offer, sets it on a real peer connection and answers it, and only ICE never starts.
///
/// With no media there is no ICE either, so the SDK's own release on ICE disconnect never fires
/// here. Whatever ends a session in these checks had to come from the client.
/// </summary>
internal sealed partial class FakeSignallingServer : IDisposable
{
    // Fixed by RFC 6455.
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const int TextOpcode = 1;
    private const int CloseOpcode = 8;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentQueue<string> _received = new();

    private int _connections;
    private int _open;

    private readonly TaskCompletionSource _commandReceived =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource<bool> _answered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeSignallingServer()
    {
        _listener.Start();
        _ = ServeAsync();
    }

    public string Url => $"ws://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/LiveApp/websocket";

    /// <summary>Hangs up on each connection before the upgrade, which fails a publish.</summary>
    public bool DropBeforeUpgrade { get; set; }

    /// <summary>Answers a publish with this error instead of confirming it.</summary>
    public string? PublishError { get; init; }

    /// <summary>False leaves a publish unanswered, so the start stays pending.</summary>
    public bool ConfirmsPublish { get; set; } = true;

    /// <summary>False leaves a stop unanswered, as a server that has hung would.</summary>
    public bool AnswersStop { get; init; } = true;

    /// <summary>Every connection the client has made, the reconnector's included.</summary>
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>The websockets open right now.</summary>
    public int Open => Volatile.Read(ref _open);

    /// <summary>What the client has asked for, as "command streamId", or "close".</summary>
    public IReadOnlyCollection<string> Received => _received;

    /// <summary>
    /// The server's offer, sent when the client plays, followed by play_started. Ant Media sends
    /// play_started once ICE connects, and there is no ICE here, so it goes at once.
    /// </summary>
    public string? Offer { get; init; }

    /// <summary>Holds the <see cref="Offer" /> back until this completes.</summary>
    public Task? OfferAfter { get; init; }

    /// <summary>
    /// A second offer, sent once the client's answer to the first arrives. The client sends that
    /// only after setting the first offer, so it is ready for another.
    /// </summary>
    public string? Renegotiation { get; init; }

    /// <summary>Holds the <see cref="Renegotiation" /> back until this completes.</summary>
    public Task? RenegotiationAfter { get; init; }

    /// <summary>Completes when the client's first command arrives.</summary>
    public Task CommandReceived => _commandReceived.Task;

    /// <summary>
    /// True once the client answers the last offer: the <see cref="Renegotiation" /> if there is
    /// one, the <see cref="Offer" /> if not. It answers only after setting the offer, so every track
    /// callback for it has run by then. False if the connection ends first.
    /// </summary>
    public Task<bool> Answered => _answered.Task;

    public void Dispose() => _listener.Stop();

    private async Task ServeAsync()
    {
        try
        {
            while (true)
            {
                _ = HandleAsync(await _listener.AcceptTcpClientAsync().ConfigureAwait(false));
            }
        }
        catch (Exception exception) when (exception is ObjectDisposedException or SocketException)
        {
            // Disposed.
        }
    }

    private async Task HandleAsync(TcpClient connection)
    {
        Interlocked.Increment(ref _connections);

        using (connection)
        {
            if (DropBeforeUpgrade)
            {
                return;
            }

            try
            {
                var stream = connection.GetStream();
                var buffer = new byte[4096];

                var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                var request = Encoding.ASCII.GetString(buffer, 0, read);

                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(
                    WebSocketKey().Match(request).Groups[1].Value.Trim() + WebSocketGuid)));

                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    $"Sec-WebSocket-Accept: {accept}\r\n\r\n")).ConfigureAwait(false);

                Interlocked.Increment(ref _open);

                try
                {
                    await ServeFramesAsync(stream).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _open);
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // The client went away.
            }
            finally
            {
                _answered.TrySetResult(false);
            }
        }
    }

    private async Task ServeFramesAsync(NetworkStream stream)
    {
        var finished = "publish_finished";
        var answers = 0;

        while (true)
        {
            var (opcode, payload) = await ReadFrameAsync(stream).ConfigureAwait(false);

            if (opcode == CloseOpcode)
            {
                _received.Enqueue("close");

                // Autobahn's disconnect() only sends a close: the socket goes when this answers.
                await stream.WriteAsync(new byte[] { 0x88, 0 }).ConfigureAwait(false);
                return;
            }

            if (opcode != TextOpcode)
            {
                continue;
            }

            var (command, streamId) = ParseCommand(payload);
            _received.Enqueue($"{command} {streamId}");
            _commandReceived.TrySetResult();

            if (command == "play" && Offer is not null)
            {
                finished = "play_finished";

                if (OfferAfter is not null)
                {
                    await OfferAfter.ConfigureAwait(false);
                }

                await stream.WriteAsync(TextFrame(Configuration(Offer, streamId))).ConfigureAwait(false);
                await stream.WriteAsync(TextFrame(Notification("play_started", streamId))).ConfigureAwait(false);
                continue;
            }

            // The client's answer to an offer. ICE candidates are a command of their own.
            if (command == "takeConfiguration")
            {
                if (++answers == 1 && Renegotiation is not null)
                {
                    if (RenegotiationAfter is not null)
                    {
                        await RenegotiationAfter.ConfigureAwait(false);
                    }

                    await stream.WriteAsync(TextFrame(Configuration(Renegotiation, streamId))).ConfigureAwait(false);
                }
                else
                {
                    _answered.TrySetResult(true);
                }

                continue;
            }

            var reply = command switch
            {
                "publish" when PublishError is not null =>
                    $$"""{"command":"error","definition":"{{PublishError}}","streamId":"{{streamId}}"}""",
                "publish" when ConfirmsPublish => Notification("publish_started", streamId),
                "stop" when AnswersStop => Notification(finished, streamId),
                _ => null,
            };

            if (reply is not null)
            {
                await stream.WriteAsync(TextFrame(reply)).ConfigureAwait(false);
            }
        }
    }

    private static (string? Command, string? StreamId) ParseCommand(byte[] payload)
    {
        using var json = JsonDocument.Parse(payload);

        return (
            json.RootElement.GetProperty("command").GetString(),
            json.RootElement.TryGetProperty("streamId", out var streamId) ? streamId.GetString() : null);
    }

    /// <summary>One client frame, unmasked. Autobahn doesn't fragment what the SDK sends.</summary>
    private static async Task<(int Opcode, byte[] Payload)> ReadFrameAsync(NetworkStream stream)
    {
        var header = new byte[2];
        await stream.ReadExactlyAsync(header).ConfigureAwait(false);

        long length = header[1] & 0x7F;

        if (length >= 126)
        {
            var extended = new byte[length == 126 ? 2 : 8];
            await stream.ReadExactlyAsync(extended).ConfigureAwait(false);
            length = extended.Aggregate(0L, (value, next) => (value << 8) | next);
        }

        var mask = new byte[4];
        await stream.ReadExactlyAsync(mask).ConfigureAwait(false);

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload).ConfigureAwait(false);

        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] ^= mask[i % 4];
        }

        return (header[0] & 0x0F, payload);
    }

    private static string Notification(string definition, string? streamId) =>
        $$"""{"command":"notification","definition":"{{definition}}","streamId":"{{streamId}}"}""";

    private static string Configuration(string offer, string? streamId) =>
        $$"""{"command":"takeConfiguration","streamId":"{{streamId}}","type":"offer","sdp":"{{JsonEncodedText.Encode(offer)}}"}""";

    private static byte[] TextFrame(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);

        // FIN + text opcode, unmasked from a server. A notification fits a 7-bit length, and an
        // offer needs the 16-bit one.
        if (payload.Length < 126)
        {
            return [0x81, (byte)payload.Length, .. payload];
        }

        var length = checked((ushort)payload.Length);
        return [0x81, 126, (byte)(length >> 8), (byte)length, .. payload];
    }

    [GeneratedRegex("Sec-WebSocket-Key:(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex WebSocketKey();
}
