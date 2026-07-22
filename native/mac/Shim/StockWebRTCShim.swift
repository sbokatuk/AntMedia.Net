//
//  StockWebRTCShim.swift
//
//  Part of https://github.com/sbokatuk/AntMedia.Net — not an Ant Media file.
//  Compiled into WebRTCiOSSDK for the Mac Catalyst build only. iOS is untouched.
//
//  WHY THIS EXISTS
//  ---------------
//  Ant Media links a *customised* libwebrtc. Their build adds RTCAudioDeviceModule, publishes for
//  iOS only, and the fork's source is not available — so a Mac Catalyst slice cannot be produced
//  from it. The Catalyst build therefore uses a stock community libwebrtc, which does ship a
//  maccatalyst slice, and this supplies the handful of things Ant Media's Swift code expects from
//  their fork that stock does not have.
//
//  The gap is genuinely small. Across the whole SDK the fork-only API amounts to three members of
//  RTCAudioDeviceModule, one factory initialiser label, and one AVAudioSession category overload.
//  Nothing in their source is patched.
//

import AVFoundation
import CoreMedia
import Foundation
import WebRTC

/// Stand-in for the fork's audio device module.
///
/// Stock libwebrtc owns its audio device internally and exposes only the `RTCAudioDevice`
/// *protocol*, for replacing that device wholesale. Ordinary microphone capture therefore needs
/// nothing from this type — libwebrtc's own device does the work — so it only has to carry the
/// external-audio flag and stand in for the type Ant Media's code names.
@objc public class RTCAudioDeviceModule: NSObject {
    private var externalAudio = false

    /// Records the caller's intent. Actually routing external audio needs
    /// <see cref="deliverRecordedData"/>, which this shim cannot service — see below.
    @objc public func setExternalAudio(_ enabled: Bool) {
        externalAudio = enabled
    }

    /// Only reached when external audio was requested, which is not supported on Mac Catalyst.
    ///
    /// Injecting samples into stock libwebrtc means implementing the `RTCAudioDevice` protocol —
    /// a real audio unit, not a forwarding call — so this deliberately does nothing rather than
    /// pretend. Microphone capture is unaffected; this is the broadcast-extension path.
    @objc public func deliverRecordedData(_ sampleBuffer: CMSampleBuffer) {
        // Intentionally empty. Logged rather than trapped so a caller that sets external audio by
        // habit does not take the whole app down.
        NSLog("[AntMedia.Net] external audio is not supported on Mac Catalyst; sample dropped.")
    }
}

public extension RTCPeerConnectionFactory {
    /// Maps the fork's initialiser onto the stock one.
    ///
    /// Passing no audio device leaves libwebrtc using its own, which is what the fork's module
    /// does for everything except external audio.
    convenience init(
        encoderFactory: RTCVideoEncoderFactory?,
        decoderFactory: RTCVideoDecoderFactory?,
        audioDeviceModule: RTCAudioDeviceModule
    ) {
        self.init(encoderFactory: encoderFactory, decoderFactory: decoderFactory)
    }
}

public extension RTCAudioSession {
    /// The fork takes a `String` here; stock takes `AVAudioSession.Category`. Not recursive — the
    /// parameter types differ, so this resolves to the framework's own overload.
    func setCategory(_ category: String) throws {
        try setCategory(AVAudioSession.Category(rawValue: category))
    }
}
