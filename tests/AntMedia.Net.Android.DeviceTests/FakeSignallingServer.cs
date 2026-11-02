using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AntMedia.Net.Android.DeviceTests;

/// <summary>
/// Just enough of an Ant Media Server on loopback to get through the websocket: it answers the
/// upgrade, and confirms a publish as soon as the client sends anything. There is no media, so a
/// pass here says the signalling path works and nothing about streaming; that is the live check's
/// job, and this one runs on the offline CI job, which has no server.
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

    /// <summary>The stream id to confirm when the client publishes.</summary>
    public string StreamId { get; init; } = string.Empty;

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

                // The first frame the client sends is the publish command.
                if (await stream.ReadAsync(buffer).ConfigureAwait(false) > 0)
                {
                    await stream.WriteAsync(TextFrame(
                        $$"""{"command":"notification","definition":"publish_started","streamId":"{{StreamId}}"}""")).ConfigureAwait(false);
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

    private static byte[] TextFrame(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);

        // Short frames only: FIN + text opcode, then a 7-bit length, unmasked from a server.
        return [0x81, checked((byte)payload.Length), .. payload];
    }

    [GeneratedRegex("Sec-WebSocket-Key:(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex WebSocketKey();
}
