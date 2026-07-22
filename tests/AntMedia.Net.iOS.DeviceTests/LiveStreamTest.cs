using AVFoundation;

namespace AntMedia.Net.Apple.DeviceTests;

/// <summary>
/// Publishes to a real Ant Media Server and waits for it to confirm. The Apple counterpart of
/// tests/AntMedia.Net.Android.DeviceTests/LiveStreamTest.cs, and deliberately the same shape.
///
/// Everything else in this app is offline: it proves the native frameworks load and the bound API
/// is callable, which is all a run without a server can do. This is the one check that proves a
/// stream actually reaches somewhere — websocket signalling, SDP exchange, ICE, and the server
/// accepting the broadcast.
///
/// It runs through <see cref="IAntMediaClient" /> rather than the binding directly, so it covers
/// the stack a consumer really uses, including PublishAsync's callback-to-Task bridge.
///
/// Skipped unless ANTMEDIA_TEST_SERVER is set. On Mac Catalyst that is an ordinary environment
/// variable; on the iOS simulator simctl passes it through as SIMCTL_CHILD_ANTMEDIA_TEST_SERVER.
///
/// A word on where this can run: publishing needs a camera. Mac Catalyst has the Mac's, and the
/// Android emulator has a synthetic one, but an iOS simulator has neither camera nor microphone,
/// so a publish there stalls with no callback. The iOS runner leaves the variable unset.
/// </summary>
public static class LiveStreamTest
{
    public static async Task<string> RunAsync(string serverUrl)
    {
        await RequireCaptureAccessAsync(AVAuthorizationMediaType.Video);
        await RequireCaptureAccessAsync(AVAuthorizationMediaType.Audio);

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

        using var client = new AntMediaClient(options);

        // No view is attached. The camera still runs and frames still reach the encoder; nothing
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

    /// <summary>
    /// Checks — and if necessary asks for — permission to capture, before anything opens a device.
    ///
    /// Without this the failure is silent and misleading. Signalling, SDP and ICE all succeed
    /// against the server, the capture devices simply produce no frames, and ten seconds later the
    /// server gives up with "WebRTC ingest is not started. So publish timeout is firing" — which
    /// reads like a networking or codec problem rather than a missing permission.
    ///
    /// On macOS the prompt only appears for an app launched through LaunchServices, so
    /// run-mac-device-tests.sh uses `open` rather than running the binary directly.
    /// </summary>
    private static async Task RequireCaptureAccessAsync(AVAuthorizationMediaType mediaType)
    {
        var status = AVCaptureDevice.GetAuthorizationStatus(mediaType);

        if (status == AVAuthorizationStatus.NotDetermined)
        {
            status = await AVCaptureDevice.RequestAccessForMediaTypeAsync(mediaType)
                ? AVAuthorizationStatus.Authorized
                : AVAuthorizationStatus.Denied;
        }

        if (status != AVAuthorizationStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"no permission to capture {mediaType}: {status}. Grant it in System Settings > " +
                "Privacy & Security, or reset it with " +
                "`tccutil reset Camera com.sbokatuk.antmedia.macdevicetests`.");
        }
    }
}
