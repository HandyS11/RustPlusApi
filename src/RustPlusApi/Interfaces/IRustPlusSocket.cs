using RustPlusContracts;

namespace RustPlusApi.Interfaces;

public interface IRustPlusSocket
{
    public interface IRustPlusSocket : IDisposable
    {
        event EventHandler? Connecting;
        event EventHandler? Connected;
        event EventHandler? SendingRequest;
        event EventHandler<AppRequest>? RequestSent;
        event EventHandler<AppMessage>? MessageReceived;
        event EventHandler<AppMessage>? NotificationReceived;
        event EventHandler<AppMessage>? ResponseReceived;
        event EventHandler? Disconnecting;
        event EventHandler? Disconnected;
        event EventHandler<Exception>? ErrorOccurred;

        Task ConnectAsync();
        Task DisconnectAsync(bool forceClose = false);
    }
}