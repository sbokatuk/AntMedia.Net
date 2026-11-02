using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AntMedia.Net.Android.DeviceTests;

/// <summary>
/// Just enough of an Ant Media Server on loopback to get through the websocket: it answers the
/// upgrade, and confirms a publish as soon as the client asks for one. There is no media, so a
/// pass here says the signalling path works and nothing about streaming; that is the live check's
/// job, and this one runs on the offline CI job, which has no server.
///
/// Given an <see cref="Offer" /> it plays instead, which the community server can't: the client
/// gets the offer, sets it on a real peer connection and answers it, and only ICE never starts.
/// </summary>
internal sealed partial class FakeSignallingServer : IDisposable
{
    // Fixed by RFC 6455.
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const int TextOpcode = 1;
    private const int CloseOpcode = 8;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

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

    /// <summary>The stream id to confirm when the client publishes or plays.</summary>
    public string StreamId { get; init; } = string.Empty;

    /// <summary>
    /// The server's offer, sent when the client asks for anything, followed by play_started. Ant
    /// Media sends play_started once ICE connects, and there is no ICE here, so it goes at once.
    /// </summary>
    public string? Offer { get; init; }

    /// <summary>Holds the <see cref="Offer" /> back until this completes.</summary>
    public Task? OfferAfter { get; init; }

    /// <summary>
    /// A second offer, sent once the client's answer to the first arrives. The client sends that
    /// only after setting the first offer, so it is ready for another.
    /// </summary>
    public string? Renegotiation { get; init; }

    /// <summary>Completes when the client's first command arrives.</summary>
    public Task CommandReceived => _commandReceived.Task;

    /// <summary>
    /// True once the client answers the <see cref="Offer" />. It answers only after setting the
    /// offer, so every track callback for it has run by then. False if the connection ends first.
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
        using (connection)
        {
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

                // The first frame the client sends is the publish or play command.
                if (await ReadCommandAsync(stream).ConfigureAwait(false) is null)
                {
                    return;
                }

                _commandReceived.TrySetResult();

                if (Offer is null)
                {
                    await stream.WriteAsync(TextFrame(Notification("publish_started"))).ConfigureAwait(false);
                }
                else
                {
                    if (OfferAfter is not null)
                    {
                        await OfferAfter.ConfigureAwait(false);
                    }

                    await stream.WriteAsync(TextFrame(Configuration(Offer))).ConfigureAwait(false);
                    await stream.WriteAsync(TextFrame(Notification("play_started"))).ConfigureAwait(false);

                    // Its answer is the client's only takeConfiguration. ICE candidates can come
                    // either side of it.
                    string? command;

                    do
                    {
                        command = await ReadCommandAsync(stream).ConfigureAwait(false);
                    }
                    while (command is not null and not "takeConfiguration");

                    if (command is null)
                    {
                        return;
                    }

                    _answered.TrySetResult(true);

                    if (Renegotiation is not null)
                    {
                        await stream.WriteAsync(TextFrame(Configuration(Renegotiation))).ConfigureAwait(false);
                    }
                }

                while (await stream.ReadAsync(buffer).ConfigureAwait(false) > 0)
                {
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // The client went away, which is how every test here ends.
            }
            finally
            {
                _answered.TrySetResult(false);
            }
        }
    }

    /// <summary>The command in the client's next text frame, or null if it closes instead.</summary>
    private static async Task<string?> ReadCommandAsync(NetworkStream stream)
    {
        while (true)
        {
            var (opcode, payload) = await ReadFrameAsync(stream).ConfigureAwait(false);

            if (opcode == CloseOpcode)
            {
                return null;
            }

            if (opcode == TextOpcode)
            {
                using var json = JsonDocument.Parse(payload);
                return json.RootElement.GetProperty("command").GetString() ?? string.Empty;
            }
        }
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

    private string Notification(string definition) =>
        $$"""{"command":"notification","definition":"{{definition}}","streamId":"{{StreamId}}"}""";

    private string Configuration(string offer) =>
        $$"""{"command":"takeConfiguration","streamId":"{{StreamId}}","type":"offer","sdp":"{{JsonEncodedText.Encode(offer)}}"}""";

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
