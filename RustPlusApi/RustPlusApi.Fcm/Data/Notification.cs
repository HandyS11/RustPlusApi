namespace RustPlusApi.Fcm.Data
{
    public class Notification<T>
    {
        public Guid NotificationId { get; set; }
        public ulong PlayerId { get; set; }
        public int PlayerToken { get; set; }
        public Guid ServerId { get; set; }
        public T? Data { get; set; }
    }
}
