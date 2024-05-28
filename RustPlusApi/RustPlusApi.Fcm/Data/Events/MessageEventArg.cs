using static RustPlusApi.Fcm.Data.Constants;

namespace RustPlusApi.Fcm.Data.Events
{
    internal class MessageEventArgs : EventArgs
    {
        public McsProtoTag Tag { get; set; }
        public object? Object { get; set; }
    }
}
