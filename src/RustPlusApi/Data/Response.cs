namespace RustPlusApi.Data;

public class Response<T>
{
    public bool IsSuccess { get; set; }
    public ErrorMessage? Error { get; set; }
    public T? Data { get; set; }
}

public class ErrorMessage
{
    public string? Message { get; set; }
}
