using Android.App;
using Org.Webrtc;

// Aliased: System.Diagnostics also has an Activity.
using Stopwatch = System.Diagnostics.Stopwatch;

namespace AntMedia.Net.Android.DeviceTests;

/// <summary>
/// Checks that a play reports the remote video track, and that the SDK still gets to attach it to
/// the renderer and release it again, which its default listener does and the client's listener
/// has to let it.
///
/// The community server only takes publishes, so the offer comes from
/// <see cref="FakeSignallingServer" />. Everything on the client side is real: the SDK sets the
/// offer on a libwebrtc peer connection, and the track events come from there. With no ICE no
/// frame ever arrives, so these say nothing about rendering, only about who holds the renderer.
/// </summary>
public static class RemoteTrackTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    // The track id from the offer's msid line.
    private const string TrackId = "ARDAMSvvideoTrack0";

    /// <summary>
    /// The video section of an offer Ant Media Server Enterprise sent to this SDK for a play, with
    /// its audio and data channel sections left out.
    /// </summary>
    private static readonly string Offer = """
        v=0
        o=- 1832232327687221975 2 IN IP4 127.0.0.1
        s=-
        t=0 0
        a=group:BUNDLE 0
        a=extmap-allow-mixed
        a=msid-semantic: WMS ARDAMS
        m=video 9 UDP/TLS/RTP/SAVPF 127 125
        c=IN IP4 0.0.0.0
        a=rtcp:9 IN IP4 0.0.0.0
        a=ice-ufrag:chKE
        a=ice-pwd:O1LOMjJzQOo4hyWhLbCwzrqz
        a=ice-options:trickle renomination
        a=fingerprint:sha-256 40:56:A0:13:B7:9B:EF:8C:69:87:4C:D3:E2:AD:A7:8A:23:AE:1C:59:E5:2F:63:C9:08:15:76:E9:36:2C:B7:74
        a=setup:actpass
        a=mid:0
        a=extmap:1 urn:ietf:params:rtp-hdrext:toffset
        a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
        a=extmap:3 urn:3gpp:video-orientation
        a=extmap:4 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
        a=extmap:5 http://www.webrtc.org/experiments/rtp-hdrext/playout-delay
        a=extmap:6 http://www.webrtc.org/experiments/rtp-hdrext/video-content-type
        a=extmap:7 http://www.webrtc.org/experiments/rtp-hdrext/video-timing
        a=extmap:8 http://www.webrtc.org/experiments/rtp-hdrext/color-space
        a=extmap:9 urn:ietf:params:rtp-hdrext:sdes:mid
        a=extmap:10 urn:ietf:params:rtp-hdrext:sdes:rtp-stream-id
        a=extmap:11 urn:ietf:params:rtp-hdrext:sdes:repaired-rtp-stream-id
        a=sendonly
        a=msid:ARDAMS ARDAMSvvideoTrack0
        a=rtcp-mux
        a=rtcp-rsize
        a=rtpmap:127 H264/90000
        a=rtcp-fb:127 goog-remb
        a=rtcp-fb:127 transport-cc
        a=rtcp-fb:127 ccm fir
        a=rtcp-fb:127 nack
        a=rtcp-fb:127 nack pli
        a=fmtp:127 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f
        a=rtpmap:125 rtx/90000
        a=fmtp:125 apt=127
        a=ssrc-group:FID 3237231459 1286856751
        a=ssrc:3237231459 cname:wTflAnsN6YUYLp1G
        a=ssrc:3237231459 msid:ARDAMS ARDAMSvvideoTrack0
        a=ssrc:1286856751 cname:wTflAnsN6YUYLp1G
        a=ssrc:1286856751 msid:ARDAMS ARDAMSvvideoTrack0

        """.ReplaceLineEndings("\r\n");

    /// <summary>
    /// The same offer, renegotiated with the server no longer sending: how a remote track goes
    /// away under Unified Plan, which libwebrtc reports as onRemoveTrack.
    /// </summary>
    private static readonly string StoppedSending = Offer
        .Replace(" 2 IN IP4 ", " 3 IN IP4 ")
        .Replace("a=sendonly", "a=inactive");

    public static IReadOnlyList<(string Name, Func<Activity, Task<string>> Run)> All =>
    [
        ("play reports the remote video track", ReportsTrackAsync),
        ("an ended remote track gives the renderer back", ReleasesRendererAsync),
    ];

    /// <summary>
    /// TrackAdded used to be wired to the one-argument onNewVideoTrack, which the SDK never calls,
    /// so it never fired on Android. The renderer check is what fails if the fix stops calling
    /// base: the SDK's default is what attaches the track to it.
    /// </summary>
    private static async Task<string> ReportsTrackAsync(Activity activity)
    {
        const string streamId = "faketrack";

        using var server = new FakeSignallingServer { StreamId = streamId, Offer = Offer };
        var renderer = new SurfaceViewRenderer(activity);
        using var client = new AntMediaClient(Options(server.Url), activity);
        client.SetRemoteRenderer(renderer);

        var added = Watch(handler => client.TrackAdded += handler);

        await client.PlayAsync(streamId).WaitAsync(Limit);
        var track = await Expect(added, "TrackAdded");

        // Set before TrackAdded is raised, so there is nothing to wait for.
        if (HeldTrack(renderer) is var held && held != TrackId)
        {
            throw new InvalidOperationException(
                $"TrackAdded fired but the renderer holds {Describe(held)}, not the track.");
        }

        client.Stop();
        return $"TrackAdded {track.Kind} {track.TrackId}, attached to the renderer";
    }

    /// <summary>
    /// The SDK's default listener releases the renderer when its track ends, so that the next
    /// track can have it: it only ever attaches a track to a renderer holding none.
    /// </summary>
    private static async Task<string> ReleasesRendererAsync(Activity activity)
    {
        const string streamId = "faketrackend";

        using var server = new FakeSignallingServer
        {
            StreamId = streamId,
            Offer = Offer,
            Renegotiation = StoppedSending,
        };

        var renderer = new SurfaceViewRenderer(activity);
        using var client = new AntMediaClient(Options(server.Url), activity);
        client.SetRemoteRenderer(renderer);

        // Looked at as the track arrives: a renderer that never took it would pass below.
        string? heldWhenAdded = null;
        client.TrackAdded += (_, _) => heldWhenAdded = HeldTrack(renderer);

        var removed = Watch(handler => client.TrackRemoved += handler);

        await client.PlayAsync(streamId).WaitAsync(Limit);
        var track = await Expect(removed, "TrackRemoved");

        if (heldWhenAdded != TrackId)
        {
            throw new InvalidOperationException(
                $"the renderer held {Describe(heldWhenAdded)} when the track arrived, so there was nothing to release.");
        }

        // Released on the main thread, where this check runs, so it is waited for rather than
        // expected straight away.
        var elapsed = Stopwatch.StartNew();

        while (HeldTrack(renderer) is { } held)
        {
            if (elapsed.Elapsed > Limit)
            {
                throw new InvalidOperationException(
                    $"TrackRemoved fired but the renderer still holds {Describe(held)} after {Limit.TotalSeconds:0}s.");
            }

            await Task.Delay(100);
        }

        client.Stop();
        return $"TrackRemoved {track.Kind} {track.TrackId}, renderer released";
    }

    private static Task<AntMediaTrackEventArgs> Watch(Action<EventHandler<AntMediaTrackEventArgs>> subscribe)
    {
        // Raised on the SDK's signalling thread.
        var seen = new TaskCompletionSource<AntMediaTrackEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscribe((_, e) => seen.TrySetResult(e));
        return seen.Task;
    }

    private static async Task<AntMediaTrackEventArgs> Expect(Task<AntMediaTrackEventArgs> seen, string name)
    {
        AntMediaTrackEventArgs track;

        try
        {
            track = await seen.WaitAsync(Limit);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException($"{name} did not fire within {Limit.TotalSeconds:0}s of the offer.");
        }

        if (track.TrackId != TrackId || track.Kind != "video")
        {
            throw new InvalidOperationException(
                $"{name} reported {track.Kind} '{track.TrackId}', not video '{TrackId}'.");
        }

        return track;
    }

    /// <summary>The SDK marks a remote renderer as taken by setting its tag to the track.</summary>
    private static string? HeldTrack(SurfaceViewRenderer renderer) =>
        renderer.Tag is VideoTrack track ? track.Id() : renderer.Tag?.ToString();

    private static string Describe(string? held) => held is null ? "no track" : $"'{held}'";

    // Audio and video off: the offline CI emulator has neither, and a play needs neither. Video
    // off doesn't stop the client receiving it.
    private static AntMediaOptions Options(string url) => new()
    {
        ServerUrl = url,
        VideoEnabled = false,
        AudioEnabled = false,
        ReconnectionEnabled = false,
        Timeout = Limit,
    };
}
