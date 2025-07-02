using RustPlusApi.Data;

namespace RustPlusApi.Utils;

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
            Error = message is null ? null : new ErrorMessage { Message = message },
            Data = data
        };
    }
}
