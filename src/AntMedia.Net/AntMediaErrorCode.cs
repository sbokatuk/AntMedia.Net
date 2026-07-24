namespace AntMedia.Net;

/// <summary>
/// Machine-readable category for <see cref="AntMediaErrorEventArgs" /> and
/// <see cref="AntMediaException" />. Match on this rather than on the message: the wording
/// differs per platform — Android relays the server's error definitions verbatim, the iOS SDK
/// rewrites some of them — and neither is stable.
/// </summary>
public enum AntMediaErrorCode
{
    /// <summary>The SDK reported something this library does not recognise. See the message.</summary>
    Unknown,

    /// <summary>The stream asked for does not exist or is not live.</summary>
    NoStreamExists,

    /// <summary>Another publisher is already using the stream id.</summary>
    StreamIdInUse,

    /// <summary>The server rejected the token or the request was unauthorized.</summary>
    Unauthorized,

    /// <summary>The server only accepts registered stream ids and this one is not.</summary>
    NotAllowed,

    /// <summary>The server gave up waiting for the publisher.</summary>
    PublishTimeout,

    /// <summary>The server reported an internal problem — check the server logs.</summary>
    ServerError,

    /// <summary>
    /// The server did not confirm the operation within <see cref="AntMediaOptions.Timeout" />.
    /// Raised by this library, not by the server, because some failures produce no callback.
    /// </summary>
    Timeout,

    /// <summary>The connection dropped before the server confirmed the operation.</summary>
    Disconnected,
}

/// <summary>Maps the SDKs' error strings onto <see cref="AntMediaErrorCode" />.</summary>
internal static class AntMediaErrorCodes
{
    // The iOS SDK passes its delegate AntMediaError.localized(definition): two definitions are
    // replaced with prose, everything else is prefixed. The raw definition is recovered from the
    // suffix before matching, so both platforms resolve to the same code.
    private const string ApplePrefix = "An error occured: ";

    internal static AntMediaErrorCode FromMessage(string message)
    {
        var definition = message.StartsWith(ApplePrefix, StringComparison.Ordinal)
            ? message[ApplePrefix.Length..]
            : message;

        return definition switch
        {
            "no_stream_exist" or
            "stream_not_exist_or_not_streaming" or
            "No stream exists on server." => AntMediaErrorCode.NoStreamExists,

            "streamIdInUse" => AntMediaErrorCode.StreamIdInUse,

            "unauthorized_access" or
            "authenticationTokenNotValid" or
            "Unauthorized access: Check your token" => AntMediaErrorCode.Unauthorized,

            "not_allowed_unregistered_streams" => AntMediaErrorCode.NotAllowed,

            "publishTimeoutError" => AntMediaErrorCode.PublishTimeout,

            "server_error_check_logs" or
            "license_suspended_please_renew_license" => AntMediaErrorCode.ServerError,

            _ => AntMediaErrorCode.Unknown,
        };
    }
}
