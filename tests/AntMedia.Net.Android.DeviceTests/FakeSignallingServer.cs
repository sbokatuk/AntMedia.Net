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

    /// <summary>False leaves a stop unanswered, as a server that has hung would.</summary>
    public bool AnswersStop { get; init; } = true;

    /// <summary>Every connection the client has made, the reconnector's included.</summary>
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>The websockets open right now.</summary>
    public int Open => Volatile.Read(ref _open);

    /// <summary>What the client has asked for, as "command streamId", or "close".</summary>
    public IReadOnlyCollection<string> Received => _received;

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
        }
    }

    private async Task ServeFramesAsync(NetworkStream stream)
    {
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

            var reply = command switch
            {
                "publish" when PublishError is not null =>
                    $$"""{"command":"error","definition":"{{PublishError}}","streamId":"{{streamId}}"}""",
                "publish" =>
                    $$"""{"command":"notification","definition":"publish_started","streamId":"{{streamId}}"}""",
                "stop" when AnswersStop =>
                    $$"""{"command":"notification","definition":"publish_finished","streamId":"{{streamId}}"}""",
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

    private static byte[] TextFrame(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);

        // Short frames only: FIN + text opcode, then a 7-bit length, unmasked from a server.
        return [0x81, checked((byte)payload.Length), .. payload];
    }

    [GeneratedRegex("Sec-WebSocket-Key:(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex WebSocketKey();
}
