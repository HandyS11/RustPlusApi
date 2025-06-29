namespace RustPlusApi.Fcm.Interfaces;

public interface IRustPlusFcmSocket
{
    event EventHandler? Connecting;
    event EventHandler? Connected;
    event EventHandler<string>? NotificationReceived;
    event EventHandler? Disconnecting;
    event EventHandler? Disconnected;
    event EventHandler? SocketClosed;
    event EventHandler<Exception>? ErrorOccurred;

    Task ConnectAsync();
    void Disconnect();
}