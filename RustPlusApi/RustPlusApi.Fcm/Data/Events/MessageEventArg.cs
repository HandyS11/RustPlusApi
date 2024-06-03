using static RustPlusApi.Fcm.Data.Tags;

namespace RustPlusApi.Fcm.Data.Events
{
    internal class MessageEventArgs : EventArgs
    {
        public McsProtoTag Tag { get; set; }
        public object? Object { get; set; }
    }
}
