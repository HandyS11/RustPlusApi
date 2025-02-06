using RustPlusApi.Data;

namespace RustPlusApi.Utils;

public static class ResponseHelper
{
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
