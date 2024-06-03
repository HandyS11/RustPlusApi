namespace RustPlusApi.Fcm.Data.Events
{
    public class ServerFullEventArg : ServerEventArg
    {
        public string Ip { get; set; } = null!;
        public int Port { get; set; }
        public string Desc { get; set; } = null!;
        public string Logo { get; set; }
        public string Img { get; set; }
        public string Url { get; set; } = null!;
        public ulong PlayerId { get; set; }
        public string PlayerToken { get; set; } = null!;
    }
}
