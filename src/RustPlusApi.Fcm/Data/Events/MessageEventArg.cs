using static RustPlusApi.Fcm.Data.Tags;

namespace RustPlusApi.Fcm.Data.Events;

internal sealed class MessageEventArgs : EventArgs
{
    public McsProtoTag Tag { get; init; }
    public object? Object { get; init; }
}
