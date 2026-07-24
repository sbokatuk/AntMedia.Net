namespace AntMedia.Net;

/// <summary>
/// How video is fitted into its view. Maps to <c>RendererCommon.ScalingType</c> on Android and
/// <c>UIViewContentMode</c> on iOS and Mac Catalyst.
/// </summary>
public enum AntMediaVideoScaling
{
    /// <summary>Fill the view, cropping whatever does not fit. The usual choice for previews.</summary>
    Fill,

    /// <summary>Show the whole frame, letterboxing the rest. The usual choice for playback.</summary>
    Fit,

    /// <summary>
    /// Android's compromise: crop a bounded amount, letterbox the remainder. Apple has no
    /// equivalent, so it behaves as <see cref="Fill" /> there.
    /// </summary>
    Balanced,
}
