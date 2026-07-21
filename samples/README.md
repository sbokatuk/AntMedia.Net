# AntMedia.Net.Sample

A MAUI app that publishes and plays a WebRTC stream through an Ant Media Server.

## Running it

You need a reachable Ant Media Server. Enter its websocket url and a stream id in the app:

```
wss://your-server:5443/WebRTCAppEE/websocket
```

Tap **Publish** to send the device's camera and microphone, or **Play** to view a stream someone
else is publishing. The panel at the bottom shows the SDK's callbacks as they arrive.

The sample consumes the packed `AntMedia.Net` metapackage from `../../artifacts`, so pack first:

```sh
./build/BuildNugets.sh                                    # from the repository root
dotnet build samples/AntMedia.Net.Sample -f net9.0-android35.0
dotnet build samples/AntMedia.Net.Sample -f net9.0-ios18.0
```

Pass `-p:AntMediaPackageVersion=<version>` to build against a specific packed version rather than
the default from `Directory.Build.props`.

## What is worth reading

The two SDKs do not look alike — Android has a builder plus a listener interface, iOS has our
`@objc` facade plus a delegate — so the sample puts a single
[`IStreamingSession`](AntMedia.Net.Sample/Streaming/IStreamingSession.cs) in front of them and
keeps one small implementation per platform:

| | Android | iOS |
| --- | --- | --- |
| Session | [`Platforms/Android/StreamingSession.cs`](AntMedia.Net.Sample/Platforms/Android/StreamingSession.cs) | [`Platforms/iOS/StreamingSession.cs`](AntMedia.Net.Sample/Platforms/iOS/StreamingSession.cs) |
| Video view | `org.webrtc.SurfaceViewRenderer` | a plain `UIView` |
| Callbacks | `DefaultWebRTCListener` subclass | `AMSClientDelegate` subclass |

[`VideoSurface`](AntMedia.Net.Sample/Streaming/VideoSurface.cs) is a MAUI view whose handler
creates whichever native view the platform's SDK renders into. Both SDKs want the native view
itself rather than a container, which is why it is a custom view rather than a wrapped
`ContentView`.

Two things that are easy to get wrong and are commented in the code: the iOS delegate is held
**weakly** by the client, so it has to be kept in a field; and the Android renderer holds an EGL
context that must be released when the page goes away.
