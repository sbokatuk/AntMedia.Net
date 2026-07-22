#if MACCATALYST
using AntMedia.Net.Mac;
#else
using AntMedia.Net.iOS;
#endif
using Foundation;

namespace AntMedia.Net.Apple.DeviceTests;

/// <summary>
/// Offline checks that the packaged binding actually works on the device it is built for —
/// an iOS simulator, or Mac Catalyst. One file, both packages: the two bindings are generated
/// from one ApiDefinition and differ only in namespace.
///
/// They stop short of connecting to a server: a real publish/play round-trip needs a running Ant
/// Media Server, which CI does not have. What is proven here is the part a desktop package test
/// cannot see — that WebRTCiOSSDK.framework and WebRTC.framework load, that the @objc facade is
/// registered with the Objective-C runtime, and that calls through the binding actually dispatch
/// to Swift rather than merely compiling.
/// </summary>
public static class SmokeTests
{
    public delegate void Report(string message);

    public static IReadOnlyList<(string Name, Action<Report> Run)> All =>
    [
        ("frameworks load and the facade is registered", FacadeIsRegistered),
        ("configuration selectors dispatch", ConfigurationSelectorsDispatch),
        ("delegate can be attached", DelegateCanBeAttached),
        ("session state is queryable", SessionStateIsQueryable),
    ];

    /// <summary>
    /// The single most valuable check. Instantiating AMSClient forces dyld to load
    /// WebRTCiOSSDK.framework (and WebRTC.framework, which it links against) and forces the
    /// Objective-C runtime to resolve the facade's class. If the xcframeworks were not embedded
    /// in the package, or the facade was not compiled into the framework, this is where it shows.
    /// </summary>
    private static void FacadeIsRegistered(Report report)
    {
        using var client = new AMSClient();

        report($"AMSClient is {client.Handle}");

        // A static member on the facade, so it exercises class-level dispatch too.
        AMSClient.SetDebug(true);
        report("SetDebug dispatched");
    }

    /// <summary>
    /// Calls that cross into Swift and return nothing. They would throw
    /// "unrecognized selector sent to instance" if the binding's [Export] selectors did not match
    /// the ones the facade actually emits — the most likely way this binding breaks silently.
    /// </summary>
    private static void ConfigurationSelectorsDispatch(Report report)
    {
        using var client = new AMSClient();

        client.SetWebSocketServerUrl("wss://example.invalid:5443/WebRTCAppEE/websocket");
        client.SetEnableDataChannel(true);
        client.SetVideoEnable(true);
        client.SetTargetResolution(640, 480);
        client.SetTargetFps(30);
        client.SetMaxVideoBps(new NSNumber(1_500_000));

        report("six configuration selectors dispatched");
    }

    /// <summary>
    /// The delegate is a weak reference bridged back into Swift, which is the fiddliest part of
    /// the facade. Setting and reading it proves the bridge is wired up.
    /// </summary>
    private static void DelegateCanBeAttached(Report report)
    {
        using var client = new AMSClient();
        var recorder = new RecordingDelegate();

        client.Delegate = recorder;

        if (client.Delegate is null)
        {
            throw new InvalidOperationException("the delegate did not survive assignment.");
        }

        report($"delegate attached as {client.Delegate.GetType().Name}");

        // Held weakly on the Swift side, so the local must outlive the check.
        GC.KeepAlive(recorder);
    }

    /// <summary>Round-trips a value out of Swift, exercising the return path of the bridge.</summary>
    private static void SessionStateIsQueryable(Report report)
    {
        using var client = new AMSClient();

        if (client.IsConnected())
        {
            throw new InvalidOperationException("a client that never connected reports connected.");
        }

        var streamId = client.GetStreamId("smoke-test-stream");
        report($"IsConnected=false, GetStreamId returned '{streamId}'");
    }

    /// <summary>Minimal delegate implementation; the smoke test never triggers a callback.</summary>
    private sealed class RecordingDelegate : AMSClientDelegate
    {
        public override void ClientHasError(string message) =>
            Console.WriteLine($"    delegate received error: {message}");
    }
}
