namespace RustPlusApi.Fcm.Data.Events
{
    public class ServerEvent
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string Ip { get; set; } = null!;
        public int Port { get; set; }
        public string? Desc { get; set; }
        public string? Logo { get; set; }
        public string? Img { get; set; }
        public string? Url { get; set; }
    }
}
