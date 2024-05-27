using static RustPlusApi.Fcm.Data.Constants;

namespace RustPlusApi.Fcm.Utils
{
    internal class MessageEventArgs : EventArgs
    {
        public McsProtoTag Tag { get; set; }
        public object? Object { get; set; }
    }
}
