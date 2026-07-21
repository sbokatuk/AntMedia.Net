# AntMedia.Net

.NET bindings for the [Ant Media][antmedia] WebRTC SDKs, for .NET MAUI and .NET for
Android / iOS apps. Publish and play WebRTC streams through an Ant Media Server from C#.

| Package | Platforms | Target frameworks |
| --- | --- | --- |
| `AntMedia.Net` | Android, iOS | net8.0, net9.0, net10.0 |
| `AntMedia.Net.Android` | Android | `net8.0-android34.0`, `net9.0-android35.0`, `net10.0-android36.0` |
| `AntMedia.Net.iOS` | iOS | `net8.0-ios18.0`, `net9.0-ios18.0`, `net10.0-ios26.0` |

```sh
dotnet add package AntMedia.Net
```

`AntMedia.Net` is a metapackage: it pulls in the right platform binding for whichever target
framework you build. Reference a platform package directly if your project only targets one.

## What is bound

**Android** binds [WebRTC-Android-SDK][android-sdk] whole. One `.aar` carries both
`io.antmedia.webrtcandroidframework` and `org.webrtc`, so the full native API surface — including
`IWebRTCClient`, the builder configuration and the `org.webrtc` renderer views — is available.

**iOS** binds [WebRTC-iOS-SDK][ios-sdk] through an `@objc` facade this repository maintains. The
upstream SDK is Swift and exposes essentially nothing to Objective-C, so there is nothing for a
.NET binding to attach to; [`native/ios/Facade`](native/ios/Facade) re-exposes the client API as
`@objc` types (`AMSClient`, `AMSClientDelegate`, `AMSMode`, `AMSStreamInformation`) and is
compiled into the framework. That facade is the iOS API surface — see [docs/BUILD.md](docs/BUILD.md)
for why, and for what is deliberately left out of it.

**Mac Catalyst is not supported.** Neither of Ant Media's iOS xcframeworks ships a Catalyst slice.

## Sample

[`samples/AntMedia.Net.Sample`](samples) is a MAUI app that publishes and plays a stream against
an Ant Media Server of your choosing.

## Building

See [docs/BUILD.md](docs/BUILD.md). In short:

```sh
./native/android/fetch-android.sh     # builds the .aar from source (JDK 17 + Android SDK)
./native/ios/fetch-ios.sh             # builds the xcframework with the facade (macOS + Xcode)
./build/BuildNugets.sh                # packs everything into ./artifacts
```

## Licence

The binding code is MIT. The bundled Ant Media SDKs are MIT, and the `WebRTC.xcframework` shipped
in the iOS package is Ant Media's build of libwebrtc, which carries its own BSD licence.

[antmedia]: https://antmedia.io/
[android-sdk]: https://github.com/ant-media/WebRTC-Android-SDK
[ios-sdk]: https://github.com/ant-media/WebRTC-iOS-SDK
