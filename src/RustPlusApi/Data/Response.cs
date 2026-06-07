namespace RustPlusApi.Data;

/// <summary>Generic wrapper returned by every Rust+ API call, carrying success state, data and an optional error.</summary>
/// <typeparam name="T">The type of the payload returned on success.</typeparam>
public sealed record Response<T>
{
    /// <summary><see langword="true"/> if the server processed the request successfully.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Error detail provided by the server when <see cref="IsSuccess"/> is <see langword="false"/>.</summary>
    public ErrorMessage? Error { get; init; }

    /// <summary>The response payload, or <see langword="null"/> when the call failed.</summary>
    public T? Data { get; init; }
}

/// <summary>Error detail attached to a failed <see cref="Response{T}"/>.</summary>
public sealed record ErrorMessage
{
    /// <summary>Human-readable description of the error returned by the server.</summary>
    public string? Message { get; init; }
}
