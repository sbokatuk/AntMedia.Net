using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AntMedia.Net.Android.DeviceTests;

/// <summary>
/// Just enough of an Ant Media Server on loopback to get through the websocket: it answers the
/// upgrade, and confirms a publish as soon as the client sends anything. There is no media, so a
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

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

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

    /// <summary>
    /// A second offer, sent once the client has answered the first. A play sends nothing else in
    /// between, and even an early ICE candidate means the answer has been set, so the client is
    /// ready for it either way.
    /// </summary>
    public string? Renegotiation { get; init; }

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
                if (await stream.ReadAsync(buffer).ConfigureAwait(false) > 0)
                {
                    if (Offer is null)
                    {
                        await stream.WriteAsync(TextFrame(Notification("publish_started"))).ConfigureAwait(false);
                    }
                    else
                    {
                        await stream.WriteAsync(TextFrame(Configuration(Offer))).ConfigureAwait(false);
                        await stream.WriteAsync(TextFrame(Notification("play_started"))).ConfigureAwait(false);

                        if (Renegotiation is not null && await stream.ReadAsync(buffer).ConfigureAwait(false) > 0)
                        {
                            await stream.WriteAsync(TextFrame(Configuration(Renegotiation))).ConfigureAwait(false);
                        }
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
        }
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
