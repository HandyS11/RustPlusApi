using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// Step 8 convenience: wraps <see cref="RustPlusFcm"/> and surfaces the first in-game
/// "Pair with Server" notification as a strongly-typed <see cref="ServerPairing"/>, so the
/// whole pairing flow is one <c>await</c>.
/// </summary>
/// <remarks>
/// The event surface (<see cref="Listening"/>, <see cref="Paired"/>, <see cref="Stopped"/>,
/// <see cref="Failed"/>) is modelled on Pronwan/rustplus-desktop's <c>IPairingListener</c> so
/// it can drop in as a replacement for that project's Node-process listener.
/// </remarks>
public sealed class PairingListener(Credentials credentials, ICollection<string>? persistentIds = null)
    : IDisposable
{
    private readonly RustPlusFcm _fcm = new(credentials, persistentIds);

    /// <summary>Raised once the listener is connected and waiting for pairing notifications.</summary>
    public event EventHandler? Listening;

    /// <summary>Raised when a server-pairing notification arrives.</summary>
    public event EventHandler<ServerPairing>? Paired;

    /// <summary>Raised when the listener stops.</summary>
    public event EventHandler? Stopped;

    /// <summary>Raised when the listener fails.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>
    /// Connects, then completes with the first server-pairing notification received.
    /// </summary>
    public async Task<ServerPairing> WaitForServerPairingAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<ServerPairing>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnServerPairing(object? sender, Notification<ServerEvent?> notification)
        {
            var pairing = ToServerPairing(notification);
            Paired?.Invoke(this, pairing);
            completion.TrySetResult(pairing);
        }

        void OnError(object? sender, Exception ex)
        {
            Failed?.Invoke(this, ex);
            completion.TrySetException(ex);
        }

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        _fcm.OnServerPairing += OnServerPairing;
        _fcm.ErrorOccurred += OnError;

        try
        {
            await _fcm.ConnectAsync().ConfigureAwait(false);
            Listening?.Invoke(this, EventArgs.Empty);
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _fcm.OnServerPairing -= OnServerPairing;
            _fcm.ErrorOccurred -= OnError;
            _fcm.Disconnect();
            Stopped?.Invoke(this, EventArgs.Empty);
        }
    }

    internal static ServerPairing ToServerPairing(Notification<ServerEvent?> notification)
    {
        var server = notification.Data;
        return new ServerPairing
        {
            Ip = server?.Ip ?? string.Empty,
            Port = server?.Port ?? 0,
            PlayerId = notification.PlayerId,
            PlayerToken = notification.PlayerToken,
            Name = server?.Name
        };
    }

    public void Dispose() => _fcm.Dispose();
}
