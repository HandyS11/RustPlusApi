namespace RustPlusApi.Fcm.Data.Events
{
    public class AlarmEvent
    {
        public Guid NotificationId { get; set; }
        public string Title { get; set; } = null!;
        public string Message { get; set; } = null!;
    }
}
