using Android.App;

namespace AntMedia.Net.Android.DeviceTests;

/// <summary>
/// Publishes to a real Ant Media Server and waits for it to confirm.
///
/// Everything else in this app is offline: it proves the native libraries load and the bound API
/// is callable, which is all CI can do without a server. This is the one check that proves a
/// stream actually reaches somewhere — websocket signalling, SDP exchange, ICE, and the server
/// accepting the broadcast.
///
/// It runs through <see cref="IAntMediaClient" /> rather than the binding directly, so it covers
/// the stack a consumer really uses, including PublishAsync's callback-to-Task bridge.
///
/// Skipped unless a server url is supplied, because most runs have no server:
///   adb shell am start -n &lt;pkg&gt;/.MainActivity -e serverUrl ws://10.0.2.2:5080/WebRTCAppEE/websocket
/// </summary>
public static class LiveStreamTest
{
    /// <summary>
    /// 10.0.2.2 is how an Android emulator reaches a service on the host, which is where CI runs
    /// the Ant Media Server container.
    /// </summary>
    public static async Task<string> RunAsync(Activity activity, string serverUrl)
    {
        // A fresh id per run: republishing a live stream id is rejected by the server, which would
        // make a re-run fail for a reason that has nothing to do with the code.
        var streamId = $"e2e{DateTime.UtcNow:HHmmss}";

        var options = new AntMediaOptions
        {
            ServerUrl = serverUrl,
            VideoWidth = 320,
            VideoHeight = 240,
            VideoFps = 15,
            Timeout = TimeSpan.FromSeconds(45),
        };

        using var client = new AntMediaClient(options, activity);

        // No renderer is attached. The emulator's fake camera still produces frames, and nothing
        // here needs them on screen — the assertion is that the server accepted the broadcast.
        await client.PublishAsync(streamId);

        if (!client.IsStreaming)
        {
            throw new InvalidOperationException(
                "PublishAsync returned but the client does not consider itself streaming.");
        }

        client.Stop();
        return $"published '{streamId}' to {serverUrl}";
    }
}
