# Building AntMedia.Net

Everything here is driven by the pins in [`Directory.Build.props`](../Directory.Build.props) and
run by the scripts in `build/` and `native/`. CI runs exactly these scripts, so a green local run
means the same thing a green pipeline does.

## Layout

```
Directory.Build.props        native SDK pins, target frameworks, shared package metadata
global.json                  pins the .NET 9 SDK (the "net9 band")
NuGet.config                 nuget.org + ./artifacts, so tests consume the packed packages
build/
  pins.sh                    the only parser of Directory.Build.props for shell callers
  BuildNugets.sh             two-pass pack + merge -> ./artifacts
  merge-packages.py          combines the two passes into one package per id
native/
  android/fetch-android.sh   builds webrtc-android-framework-release.aar from source
  android/overlay/           files copied over the upstream checkout before building
  ios/fetch-ios.sh           builds WebRTCiOSSDK.xcframework with our @objc facade
  ios/Facade/                the @objc facade compiled into the iOS framework
  ios/add-facade.rb          adds the facade to the upstream Xcode target
  build/                     upstream checkouts and intermediates (git-ignored)
src/                         the binding projects and the metapackage
tests/                       package validation + on-device smoke tests
samples/                     the MAUI sample app
```

## Why two passes

No single .NET SDK can build .NET 8, 9 and 10 for a given platform — each SDK's workload carries
the current target framework and the previous one only. Verified on this repository:

| | SDK 9 band | SDK 10 band |
| --- | --- | --- |
| Android | `net8.0-android34.0`, `net9.0-android35.0` | `net10.0-android36.0` |
| iOS | `net8.0-ios18.0`, `net9.0-ios18.0` | `net10.0-ios26.0` |

So `BuildNugets.sh` packs each project twice — once under the SDK `global.json` pins, once from a
scratch directory whose own `global.json` selects the .NET 10 SDK — and `merge-packages.py`
copies the missing `lib/<tfm>` trees from the second package into the first, adding the matching
nuspec dependency groups.

The platform version in each target framework is pinned deliberately. Bare `net8.0-android`
resolves to `android21.0`, which produces a binding assembly with no `.aar` payload — it compiles,
packs and installs, and fails only at runtime.

## Native SDKs

### Android

`native/android/fetch-android.sh` clones [WebRTC-Android-SDK][android-sdk] at the pinned tag and
runs `:webrtc-android-framework:assembleRelease`. The resulting `.aar` is the entire Android
surface: the framework vendors both `io.antmedia.*` and `org.webrtc` Java sources plus `jniLibs`
for `arm64-v8a`, `armeabi-v7a`, `x86` and `x86_64`. The `x86_64` slice is what lets the smoke test
run on a CI emulator.

Requires a JDK 17 and an Android SDK (`ANDROID_HOME` or `ANDROID_SDK_ROOT`).

```sh
./native/android/fetch-android.sh              # the pinned tag
./native/android/fetch-android.sh v2.17.1      # some other tag
```

### iOS

`native/ios/fetch-ios.sh` clones [WebRTC-iOS-SDK][ios-sdk] at the pinned commit, compiles our
facade into the `WebRTCiOSSDK` target, and builds an xcframework for device and simulator.

**The checked-in xcframework upstream ships cannot be bound.** WebRTCiOSSDK is written in Swift
and exposes almost nothing to Objective-C: the generated `WebRTCiOSSDK-Swift.h` declares exactly
two `@objc` classes, `AntMediaClient` (`init` plus two delegate callbacks) and `Config` (`init`).
`setOptions`, `publish`, `play`, `AntMediaClientDelegate`, `AntMediaClientMode` — all Swift-only,
so Objective Sharpie sees nothing worth binding. [`native/ios/Facade/AntMediaNetFacade.swift`](../native/ios/Facade/AntMediaNetFacade.swift)
re-exposes that API as `@objc` types (`AMSClient`, `AMSClientDelegate`, `AMSMode`,
`AMSStreamInformation`), and it has to be compiled *into* the framework to appear in the generated
header — hence the rebuild. The script fails loudly if `AMSClient` is missing from the built
header.

Ant Media's own `WebRTC.xcframework` (their build of libwebrtc, ~26 MB) is plain Objective-C and
is copied through as-is.

Requires macOS with Xcode and the `xcodeproj` Ruby gem (installed by the script if missing).

```sh
./native/ios/fetch-ios.sh                      # the pinned commit
./native/ios/fetch-ios.sh <sha>                # some other commit
```

Both scripts are pin-keyed and idempotent, which is what makes the CI caches safe: the cache key
is the pin plus a hash of the script inputs, so changing the facade rebuilds the framework.

### Mac Catalyst is not supported

Neither `WebRTCiOSSDK.xcframework` nor `WebRTC.xcframework` has a Mac Catalyst slice — both carry
`ios-arm64` and `ios-arm64_x86_64-simulator` only — and `Package.swift` declares `.iOS(.v12)`.
Shipping Catalyst would mean rebuilding libwebrtc for the Catalyst destination, which is a
separate project. The `Bindings/AntMedia.Net.Mac*` scaffolding predates this and is not built or
published.

## Packing

```sh
./build/BuildNugets.sh                          # version from Directory.Build.props
./build/BuildNugets.sh 2.17.2-beta.4            # explicit version
./build/BuildNugets.sh 2.17.2-beta.4 android    # Android only (what the Linux CI runner does)
./build/BuildNugets.sh 2.17.2-beta.4 apple      # iOS + metapackage (the macOS runner)
```

Output lands in `./artifacts`, which `NuGet.config` exposes as a package source so the tests and
the sample app resolve the packages that were just built rather than whatever is on nuget.org.

## Testing

```sh
dotnet test tests/AntMedia.Net.PackageTests                       # asserts the packed .nupkg shape
./.github/scripts/run-android-device-tests.sh <version>           # needs a booted emulator
./.github/scripts/run-ios-device-tests.sh <version>               # boots its own simulator
```

The device tests are offline smoke tests: they prove the native library loads and the bound API is
callable, not that streaming works. Verifying a real publish/play round-trip needs a running Ant
Media Server and is out of scope for CI.

## CI

| Workflow | Trigger | What it does |
| --- | --- | --- |
| [`build.yml`](../.github/workflows/build.yml) | called by the other two | Builds natives, packs, validates, runs both smoke tests |
| [`pr.yml`](../.github/workflows/pr.yml) | pull requests | Builds `<version>-beta.<pr>.<run>` and publishes it |
| [`release.yml`](../.github/workflows/release.yml) | `v*` tags | Publishes the tagged version and creates the GitHub release |

`build.yml` splits packing across runners: Android needs only a JDK and the Android SDK so it
packs on `ubuntu-latest`, while iOS needs Xcode and packs on `macos-latest`. The two package sets
are merged by the `validate` job, which runs the package tests over the complete set.

Publishing uses nuget.org [trusted publishing][trusted-publishing]: the job requests a GitHub OIDC
token and exchanges it for a short-lived API key, so there is no long-lived key in the repository
secrets. It needs `id-token: write`, a `production` environment matching the nuget.org policy, and
a `NUGET_USER` secret holding the nuget.org account name.

[android-sdk]: https://github.com/ant-media/WebRTC-Android-SDK
[ios-sdk]: https://github.com/ant-media/WebRTC-iOS-SDK
[trusted-publishing]: https://learn.microsoft.com/nuget/nuget-org/trusted-publishing
