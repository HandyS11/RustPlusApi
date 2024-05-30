namespace RustPlusApi.Data
{
    public class Response<T>
    {
        public bool IsSuccess { get; set; }
        public Error? Error { get; set; }
        public T? Data { get; set; }
    }

    public class Error
    {
        public string? Message { get; set; }
    }
}
