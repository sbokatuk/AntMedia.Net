# AntMedia.Net

[![NuGet](https://img.shields.io/nuget/v/AntMedia.Net?label=nuget)](https://www.nuget.org/packages/AntMedia.Net)
[![release](https://github.com/sbokatuk/AntMedia.Net/actions/workflows/release.yml/badge.svg)](https://github.com/sbokatuk/AntMedia.Net/actions/workflows/release.yml)
[![Targets: net8.0 | net9.0 | net10.0](https://img.shields.io/badge/targets-net8.0%20%7C%20net9.0%20%7C%20net10.0-512BD4)](#packages)
[![Platforms: Android | iOS | Mac Catalyst](https://img.shields.io/badge/platforms-Android%20%7C%20iOS%20%7C%20Mac%20Catalyst-blue)](#packages)
[![Licence: MIT](https://img.shields.io/badge/licence-MIT-green)](#licence)

.NET bindings for the [Ant Media][antmedia] WebRTC SDKs, with one API across Android, iOS and
Mac Catalyst.
Publish and play WebRTC streams through an Ant Media Server from C#, in .NET MAUI or plain
.NET for Android / iOS.

```sh
dotnet add package AntMedia.Net.Maui    # MAUI apps: adds the video view
dotnet add package AntMedia.Net         # everything else
```

```csharp
var client = new AntMediaOptions
{
    ServerUrl = "wss://your-server:5443/WebRTCAppEE/websocket",
}.CreateClient();

client.SetLocalView(LocalView);          // an <antmedia:AntMediaVideoView /> from your XAML
client.PublishStarted += (_, e) => Console.WriteLine($"live: {e.StreamId}");

await client.PublishAsync("my-stream");  // completes when the server confirms
```

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
`AntMedia.Net.Mac.*` (Mac Catalyst).

## Why there is a cross-platform layer

The two SDKs look nothing like each other: Android has a builder and a 40-method Java listener
interface, iOS has a Swift client reached through an Objective-C facade and a delegate. Written
against the bindings directly, an app targeting both needs roughly 300 lines of adapter before it
can publish a stream. `AntMedia.Net` is that adapter, so you do not write it.

The sample is the evidence: [`samples/AntMedia.Net.Sample`](samples) publishes and plays with no
per-platform code at all.

## What is bound

**Android** binds [WebRTC-Android-SDK][android-sdk] whole. One `.aar` carries both
`io.antmedia.webrtcandroidframework` and `org.webrtc`, so the full native surface is available,
documented with the SDK's own javadoc.

**iOS** binds [WebRTC-iOS-SDK][ios-sdk] through an `@objc` facade this repository maintains. The
upstream SDK is Swift and exposes almost nothing to Objective-C, so there is nothing for a .NET
binding to attach to; [`native/ios/Facade`](native/ios/Facade) re-exposes the client API and is
compiled into the framework. See [docs/BUILD.md](docs/BUILD.md) for what that leaves out.

**Mac Catalyst** binds the same Swift SDK, from the same commit, through the same facade — but
against a different libwebrtc. Ant Media's own is a fork (it adds `RTCAudioDeviceModule`) published
for iOS only, so `AntMedia.Net.Mac` links a stock community build that ships a `maccatalyst` slice
and compiles a small shim beside the facade to supply the API their Swift code expects. iOS and
Android are untouched and keep using Ant Media's own libraries. Apple Silicon only. See
[docs/BUILD.md](docs/BUILD.md).

## Building

See [docs/BUILD.md](docs/BUILD.md). In short:

```sh
./native/android/fetch-android.sh     # builds the .aar from source (JDK 17 + Android SDK)
./native/ios/fetch-ios.sh             # builds the xcframework with the facade (macOS + Xcode)
./native/mac/fetch-mac.sh             # the same, for Mac Catalyst (macOS + Xcode, Apple Silicon)
./build/BuildNugets.sh                # packs everything into ./artifacts
```

## Licence

The binding and client code is MIT. The bundled Ant Media SDKs are MIT, and the
`WebRTC.xcframework` in the iOS package is Ant Media's build of libwebrtc, under its own BSD
licence.

[antmedia]: https://antmedia.io/
[android-sdk]: https://github.com/ant-media/WebRTC-Android-SDK
[ios-sdk]: https://github.com/ant-media/WebRTC-iOS-SDK
