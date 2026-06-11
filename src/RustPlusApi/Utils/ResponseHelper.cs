using RustPlusApi.Data;

namespace RustPlusApi.Utils;

/// <summary>Factory helpers for building <see cref="Response{T}"/> objects.</summary>
public static class ResponseHelper
{
    /// <summary>
    /// Builds a generic <see cref="Response{T}"/> object with the specified success status, data, and optional error message.
    /// </summary>
    /// <typeparam name="T">The type of the data to include in the response.</typeparam>
    /// <param name="isSuccess">Indicates whether the operation was successful.</param>
    /// <param name="data">The data to include in the response.</param>
    /// <param name="message">An optional error message. If null, no error is set.</param>
    /// <returns>A <see cref="Response{T}"/> containing the result of the operation.</returns>
    public static Response<T?> BuildGenericOutput<T>(bool isSuccess, T data, string? message = null)
    {
        return new Response<T?>
        {
            IsSuccess = isSuccess,
            Error = BuildError(message),
            Data = data
        };
    }

    /// <summary>
    /// Builds a payload-free <see cref="Response"/> for acknowledge-only commands.
    /// </summary>
    /// <param name="isSuccess">Indicates whether the operation was successful.</param>
    /// <param name="message">An optional error message. If null, no error is set.</param>
    /// <returns>A <see cref="Response"/> containing the result of the operation.</returns>
    public static Response BuildAckOutput(bool isSuccess, string? message = null)
    {
        return new Response
        {
            IsSuccess = isSuccess,
            Error = BuildError(message)
        };
    }

    /// <summary>Builds the <see cref="ErrorMessage"/> for a raw server identifier, or
    /// <see langword="null"/> when there is no error.</summary>
    /// <param name="message">The raw server error identifier, or <see langword="null"/>.</param>
    private static ErrorMessage? BuildError(string? message) =>
        message is null
            ? null
            : new ErrorMessage
            {
                Message = message,
                Code = ParseErrorCode(message)
            };

    /// <summary>Maps a raw Rust+ server error identifier to its <see cref="RustPlusErrorCode"/>;
    /// unrecognized identifiers map to <see cref="RustPlusErrorCode.Unknown"/>. The match is
    /// deliberately exact and case-sensitive — the wire identifiers are stable lowercase strings.
    /// Keep these arms in sync with the <see cref="RustPlusErrorCode"/> members.</summary>
    /// <param name="message">The raw server error identifier.</param>
    private static RustPlusErrorCode ParseErrorCode(string message) => message switch
    {
        "server_error" => RustPlusErrorCode.ServerError,
        "banned" => RustPlusErrorCode.Banned,
        "rate_limit" => RustPlusErrorCode.RateLimit,
        "not_found" => RustPlusErrorCode.NotFound,
        "wrong_type" => RustPlusErrorCode.WrongType,
        "no_team" => RustPlusErrorCode.NoTeam,
        "no_clan" => RustPlusErrorCode.NoClan,
        "no_map" => RustPlusErrorCode.NoMap,
        "access_denied" => RustPlusErrorCode.AccessDenied,
        "message_not_sent" => RustPlusErrorCode.MessageNotSent,
        "too_many_subscribers" => RustPlusErrorCode.TooManySubscribers,
        "not_enabled" => RustPlusErrorCode.NotEnabled,
        _ => RustPlusErrorCode.Unknown
    };
}
