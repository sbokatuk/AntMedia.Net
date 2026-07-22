using Android.Content;
using AntMedia.WebRTC.Api;
using Org.Webrtc;

namespace AntMedia.Net.Android.DeviceTests;

/// <summary>
/// Offline checks that the packaged binding actually works on a device.
///
/// They deliberately stop short of connecting to a server: a real publish/play round-trip needs a
/// running Ant Media Server, which CI does not have. What is proven here is everything that can
/// break between "the package restored" and "the SDK is usable" — that the native libraries load,
/// that the bound types resolve at runtime, and that calls into them dispatch correctly. Those
/// are the failures a desktop package test cannot see.
/// </summary>
public static class SmokeTests
{
    public delegate void Report(string message);

    public static IReadOnlyList<(string Name, Action<Context, Report> Run)> All =>
    [
        ("native library loads", NativeLibraryLoads),
        ("peer connection factory is usable", PeerConnectionFactoryIsUsable),
        ("camera enumeration works", CameraEnumerationWorks),
        ("ant media types resolve", AntMediaTypesResolve),
    ];

    /// <summary>
    /// The single most valuable check: PeerConnectionFactory.Initialize is what dlopens
    /// libjingle_peerconnection.so. If the .aar shipped without the jniLibs for this ABI, or the
    /// package was assembled wrongly, this throws UnsatisfiedLinkError and nothing else matters.
    /// </summary>
    private static void NativeLibraryLoads(Context context, Report report)
    {
        var options = PeerConnectionFactory.InitializationOptions
            .InvokeBuilder(context)
            .CreateInitializationOptions();

        PeerConnectionFactory.Initialize(options);

        report("libjingle_peerconnection loaded");
    }

    /// <summary>
    /// Creating a factory crosses the JNI boundary in both directions and allocates native
    /// objects, so it proves the binding dispatches rather than merely linking.
    /// </summary>
    private static void PeerConnectionFactoryIsUsable(Context context, Report report)
    {
        var factory = PeerConnectionFactory.InvokeBuilder().CreatePeerConnectionFactory();

        if (factory is null)
        {
            throw new InvalidOperationException("CreatePeerConnectionFactory returned null.");
        }

        try
        {
            report($"created {factory.Class.SimpleName}");
        }
        finally
        {
            factory.Dispose();
        }
    }

    /// <summary>
    /// Exercises a bound API that returns a Java array. An emulator image always has at least one
    /// camera, but the assertion is deliberately only that the call round-trips: the value matters
    /// less than the marshalling working.
    /// </summary>
    private static void CameraEnumerationWorks(Context context, Report report)
    {
        var enumerator = new Camera2Enumerator(context);
        var devices = enumerator.GetDeviceNames();

        report($"enumerated {devices?.Length ?? 0} camera(s)");
    }

    /// <summary>
    /// The org.webrtc layer above is vendored inside the same .aar as Ant Media's own code, so it
    /// could in principle work while the io.antmedia classes were missing. Touching the Ant Media
    /// types resolves them from the dex at runtime and proves the whole .aar shipped.
    /// </summary>
    private static void AntMediaTypesResolve(Context context, Report report)
    {
        var builder = new WebRTCClientBuilder();
        var listener = new DefaultWebRTCListener();

        // Class is resolved lazily by the runtime, so reading it is what forces the load.
        report($"{builder.Class.Name} and {listener.Class.Name} resolved");
    }
}
