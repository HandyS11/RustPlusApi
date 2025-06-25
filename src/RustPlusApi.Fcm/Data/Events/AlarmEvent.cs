namespace RustPlusApi.Fcm.Data.Events;

public sealed record AlarmEvent
{
    public Guid NotificationId { get; set; }
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;
}
