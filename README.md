# AntMedia.Net

[![NuGet](https://img.shields.io/nuget/v/AntMedia.Net?label=nuget)](https://www.nuget.org/packages/AntMedia.Net)
[![release](https://github.com/sbokatuk/AntMedia.Net/actions/workflows/release.yml/badge.svg)](https://github.com/sbokatuk/AntMedia.Net/actions/workflows/release.yml)
[![Targets: net8.0 | net9.0 | net10.0](https://img.shields.io/badge/targets-net8.0%20%7C%20net9.0%20%7C%20net10.0-512BD4)](#packages)
[![Platforms: Android | iOS | Mac Catalyst](https://img.shields.io/badge/platforms-Android%20%7C%20iOS%20%7C%20Mac%20Catalyst-blue)](#packages)
[![Licence: MIT](https://img.shields.io/badge/licence-MIT-green)](https://github.com/sbokatuk/AntMedia.Net/blob/main/LICENSE)

.NET bindings for the [Ant Media][antmedia] WebRTC SDKs, with one API across Android, iOS and
Mac Catalyst.
Publish and play WebRTC streams through an Ant Media Server from C#, in .NET MAUI or plain
.NET for Android / iOS.

```sh
dotnet add package AntMedia.Net.Maui    # MAUI apps: adds the video view
dotnet add package AntMedia.Net         # everything else
```

Three pieces of wiring, then the client. First register the handler — without this the video
view renders nothing:

```csharp
// MauiProgram.cs
builder.UseMauiApp<App>().UseAntMedia();
```

Declare the namespace and drop a view into your XAML:

```xml
<ContentPage xmlns:antmedia="http://schemas.sbokatuk.com/antmedia/maui" ...>
    <antmedia:AntMediaVideoView x:Name="LocalView" />
```

Then create the client **once the page has appeared** — in `OnAppearing`, not the constructor:
on Android the SDK needs the current `Activity`, which does not exist any earlier.
`CreateClient()` and `SetLocalView(AntMediaVideoView)` come from `AntMedia.Net.Maui`:

```csharp
var client = new AntMediaOptions
{
    ServerUrl = "wss://your-server:5443/WebRTCAppEE/websocket",
}.CreateClient();

client.SetLocalView(LocalView);          // the <antmedia:AntMediaVideoView /> from your XAML
client.PublishStarted += (_, e) => Console.WriteLine($"live: {e.StreamId}");

await client.PublishAsync("my-stream");  // completes when the server confirms
```

Playing is the same shape: `SetRemoteView` and `PlayAsync`. One client runs one session —
publish *or* play — so a video-call page uses two clients. Events arrive on SDK threads;
marshal before touching UI (`MainThread.BeginInvokeOnMainThread`).

## Without MAUI

`AntMedia.Net.Maui` only adds the video view, the handler and the Android `Activity` lookup. A
plain .NET for Android or .NET for iOS app needs none of that — reference `AntMedia.Net` and use
the same `AntMediaClient`, with the platform's own view type instead of `AntMediaVideoView`:

```sh
dotnet add package AntMedia.Net
```

**Android** — the SDK renders into `org.webrtc`'s `SurfaceViewRenderer`, and needs the `Activity`:

```csharp
using AntMedia.Net;
using Android.App;
using Android.OS;
using Android.Util;
using Org.Webrtc;

[Activity(Label = "Publisher", MainLauncher = true)]
public class MainActivity : Activity
{
    private AntMediaClient? _client;
    private SurfaceViewRenderer? _preview;

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // org.webrtc's own view, straight from the Android binding.
        _preview = new SurfaceViewRenderer(this);
        SetContentView(_preview);

        var options = new AntMediaOptions
        {
            ServerUrl = "wss://your-server:5443/WebRTCAppEE/websocket",
        };

        // The Activity is the one thing the Android SDK cannot work out for itself: it needs one
        // for the camera and the renderers. AntMedia.Net.Maui is what supplies it in a MAUI app.
        _client = new AntMediaClient(options, this);
        _client.SetLocalRenderer(_preview);

        _client.PublishStarted += (_, e) => Log.Info("app", $"live: {e.StreamId}");
        _client.Error += (_, e) => Log.Error("app", e.Message);

        await _client.PublishAsync("my-stream");
    }

    protected override void OnDestroy()
    {
        // Releases the camera and the renderer's EGL context; managed collection alone does not.
        _client?.Dispose();
        base.OnDestroy();
    }
}
```

**iOS and Mac Catalyst** — one file for both, because the facade takes an ordinary `UIView` and
adds its own renderer inside it. Note there is no `Activity` parameter here:

```csharp
using System;
using AntMedia.Net;
using UIKit;

public class StreamViewController : UIViewController
{
    private AntMediaClient? _client;

    public override async void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);

        var options = new AntMediaOptions
        {
            ServerUrl = "wss://your-server:5443/WebRTCAppEE/websocket",
        };

        _client = new AntMediaClient(options);

        // Any UIView will do — the SDK adds its own renderer as a subview.
        _client.SetLocalView(View!);

        _client.PublishStarted += (_, e) => Console.WriteLine($"live: {e.StreamId}");
        _client.Error += (_, e) => Console.WriteLine($"error: {e.Message}");

        await _client.PublishAsync("my-stream");
    }

    public override void ViewDidDisappear(bool animated)
    {
        _client?.Dispose();
        _client = null;
        base.ViewDidDisappear(animated);
    }
}
```

Playing instead of publishing is `SetRemoteView`/`SetRemoteRenderer` and `PlayAsync`. Both
snippets create the client once the view exists rather than in a constructor — on Android the
`Activity` is not usable before `OnCreate`, and on Apple the view has no window before
`ViewDidAppear`.

`AntMedia.Net.Maui` has no Windows target, because the Ant Media SDKs have none. The default
MAUI template also targets `net*-windows` on Windows machines, so condition the reference:

```xml
<PackageReference Include="AntMedia.Net.Maui" Version="..."
    Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) != 'windows'" />
```

Capture permissions are the app's own responsibility either way: `CAMERA` and `RECORD_AUDIO` in
`AndroidManifest.xml`, `NSCameraUsageDescription` and `NSMicrophoneUsageDescription` in
`Info.plist`, plus `com.apple.security.device.camera` and `.audio-input` entitlements for a
sandboxed Mac Catalyst app.

## Packages

| Package | What it is | Target frameworks |
| --- | --- | --- |
| `AntMedia.Net.Maui` | MAUI video view, handlers, and the Android `Activity` plumbing | net8.0, net9.0, net10.0 (android + ios + maccatalyst) |
| `AntMedia.Net` | The cross-platform client: `IAntMediaClient`, options, events, async | net8.0, net9.0, net10.0 (android + ios + maccatalyst) |
| `AntMedia.Net.Android` | The raw binding to the Ant Media Android SDK | `net8.0-android34.0`, `net9.0-android35.0`, `net10.0-android36.0` |
| `AntMedia.Net.iOS` | The raw binding to the Ant Media iOS SDK | `net8.0-ios18.0`, `net9.0-ios18.0`, `net10.0-ios26.0` |
| `AntMedia.Net.Mac` | The same binding, built for Mac Catalyst (Apple Silicon) | `net8.0-maccatalyst18.0`, `net9.0-maccatalyst18.0`, `net10.0-maccatalyst26.0` |

Each package pulls in the one below it, so a single reference is enough. Drop to a platform
binding directly when you need something the cross-platform API does not expose — the full SDK
surface is there under `AntMedia.WebRTC.*` (Android), `AntMedia.Net.iOS.*` (iOS) and
`AntMedia.Net.Mac.*` (Mac Catalyst). The two Apple bindings expose the same types under
different namespaces, so a file shared between iOS and Mac Catalyst raw-binding code needs one
alias:

```csharp
#if MACCATALYST
using AMS = AntMedia.Net.Mac;
#else
using AMS = AntMedia.Net.iOS;
#endif
```

## Why there is a cross-platform layer

The two SDKs look nothing like each other: Android has a builder and a 40-method Java listener
interface, iOS has a Swift client reached through an Objective-C facade and a delegate. Written
against the bindings directly, an app targeting both needs roughly 300 lines of adapter before it
can publish a stream. `AntMedia.Net` is that adapter, so you do not write it.

The sample is the evidence:
[`samples/AntMedia.Net.Sample`](https://github.com/sbokatuk/AntMedia.Net/tree/main/samples)
publishes and plays with no per-platform code at all.

## What is bound

**Android** binds [WebRTC-Android-SDK][android-sdk] whole. One `.aar` carries both
`io.antmedia.webrtcandroidframework` and `org.webrtc`, so the full native surface is available,
documented with the SDK's own javadoc.

**iOS** binds [WebRTC-iOS-SDK][ios-sdk] through an `@objc` facade this repository maintains. The
upstream SDK is Swift and exposes almost nothing to Objective-C, so there is nothing for a .NET
binding to attach to;
[`native/ios/Facade`](https://github.com/sbokatuk/AntMedia.Net/tree/main/native/ios/Facade)
re-exposes the client API and is compiled into the framework. See
[docs/BUILD.md](https://github.com/sbokatuk/AntMedia.Net/blob/main/docs/BUILD.md) for what that
leaves out.

**Mac Catalyst** binds the same Swift SDK, from the same commit, through the same facade — but
against a different libwebrtc. Ant Media's own is a fork (it adds `RTCAudioDeviceModule`) published
for iOS only, so `AntMedia.Net.Mac` links a stock community build that ships a `maccatalyst` slice
and compiles a small shim beside the facade to supply the API their Swift code expects. iOS and
Android are untouched and keep using Ant Media's own libraries. Apple Silicon only. See
[docs/BUILD.md](https://github.com/sbokatuk/AntMedia.Net/blob/main/docs/BUILD.md).

## Building

See [docs/BUILD.md](https://github.com/sbokatuk/AntMedia.Net/blob/main/docs/BUILD.md). In short:

```sh
./native/android/fetch-android.sh     # builds the .aar from source (JDK 17 + Android SDK)
./native/ios/fetch-ios.sh             # builds the xcframework with the facade (macOS + Xcode)
./native/mac/fetch-mac.sh             # the same, for Mac Catalyst (macOS + Xcode, Apple Silicon)
./build/BuildNugets.sh                # packs everything into ./artifacts
```

## Licence

The binding and client code is MIT — see
[LICENSE](https://github.com/sbokatuk/AntMedia.Net/blob/main/LICENSE). The bundled Ant Media
SDKs are MIT, and the `WebRTC.xcframework` in the iOS package is Ant Media's build of libwebrtc,
under its own BSD licence. The Mac Catalyst package carries a stock community build of libwebrtc
instead, under the same BSD licence. The licence and notice texts for everything redistributed
ship inside each package as `THIRD-PARTY-NOTICES.txt`.

[antmedia]: https://antmedia.io/
[android-sdk]: https://github.com/ant-media/WebRTC-Android-SDK
[ios-sdk]: https://github.com/ant-media/WebRTC-iOS-SDK
