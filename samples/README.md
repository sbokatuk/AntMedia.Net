# AntMedia.Net.Sample

A MAUI app that publishes and plays a WebRTC stream through an Ant Media Server — with no
per-platform code at all, which is the point: the adapter layer the app would otherwise need is
what the `AntMedia.Net` packages are.

## Running it

You need a reachable Ant Media Server. Enter its websocket url and a stream id in the app:

```
wss://your-server:5443/WebRTCAppEE/websocket
```

Tap **Publish** to send the device's camera and microphone, or **Play** to view a stream someone
else is publishing. The panel at the bottom shows the SDK's callbacks as they arrive.

The sample consumes the packed `AntMedia.Net.Maui` package from `../../artifacts`, so pack first:

```sh
./build/BuildNugets.sh                                    # from the repository root
dotnet build samples/AntMedia.Net.Sample -f net9.0-android35.0
dotnet build samples/AntMedia.Net.Sample -f net9.0-ios18.0
```

Pass `-p:AntMediaPackageVersion=<version>` to build against a specific packed version rather than
the default from `Directory.Build.props`.

## What is worth reading

The whole app is one page,
[`MainPage.xaml.cs`](AntMedia.Net.Sample/MainPage.xaml.cs) — publish, play, stop, permissions,
errors and cleanup in ~180 lines with no `#if` anywhere. The pieces worth a look:

- **Registration** — [`MauiProgram.cs`](AntMedia.Net.Sample/MauiProgram.cs) calls
  `.UseAntMedia()`; without it the video view has no handler and renders nothing.
- **Lazy client** — the client is created on first use, not in the page constructor, because on
  Android the SDK needs the current `Activity` and the video views need their handlers. It is
  rebuilt when the server url changes, so a corrected typo actually takes effect.
- **Awaitable start** — `PublishAsync`/`PlayAsync` complete when the server confirms, so failure
  handling is one `catch (AntMediaException)` with a stable `Code`, not a callback web.
- **Threading** — SDK events arrive on their own threads; every UI touch hops back through
  `MainThread.BeginInvokeOnMainThread`.
- **Session end** — `PublishFinished`, `PlayFinished` and `Disconnected` re-enable the buttons,
  so a server-side drop leaves the UI usable, not stuck.
- **Cleanup** — the client is disposed when the page's handler goes away; the Android renderer
  holds an EGL context and the camera that managed collection alone does not reclaim.
- **The views** — [`MainPage.xaml`](AntMedia.Net.Sample/MainPage.xaml) shows `Scaling` and
  `Mirror` on `AntMediaVideoView`, and the comment there covers the Android `SurfaceView`
  z-order rule for overlapping layouts.

The per-platform manifests are worth copying from too:
[`AndroidManifest.xml`](AntMedia.Net.Sample/Platforms/Android/AndroidManifest.xml) (permissions
with the Bluetooth caveat explained), `Info.plist` (usage descriptions, background modes) and the
Mac Catalyst `Entitlements.plist` (sandbox camera/microphone entitlements — without them there is
no prompt and no video).
