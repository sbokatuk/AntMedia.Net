---
applyTo: "native/**"
---

# Native SDK builds

The natives are built from source, never downloaded prebuilt, and every input is pinned in
`Directory.Build.props`. `build/pins.sh` is the only shell parser of that file — read pins through
it, never with a second `sed`.

## Pins

- Change a pin only in `Directory.Build.props`, and change both halves together:
  `AntMediaAndroidTag` + `AntMediaAndroidCommit` (the script asserts the tag still resolves to the
  sha, so a re-pointed tag fails the build instead of silently building other sources),
  `AntMediaIosCommit` + `AntMediaIosCommitDate` (upstream publishes no versions at all, so the pin
  is master's head and the date says how current it is), `AntMediaMacWebRtcRelease` +
  `AntMediaMacWebRtcSha256` (release assets can be re-uploaded under the same name).
- Never build from a branch head. An explicit tag or sha passed on the command line is for
  experiments only.
- After moving the iOS pin, re-check that the facade compiles, that `AntMediaClientDelegate` has not
  renamed a callback (default implementations mean a renamed callback still compiles and the event
  simply stops arriving), and `src/AntMedia.Net/AntMediaErrorCode.cs` against upstream's
  `AntMediaError.localized`.

## Facade and shim

- Facade classes keep their explicit `@objc(AMSClient)`-style names. Without them Swift exports
  `_OBJC_CLASS_$__TtC12WebRTCiOSSDK9AMSClient` while the binding links against
  `_OBJC_CLASS_$_AMSClient`, and every consuming app fails at link time.
- Keep the `nm -gU` symbol assertions and the generated-header check in `fetch-ios.sh` and
  `fetch-mac.sh`; they are what catches the mangled form.
- The facade has to be compiled *into* `WebRTCiOSSDK` to reach the generated header — hence the
  rebuild, and `add-facade.py` editing `project.pbxproj` through `plutil`/`plistlib` rather than the
  `xcodeproj` gem.
- `native/mac/Shim/StockWebRTCShim.swift` supplies what stock libwebrtc lacks
  (`RTCAudioDeviceModule` and the matching `peerConnectionFactory` initialiser). Never patch Ant
  Media's own sources for Catalyst; external audio injection stays unavailable on Mac.

## Mac Catalyst build settings

Three settings in `fetch-mac.sh` are load-bearing, and each fails unhelpfully:

- `SUPPORTS_MACCATALYST=YES` — otherwise "Unable to find a destination matching the provided
  destination specifier".
- `IPHONEOS_DEPLOYMENT_TARGET=14.0` — Catalyst derives availability from the *iOS* deployment
  target; raising `MACOSX_DEPLOYMENT_TARGET` does nothing.
- `ARCHS=arm64` — Ant Media picks Metal on arm64 and OpenGL otherwise, and Catalyst has no OpenGL.
  This is why `AntMedia.Net.Mac` is Apple Silicon only.

Keep `strip-slices.py` (arm64 Catalyst slice only) and `flatten-frameworks.py` (versioned layout →
shallow, because NuGet cannot carry symlinks and the resulting copies otherwise fail code signing).

## Script contract

- Every fetch script is pin-keyed and idempotent: an up-to-date tree exits in a second, `-f` forces
  a rebuild, and the same identity is the CI cache key — so changing the facade or the shim must
  change what is hashed.
- Fail loudly and early with `antmedia_fail` from `pins.sh`, checking prerequisites (JDK 17,
  `ANDROID_HOME`, macOS with Xcode) up front rather than letting gradle or xcodebuild die obscurely.
- Keep the console quiet and the detail in `native/build/<platform>/xcodebuild-*.log`.
