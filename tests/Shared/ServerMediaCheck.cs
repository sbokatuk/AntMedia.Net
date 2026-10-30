using System.Diagnostics;

namespace AntMedia.Net.DeviceTests;

/// <summary>
/// Proves that media reached the server, which a publish confirmation does not: the server sends
/// publish_started before ICE connects, so PublishAsync completes even when no media can flow.
///
/// The proof is the stream's HLS playlist. A segment is listed only once the server has received
/// frames and muxed them, so it cannot appear from signalling alone, and it is visible over plain
/// HTTP from every place the check runs, including an emulator reaching its host at 10.0.2.2.
/// The client exposes no ICE state to ask instead.
///
/// Linked into the Android, iOS and Mac device tests, so they all apply the same standard.
/// </summary>
internal static class ServerMediaCheck
{
    /// <summary>
    /// Keeps the publish running until the playlist lists a segment, and throws if it never does
    /// or the publish ends first. <paramref name="serverUrl" /> is the websocket url the client
    /// published to, which has the application as its first path segment.
    /// </summary>
    public static async Task<string> WaitForSegmentAsync(
        IAntMediaClient client,
        string serverUrl,
        string streamId,
        TimeSpan timeout)
    {
        var playlist = PlaylistUrl(serverUrl, streamId);

        // SocketsHttpHandler rather than HttpClient's default, which is the platform's own stack:
        // Android refuses cleartext to 10.0.2.2 and App Transport Security applies on Apple, and
        // neither has anything to do with whether media arrived.
        using var http = new HttpClient(new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(5) };

        var elapsed = Stopwatch.StartNew();
        var lastResult = "no answer yet";

        while (elapsed.Elapsed < timeout)
        {
            if (!client.IsStreaming)
            {
                throw new InvalidOperationException(
                    $"the publish ended after {elapsed.Elapsed.TotalSeconds:0}s with no media at " +
                    $"the server: {playlist} lists no segment ({lastResult}).");
            }

            try
            {
                using var response = await http.GetAsync(playlist);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();

                    if (body.Contains("#EXTINF"))
                    {
                        return $"HLS segment listed after {elapsed.Elapsed.TotalSeconds:0}s at {playlist}";
                    }

                    lastResult = "playlist has no segment yet";
                }
                else
                {
                    lastResult = $"HTTP {(int)response.StatusCode}";
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastResult = exception.Message;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException(
            $"the server confirmed the publish but no media reached it: {playlist} lists no " +
            $"segment after {timeout.TotalSeconds:0}s ({lastResult}). See \"A pass proves media\" " +
            "in docs/BUILD.md for what stops ICE connecting.");
    }

    private static Uri PlaylistUrl(string serverUrl, string streamId)
    {
        var server = new Uri(serverUrl);
        var scheme = server.Scheme == "wss" ? "https" : "http";

        return new Uri($"{scheme}://{server.Authority}/{server.Segments[1]}streams/{streamId}.m3u8");
    }
}
