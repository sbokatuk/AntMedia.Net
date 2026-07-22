namespace AntMedia.Net.PackageTests;

/// <summary>
/// Asserts that the binding assembly inside each package actually exposes the SDK's API.
/// A binding that fails to generate still compiles and packs cleanly — it just produces an
/// almost-empty assembly — so the package layout alone is not enough to prove the build worked.
/// </summary>
public class BindingApiTests
{
    /// <summary>Types the Android binding must expose for the SDK to be usable at all.</summary>
    private static readonly string[] AndroidCoreTypes =
    [
        "AntMedia.WebRTC.Api.IWebRTCClient",
        "AntMedia.WebRTC.Api.IWebRTCListener",
        "AntMedia.WebRTC.Api.DefaultWebRTCListener",
        "AntMedia.WebRTC.Api.WebRTCClientBuilder",
        "AntMedia.WebRTC.Core.WebRTCClient",
        // org.webrtc is vendored inside the same .aar, and the renderer views are what a MAUI app
        // actually puts on screen, so their absence would make the package useless in practice.
        "Org.Webrtc.SurfaceViewRenderer",
        "Org.Webrtc.VideoTrack",
        "Org.Webrtc.PeerConnection",
    ];

    /// <summary>Types the iOS binding must expose. All come from our @objc facade.</summary>
    private static readonly string[] IosCoreTypes =
    [
        "AntMedia.Net.iOS.AMSClient",
        "AntMedia.Net.iOS.AMSClientDelegate",
        "AntMedia.Net.iOS.IAMSClientDelegate",
        "AntMedia.Net.iOS.AMSStreamInformation",
    ];

    private static AssemblyApi OpenBinding(string packageId, string assemblyName, string tfm)
    {
        using var package = Packages.OpenPackage(packageId);
        var assembly = Packages.ReadEntry(package, $"lib/{tfm}/{assemblyName}.dll");
        return new AssemblyApi(assembly);
    }

    [Theory]
    [MemberData(nameof(Packages.AndroidFrameworks), MemberType = typeof(Packages))]
    public void Android_binding_exposes_the_core_sdk_types(string tfm)
    {
        using var api = OpenBinding(Packages.Android, "AntMedia.Net.Android", tfm);

        var missing = AndroidCoreTypes.Except(api.PublicTypes).ToList();

        Assert.True(
            missing.Count == 0,
            $"{Packages.Android} ({tfm}) is missing bound types: {string.Join(", ", missing)}. " +
            $"The assembly exposes {api.PublicTypes.Count} public types in total.");
    }

    [Theory]
    [MemberData(nameof(Packages.AndroidFrameworks), MemberType = typeof(Packages))]
    public void Android_binding_is_not_an_empty_shell(string tfm)
    {
        using var api = OpenBinding(Packages.Android, "AntMedia.Net.Android", tfm);

        // Guards a real failure mode: an unpinned Android API level (bare net8.0-android resolving
        // to android21.0) makes the binding generator produce a valid but essentially empty
        // assembly, which still packs and installs fine.
        Assert.True(
            api.PublicTypes.Count >= 200,
            $"{Packages.Android} ({tfm}) exposes only {api.PublicTypes.Count} public types; " +
            "the binding generator likely did not run over the whole .aar.");
    }

    [Theory]
    [MemberData(nameof(Packages.AndroidFrameworks), MemberType = typeof(Packages))]
    public void Android_listener_overload_keeps_its_renamed_member(string tfm)
    {
        using var api = OpenBinding(Packages.Android, "AntMedia.Net.Android", tfm);

        var methods = api.MethodsOf("AntMedia.WebRTC.Api.IWebRTCListener");

        // Transforms/Metadata.xml renames the two-argument onNewVideoTrack overload, because both
        // overloads otherwise generate the same handler field and the binding does not compile.
        // If the transform is dropped the build breaks loudly — but if it is *changed*, consumers
        // silently lose the member they implement, so it is pinned here.
        Assert.Contains("OnNewVideoTrack", methods);
        Assert.Contains("OnNewVideoTrackWithId", methods);
    }

    [SkippableTheory]
    [MemberData(nameof(Packages.IosFrameworks), MemberType = typeof(Packages))]
    public void Ios_binding_exposes_the_facade_types(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        using var api = OpenBinding(Packages.IOS, "AntMedia.Net.iOS", tfm);

        var missing = IosCoreTypes.Except(api.PublicTypes).ToList();

        Assert.True(
            missing.Count == 0,
            $"{Packages.IOS} ({tfm}) is missing bound types: {string.Join(", ", missing)}. " +
            $"The assembly exposes {api.PublicTypes.Count} public types in total.");
    }

    [SkippableTheory]
    [MemberData(nameof(Packages.IosFrameworks), MemberType = typeof(Packages))]
    public void Ios_client_exposes_the_session_entry_points(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        using var api = OpenBinding(Packages.IOS, "AntMedia.Net.iOS", tfm);

        var methods = api.MethodsOf("AntMedia.Net.iOS.AMSClient");

        // These exist only because native/ios/Facade is compiled into the framework. Against the
        // xcframework Ant Media ships, AMSClient would not exist at all and the binding would be
        // an empty shell — this is the assertion that proves the facade survived the native build.
        foreach (var member in new[]
                 {
                     "SetWebSocketServerUrl", "InitPeerConnection", "Publish", "Play", "Stop",
                     "SetLocalView", "SetRemoteView", "IsConnected",
                 })
        {
            Assert.Contains(member, methods);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Packages.IosFrameworks), MemberType = typeof(Packages))]
    public void Ios_delegate_exposes_the_stream_lifecycle_callbacks(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        using var api = OpenBinding(Packages.IOS, "AntMedia.Net.iOS", tfm);

        var methods = api.MethodsOf("AntMedia.Net.iOS.AMSClientDelegate");

        foreach (var member in new[]
                 {
                     "ClientDidConnect", "PublishStarted", "PlayStarted", "TrackAdded",
                 })
        {
            Assert.Contains(member, methods);
        }
    }
}
