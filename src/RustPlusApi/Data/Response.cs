namespace RustPlusApi.Data;

public sealed record Response<T>
{
    public bool IsSuccess { get; init; }
    public ErrorMessage? Error { get; init; }
    public T? Data { get; init; }
}

public sealed record ErrorMessage
{
    public string? Message { get; init; }
}
