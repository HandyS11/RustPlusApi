namespace RustPlusApi.Fcm.Data.Events
{
    public class ServerFullEventArg : ServerEventArg
    {
        public string Ip { get; set; } = null!;
        public int Port { get; set; }
        public string Desc { get; set; } = null!;
        public int Logo { get; set; }
        public int Img { get; set; }
        public string Url { get; set; } = null!;
    }
}
