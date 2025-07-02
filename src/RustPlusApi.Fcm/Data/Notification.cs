namespace RustPlusApi.Fcm.Data;

public record Notification<T>
{
    public ulong PlayerId { get; set; }
    public int PlayerToken { get; set; }
    public Guid ServerId { get; set; }
    public T? Data { get; set; }
}
