namespace RustPlusApi.Fcm.Data.Events
{
    public class AlarmEventArg
    {
        public Guid ServerId { get; set; }
        public string Title { get; set; } = null!;
        public string Message { get; set; } = null!;
    }
}
