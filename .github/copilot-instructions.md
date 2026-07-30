# AntMedia.Net

.NET bindings for the Ant Media WebRTC SDKs: one C# API for publishing and playing WebRTC streams
through an Ant Media Server, from .NET MAUI or plain .NET, on Android, iOS and Mac Catalyst.

## Overview

- Five packages, each depending on the next: `AntMedia.Net.Maui` (video view, handler, Android
  `Activity` plumbing) → `AntMedia.Net` (cross-platform client) → `AntMedia.Net.Android` /
  `AntMedia.Net.iOS` / `AntMedia.Net.Mac` (raw bindings). All five publish to nuget.org.
- Bound SDKs: `ant-media/WebRTC-Android-SDK` at tag `v2.17.2`; `ant-media/WebRTC-iOS-SDK` at a
  pinned commit (upstream publishes no version of any kind); the same iOS commit rebuilt for Mac
  Catalyst against a stock `stasel/WebRTC` libwebrtc.
- Ant Media's SDKs are MIT and are redistributed inside the packages; the bundled libwebrtc is BSD,
  so every package ships `THIRD-PARTY-NOTICES.txt`.
- Versioning: `VersionPrefix` equals `AntMediaVersion` (`2.17.2`, the Ant Media *Android* SDK tag)
  and stays put between SDK bumps; each release appends a fourth digit through its tag (`v2.17.2.4`).

## Build and verify

Prerequisites: the .NET 9 and .NET 10 SDKs, with `ios android maccatalyst maui-android maui-ios
maui-maccatalyst` installed for *each* band (the SDK is resolved from the working directory and
`global.json` pins .NET 9, so install the .NET 10 band from a scratch directory with its own
`global.json`); JDK 17 plus `ANDROID_HOME` for Android; macOS with Xcode for the Apple targets,
Apple Silicon for Mac Catalyst.

```sh
./native/android/fetch-android.sh                  # builds the .aar from the pinned tag
./native/ios/fetch-ios.sh                          # xcframework with the @objc facade compiled in
./native/mac/fetch-mac.sh                          # the same commit, Mac Catalyst, stock libwebrtc
./build/BuildNugets.sh                             # two-pass pack + merge -> ./artifacts
dotnet test tests/AntMedia.Net.PackageTests        # asserts the packed .nupkg shape
```

- Do not expect `dotnet build AntMedia.Net.sln` to pass: the solution spans .NET 8/9/10 and every
  SDK fails on the band it does not own. Build individual projects, or use `build/BuildNugets.sh`,
  which handles the bands.
- Run the fetch scripts before packing, or the bindings pack without their native payload. They are
  pin-keyed and idempotent — a re-run with nothing changed exits in a second, `-f` forces a rebuild;
  full `xcodebuild` logs land in `native/build/<platform>/xcodebuild-*.log`.
- Device tests: `./.github/scripts/run-{android,ios,mac}-device-tests.sh <version> [tfm]` — Android
  needs a booted emulator, iOS boots its own simulator, Mac Catalyst runs directly on an arm64 Mac.
  Each runs the live publish check only when `ANTMEDIA_TEST_SERVER` is set; from an emulator the
  host is `10.0.2.2` and the community edition's application is `LiveApp`:
  `ANTMEDIA_TEST_SERVER="ws://10.0.2.2:5080/LiveApp/websocket" ./.github/scripts/run-android-device-tests.sh <version>`.
- For a local iOS run, scope the Xcode to the command rather than changing `xcode-select` globally:
  `DEVELOPER_DIR=/Applications/Xcode_26.0.1.app/Contents/Developer ./.github/scripts/run-ios-device-tests.sh <version>`.

## Layout

```
Directory.Build.props   native SDK pins, target framework bands, shared package metadata
global.json             pins the .NET 9 SDK (the "net9 band")
NuGet.config            nuget.org + ./artifacts, with AntMedia.* mapped to ./artifacts only
build/                  pins.sh (the only shell parser of the props), BuildNugets.sh, merge-packages.py
native/android/         fetch-android.sh, overlay/
native/ios/             fetch-ios.sh, Facade/AntMediaNetFacade.swift, add-facade.py
native/mac/             fetch-mac.sh, Shim/StockWebRTCShim.swift, enable-catalyst.py,
                        strip-slices.py, flatten-frameworks.py
src/ tests/ samples/    bindings + client + MAUI package; PackageTests + three DeviceTests; the sample
docs/BUILD.md           the long-form version of all of the above
```

## Conventions

- Change upstream versions only through `Directory.Build.props`, moving both halves of a pin
  together: tag + commit (Android), commit + date (iOS/Catalyst), release + sha256 (libwebrtc).
- Keep target frameworks in the band properties, with an explicit platform version in every one:
  bare `net8.0-android` resolves to `android21.0` and silently produces a binding with no `.aar`.
- Keep `AntMediaFallbackTargetFrameworks` (plain `net8.0`/`net9.0`/`net10.0`, compiled from
  `Platforms/Unsupported`, throwing `PlatformNotSupportedException`) on the two cross-platform
  packages: without them `dotnet new maui` on Windows fails restore with NU1202 for the whole
  project, Android leg included.
- Local packs must never carry a released version number; `./build/BuildNugets.sh` with no argument
  defaults to `<props version>-local.<timestamp>` for exactly that reason.
- Leave the `AntMedia.*` package-source mapping in `NuGet.config` alone; it is what stops the tests
  and the sample restoring the published release instead of what was just packed.
- British spelling throughout, matching the README ("licence", "behaviour", "customised"), and
  comments that explain *why*, in the style already in the props, the csproj files and the scripts.
- Update `docs/BUILD.md` when behaviour changes; its paragraph claiming `buildTransitive/` "has not
  been evaluated" is stale — the csproj comments and the packaging are the source of truth.

## CI and release flow

- `build.yml` is reusable and does everything: pack-android on `ubuntu-latest`, pack-apple on
  `macos-15` (pinned, with `.github/scripts/select-xcode.sh` globbing `Xcode_26.0*.app`), a
  `validate` job that merges both package sets and runs PackageTests, and the e2e jobs — including
  an arm64 `e2e-mac` and an Android live publish against a community server. `verify` defaults true.
- `pr.yml` builds `<version>-beta.<pr>.<run>` and publishes it **only** when the pull request
  carries the `publish-beta` label (`labeled` is in the trigger types so applying it starts a run).
- `release.yml` runs on a `v*` tag or a dispatch, with a guard job that refuses a tag which is not
  an ancestor of the default branch, `verify: false`, `actions/attest-build-provenance@v2` (verify
  with `gh attestation verify`), then the nuget.org push and the GitHub release.
- Cutting a release: write `docs/release-notes/<version>.md` and merge it. `auto-release.yml` tags
  the merge commit and dispatches `release.yml`.
- `upstream-drift.yml` runs daily and files one fingerprint-deduplicated issue per platform, titled
  `Upstream drift: <platform>`. Closing one by hand declines that update.
- Publishing is nuget.org trusted publishing: OIDC plus `NuGet/login`, `environment: nuget.org`,
  `NUGET_USER` as the only secret, one policy per workflow file.
- CI builds the smoke apps and the sample in **Debug** (bar the Android Release link check): an iOS
  Release build AOT-compiles the ~27 MB WebRTC framework — 38 minutes observed — and proves nothing
  extra.

## Testing

- `tests/AntMedia.Net.PackageTests` reads the packed `.nupkg` files with
  `System.Reflection.Metadata` — layout, metapackage, MAUI and compliance assertions, no workloads
  needed, Apple assertions skipped on Linux. There is no unit-test project.
- The three DeviceTests apps prove offline that the native libraries load and the bound API is
  callable. The single live check publishes to a real server and is what caught the SDK's
  undeclared gson dependency, which every offline check passed.
- Apple live coverage is local only: GitHub's macOS runners have no Docker, the simulator has no
  camera, and the Catalyst runner has nobody to answer the permission prompt. Without
  `ANTMEDIA_TEST_SERVER` the runners log `SKIP live publish` and leave the check count unchanged —
  a skip is not a pass.

## Hard rules

- Never build from an unpinned branch head, and never move one half of a pin without the other.
- Keep the explicit `@objc(AMSClient)`-style names on the facade types and the `nm` symbol checks in
  the fetch scripts; a mangled export breaks every consuming app at link time.
- Keep `NoBindingEmbedding=true`, the `NativeReference` items and the `buildTransitive/<id>.targets`
  packaging. Never embed the dynamic frameworks in the binding assembly, and never move the consumer
  targets to plain `build/` — NuGet hides those from transitive consumers.
- Never patch Ant Media's own sources for Mac Catalyst; `native/mac/Shim` supplies what stock
  libwebrtc lacks. `AntMedia.Net.Mac` stays arm64-only.
- Keep an explicit platform version in every TFM, and keep the plain fallback TFMs.
- Never pack a released version locally, and never remove the `AntMedia.*` source mapping.
- Test against the community edition's `LiveApp`, never `WebRTCAppEE` — the Enterprise name 404s and
  shows up as a silent publish timeout.
- Release only through a release-note merge or a `v*` tag running `release.yml` (guard plus
  attestation); publish betas only through the `publish-beta` label; never publish by hand.
- Keep `macos-15` rather than `macos-latest`, keep the glob-based Xcode selection, and keep the CI
  smoke and sample builds in Debug.
- Ship `THIRD-PARTY-NOTICES.txt` in every package; release notes use absolute links only, because
  they become GitHub Release bodies.

## References

- [Ant Media documentation](https://antmedia.io/docs/) — its WebRTC guides use the Enterprise
  application `WebRTCAppEE`; the community edition serves `LiveApp`.
- [ant-media/WebRTC-Android-SDK](https://github.com/ant-media/WebRTC-Android-SDK),
  [ant-media/WebRTC-iOS-SDK](https://github.com/ant-media/WebRTC-iOS-SDK),
  [stasel/WebRTC](https://github.com/stasel/WebRTC).
- [sbokatuk/Red5Pro.Streaming.Net](https://github.com/sbokatuk/Red5Pro.Streaming.Net) — the sibling
  repository, same shape, but its SDKs are not redistributable.
- [`docs/BUILD.md`](../docs/BUILD.md) — all of the above in full.

Trust these instructions. Search the codebase only when something here is incomplete or wrong.
