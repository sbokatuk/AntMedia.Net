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
  ios/add-facade.py          adds the facade to the upstream Xcode target
  mac/fetch-mac.sh           builds the same, for Mac Catalyst, on a stock libwebrtc
  mac/Shim/                  the fork-only API Ant Media's Swift calls, over stock libwebrtc
  mac/enable-catalyst.py     turns on Mac Catalyst in the upstream Xcode target
  mac/strip-slices.py        keeps the arm64 Catalyst slice and drops the rest
  mac/flatten-frameworks.py  versioned framework layout -> shallow, so NuGet can carry it
  build/                     upstream checkouts and intermediates (git-ignored)
src/                         the binding projects, the cross-platform client and the MAUI package
tests/                       package validation + on-device smoke tests
samples/                     the MAUI sample app
assets/                      the package icon
AntMedia.Net.sln             every project above
```

The solution spans .NET 8/9/10 and three platforms, so no single SDK can build all of it at once —
`dotnet build AntMedia.Net.sln` will fail on whichever band the current SDK does not own. Build
individual projects, or use `build/BuildNugets.sh`, which handles the bands.

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

Two details are load-bearing and easy to undo by accident:

- **The facade classes carry explicit `@objc(AMSClient)` names.** Without them Swift exports the
  class as `_OBJC_CLASS_$__TtC12WebRTCiOSSDK9AMSClient` while the .NET binding links against
  `_OBJC_CLASS_$_AMSClient`, and every consuming app fails at link time. `fetch-ios.sh` checks the
  exported symbols with `nm` and fails the build if the mangled form comes back.
- **Both xcframeworks are dynamic, so they travel outside the assembly rather than inside it.**
  A dynamic framework embedded in a binding assembly never reaches the consuming app's linker,
  which fails with `"_OBJC_CLASS_$_AMSClient", referenced from: <initial-undefines>`. The binding
  projects therefore set `NoBindingEmbedding=true` and keep their `NativeReference` items, which
  writes the frameworks beside the built assembly as a `<assembly>.resources[.zip]` sidecar —
  what a `ProjectReference` consumer links from.

  The *package* ships them differently: one copy under `native/`, re-declared in every consuming
  app by `buildTransitive/<id>.targets`, with the Apple SDK's default per-target-framework
  sidecars stripped at pack time — they would ship the same ~27 MB once per target framework.
  `buildTransitive/` is the part that makes this correct: assets in plain `build/` are hidden
  from *transitive* consumers by NuGet's default `PrivateAssets`, so an app that arrives here
  through `AntMedia.Net.Maui` → `AntMedia.Net` would never import the targets and would fail with
  exactly the error above. See the comment in
  [`AntMedia.Net.iOS.csproj`](../src/AntMedia.Net.iOS/AntMedia.Net.iOS.csproj).

Requires macOS with Xcode. Nothing else is installed: the one fiddly part, adding the facade to
the upstream Xcode target, is done by [`add-facade.py`](../native/ios/add-facade.py) using `plutil`
and Python's `plistlib` rather than Ruby's `xcodeproj` gem — `project.pbxproj` is a property list,
and Xcode reads it back happily in XML form.

```sh
./native/ios/fetch-ios.sh                      # the pinned commit
./native/ios/fetch-ios.sh <sha>                # some other commit
```

Both scripts are pin-keyed and idempotent, which is what makes the CI caches safe: the cache key
is the pin plus a hash of the script inputs, so changing the facade rebuilds the framework.

### Mac Catalyst

`native/mac/fetch-mac.sh` builds the same Ant Media commit as the iOS script, with the same facade,
and stages the result for `AntMedia.Net.Mac`.

```sh
./native/mac/fetch-mac.sh                      # the pinned commit
./native/mac/fetch-mac.sh <sha>                # some other commit
```

What differs is underneath. Ant Media links a **customised** libwebrtc: their `WebRTC.framework`
exports `RTCAudioDeviceModule` and ships `RTCAudioDeviceModule.h`, stock builds have neither, and
they publish theirs for iOS only (`ios-arm64`, `ios-arm64_x86_64-simulator`) without the fork's
source. So no Catalyst slice can be produced from it.

This build therefore links a stock community libwebrtc ([stasel/WebRTC][stasel], which ships
`maccatalyst`) and compiles [`native/mac/Shim`](../native/mac/Shim) beside the facade to supply the
two things their Swift code expects and stock libwebrtc does not have: `RTCAudioDeviceModule`, and
the `peerConnectionFactory(encoderFactory:decoderFactory:audioDeviceModule:)` initialiser. Ant
Media's own source is not patched. The consequence is that **external audio injection is not
available on Mac** — everything else works, including camera capture, publish and play.

Three build settings do the rest, each load-bearing and each with an unhelpful failure:

| Setting | Without it |
| --- | --- |
| `SUPPORTS_MACCATALYST=YES` | `Unable to find a destination matching the provided destination specifier` |
| `IPHONEOS_DEPLOYMENT_TARGET=14.0` | a dozen `'AVCaptureDevice' is only available in Mac Catalyst 14.0 or newer`. Catalyst derives availability from the *iOS* deployment target; raising `MACOSX_DEPLOYMENT_TARGET` does nothing |
| `ARCHS=arm64` | undefined `RTCEAGLVideoView` at link time. Ant Media picks its renderer with `#if arch(arm64)` — Metal on arm64, OpenGL otherwise — and Mac Catalyst has no OpenGL |

The last one is why **`AntMedia.Net.Mac` is Apple Silicon only**. A Catalyst app that builds for
`maccatalyst-x64` fails to link; set `<RuntimeIdentifiers>maccatalyst-arm64</RuntimeIdentifiers>`.

Two post-processing steps run before the frameworks are staged, both in `native/mac`:

- `strip-slices.py` keeps the `maccatalyst` slice and `lipo`s it down to arm64. The stock build
  ships iOS, simulator, macOS and Catalyst slices as one ~100 MB xcframework, and everything but
  the arm64 Catalyst slice is weight nothing here can link against.
- `flatten-frameworks.py` rewrites the macOS versioned framework layout to the shallow one, and
  rewrites the install names to match. A NuGet package cannot carry a symlink, and the versioned
  layout is mostly symlinks: each becomes a real copy, and the consuming app then fails to sign
  with `bundle format is ambiguous (could be app or framework)`.

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
./.github/scripts/run-mac-device-tests.sh <version>               # runs on this Mac (arm64)
```

The Mac Catalyst app shares the iOS one's sources — the two bindings come from one ApiDefinition,
so the checks are the same checks — and its runner is the simplest of the three, because a Catalyst
app *is* a macOS app: nothing to boot, nothing to install.

The device tests are mostly offline: they prove the native libraries load and the bound API is
callable. One check is not — every one of the three apps also publishes to a real Ant Media Server
when one is supplied, and CI runs the community edition as a service container for Android:

```bash
ANTMEDIA_TEST_SERVER="ws://10.0.2.2:5080/LiveApp/websocket" \
  ./.github/scripts/run-android-device-tests.sh <version>
```

Two things about that url are easy to get wrong. `10.0.2.2` is how an Android emulator reaches the
host; and the application is **`LiveApp`**, not `WebRTCAppEE` — the latter is the Enterprise
edition's name and 404s on the community image, which shows up as a publish timeout and an empty
server log rather than as anything pointing at the url.

This check earns its keep. It is what caught the SDK's undeclared gson dependency: the package
restored, built and installed cleanly, then threw `NoClassDefFoundError` on the first publish.
Nothing that stops short of streaming to a server would have found it.

There is no Apple equivalent in CI. GitHub's macOS runners have no Docker, so there is nowhere to
run a server beside them; beyond that an iOS simulator has no camera to publish from, and the
Catalyst runner has no camera and nobody to answer the permission prompt. Both Apple runners honour
`ANTMEDIA_TEST_SERVER` and run the live check when it is set, so the coverage is available locally
— see below.

Two choices in the iOS runner exist for reasons that are not obvious, and reverting either will
cost you an hour of CI time or a confusing failure:

- **CI selects the Xcode that carries the iOS SDK the packages target**, via
  [`select-xcode.sh`](../.github/scripts/select-xcode.sh), and the macOS jobs pin `macos-15`
  rather than `macos-latest`. `net10.0-ios26.0` only builds against an Xcode carrying the iOS 26.0
  SDK — with the image default (26.5) it fails outright:

  ```
  error : This version of .NET for iOS (26.0.11017) requires Xcode 26.0.
          The current version of Xcode is 26.5.
  ```

  The smoke app is therefore built for one of the package's *own* target frameworks, so the test
  proves what actually ships rather than something adjacent to it. The script resolves the Xcode
  by glob (`Xcode_26.0*.app`) rather than a hard-coded path, because the images carry patch
  releases — it is `26.0.1` today — and a hard-coded path goes stale silently when they re-roll.
- **The smoke app and the sample are built Debug.** An iOS Release build trims and AOT-compiles;
  with the ~27 MB WebRTC framework in the app that took 38 minutes of macOS runner time in one
  observed run. Nothing is lost — these apps are never shipped, and trimming behaviour is not what
  either check is testing. Debug still restores, resolves and links the native frameworks.

To run the iOS smoke test locally you may need the same Xcode selected. Rather than changing your
machine's global `xcode-select`, scope it to the one command:

```bash
DEVELOPER_DIR=/Applications/Xcode_26.0.1.app/Contents/Developer ./.github/scripts/run-ios-device-tests.sh <version>
```

## Running the live publish test locally

This is the check worth reproducing by hand, because it is the one that finds runtime problems —
a missing transitive dependency, a signalling mistake — that everything else misses.

**1. Start a server.** Ant Media publish the community edition to Docker Hub; see their
[Docker installation guide][ams-docker] for the image's own options.

```bash
docker run -d --name ams -p 5080:5080 antmedia/community:latest

# The container is up well before the application inside it is listening.
until curl -sf -o /dev/null http://localhost:5080/; do sleep 5; done
```

**2. Know which application you are talking to.** The community edition serves **`LiveApp`**;
`WebRTCAppEE` is the Enterprise edition's application and returns 404 here. Getting this wrong
looks like a publish timeout with nothing at all in the server log. Check what your server has:

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5080/LiveApp/     # 200 on community
```

Ant Media's [WebRTC publishing guide][ams-publish] uses `WebRTCAppEE` throughout, because it is
written for the Enterprise edition — worth remembering when following their samples.

**3. Start an emulator with a camera.** Without one the SDK has no frames to encode and the server
never sees a broadcast start:

```bash
emulator -avd <your-avd> -no-window -no-snapshot -noaudio -no-boot-anim -camera-back emulated &
adb wait-for-device
```

**4. Run it.** `10.0.2.2` is the emulator's route to your host — see Android's
[emulator networking documentation][emulator-networking]:

```bash
./build/BuildNugets.sh 1.0.0-local.1                       # pack what you want to test
ANTMEDIA_TEST_SERVER="ws://10.0.2.2:5080/LiveApp/websocket" \
  ./.github/scripts/run-android-device-tests.sh 1.0.0-local.1 net9.0-android35.0
```

A pass looks like this, and the last line is the contract the runner script checks:

```
PASS live publish
ANTMEDIA_E2E_DONE PASS (5 checks)
```

**5. Confirm from the server, not only from the app.** The app asserting success is one half; the
server having accepted the broadcast is the other:

```bash
docker logs ams 2>&1 | grep -E 'offer|onAddStream|onCreate Success'
```

```
received sdp type is offer e2e073746
onAddStream for stream: e2e073746
onCreate Success for stream: e2e073746
```

**6. Clean up.** `docker rm -f ams`.

### The same, on Mac Catalyst

```bash
ANTMEDIA_TEST_SERVER="ws://localhost:5080/LiveApp/websocket" \
  ./.github/scripts/run-mac-device-tests.sh 1.0.0-local.1 net9.0-maccatalyst18.0
```

Three things differ from the Android run, and each one fails in a way that does not name itself:

- **The app must be launched through LaunchServices**, which is why the runner uses `open` rather
  than exec'ing the binary. macOS only shows the camera and microphone prompts for an app it
  launched; a binary started from a shell inherits the terminal's grants instead. Answer the prompt
  the first time — until you do, the app sits waiting for it.
- **The check asks for permission before it publishes**, deliberately. Without that, an unauthorised
  capture device produces no frames, every signalling step succeeds, and ten seconds later the
  server reports `WebRTC ingest is not started. So publish timeout is firing` — which reads like a
  networking or codec fault.
- **The server has to be reachable at an address it can advertise.** A default `docker run`
  publishes its ICE candidate as the container's own `172.17.0.2`, which is unroutable from macOS,
  so signalling and ICE complete and no media ever flows. Publish the media ports
  (`-p 50000-50020:50000-50020/udp`) and run the server somewhere its address is real to the
  client — Docker Desktop's host networking, a VM, or a remote instance. Ant Media's
  `settings.replaceCandidateAddrWithServerAddr` is the knob for this, but it lives in the server's
  database, not `red5-web.properties`: editing the file is overwritten on the next start, so set it
  from the web panel or the REST API.

The iOS simulator has no camera or microphone, so the live check cannot run there at all; leave
`ANTMEDIA_TEST_SERVER` unset and it reports `SKIP`.

Without `ANTMEDIA_TEST_SERVER` the app runs the offline checks only and logs
`SKIP live publish (no serverUrl extra)`, leaving the check count unchanged — so a run with no
server cannot be mistaken for one that proved streaming works.

On Apple Silicon the community image runs under emulation, which is slower to start but works.

## CI

| Workflow | Trigger | What it does |
| --- | --- | --- |
| [`build.yml`](../.github/workflows/build.yml) | called by the other two | Builds natives, packs, validates, runs all three smoke tests |
| [`pr.yml`](../.github/workflows/pr.yml) | pull requests | Builds `<version>-beta.<pr>.<run>` and publishes it |
| [`release.yml`](../.github/workflows/release.yml) | `v*` tags | Publishes the tagged version and creates the GitHub release |

`build.yml` splits packing across runners: Android needs only a JDK and the Android SDK so it
packs on `ubuntu-latest`, while the Apple packages need Xcode and pack on `macos-15` — pinned, not
`macos-latest`, so an image roll cannot change which Xcode versions `select-xcode.sh` can pick
from. The two package sets are merged by the `validate` job, which runs the package tests over the
complete set. The `e2e-mac` job needs an arm64 runner, since `AntMedia.Net.Mac` ships an arm64
Catalyst slice only.

Publishing uses nuget.org [trusted publishing][trusted-publishing]: the job requests a GitHub OIDC
token and exchanges it for a short-lived API key, so there is no long-lived key in the repository
secrets. It needs `id-token: write`, a `NUGET_USER` secret holding the nuget.org account name, and
an `environment:` whose name matches the one recorded on the nuget.org policy — both workflows use
`nuget.org`. A mismatch fails the exchange with HTTP 401 and `Environment mismatch for policy`.

Policies are scoped to a single workflow file, so `pr.yml` and `release.yml` each need their own
on nuget.org; one will not cover the other.

[android-sdk]: https://github.com/ant-media/WebRTC-Android-SDK
[ios-sdk]: https://github.com/ant-media/WebRTC-iOS-SDK
[trusted-publishing]: https://learn.microsoft.com/nuget/nuget-org/trusted-publishing
[stasel]: https://github.com/stasel/WebRTC

[ams-docker]: https://antmedia.io/docs/guides/installing-on-premise/installing-ant-media-server-on-docker/
[ams-publish]: https://antmedia.io/docs/guides/publish-live-stream/webrtc-publishing/
[emulator-networking]: https://developer.android.com/studio/run/emulator-networking
