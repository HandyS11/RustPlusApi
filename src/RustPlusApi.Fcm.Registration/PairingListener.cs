using Microsoft.Extensions.Logging;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Registration.Steps;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;

namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// Step 8 convenience: wraps <see cref="RustPlusFcm"/> and surfaces the first in-game
/// "Pair with Server" notification as a strongly-typed <see cref="ServerPairing"/>, so the
/// whole pairing flow is one <c>await</c>.
/// </summary>
/// <param name="credentials">FCM credentials used to connect and authenticate.</param>
/// <param name="persistentIds">Already-seen message IDs the FCM listener should skip on reconnect.</param>
/// <param name="loggerFactory">Routes the wrapped FCM client's diagnostics (including silently
/// skipped notifications) into your logging stack; logging is disabled when <see langword="null"/>.</param>
/// <param name="httpClient">Optional <see cref="HttpClient"/> used for the pre-connect GCM check-in;
/// a new instance is created if <see langword="null"/>.</param>
/// <remarks>
/// The event surface (<see cref="Listening"/>, <see cref="Paired"/>, <see cref="Stopped"/>,
/// <see cref="Failed"/>) is modelled on Pronwan/rustplus-desktop's <c>IPairingListener</c> so
/// it can drop in as a replacement for that project's Node-process listener.
/// </remarks>
public sealed class PairingListener(
    Credentials credentials,
    ICollection<string>? persistentIds = null,
    ILoggerFactory? loggerFactory = null,
    HttpClient? httpClient = null)
    : IDisposable
{
    private readonly RustPlusFcm _fcm = new(credentials, persistentIds, loggerFactory: loggerFactory);
    private readonly AndroidFcmRegister _androidFcmRegister = new(httpClient);

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
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    /// <remarks>Excluded from coverage: requires a live FCM connection to complete;
    /// the pairing-notification mapping (<see cref="ToServerPairing"/>) is unit-tested independently.</remarks>
    [ExcludeFromCodeCoverage]
    public async Task<ServerPairing> WaitForServerPairingAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<ServerPairing>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnServerPairing(object? _, Notification<ServerEvent?> notification)
        {
            var pairing = ToServerPairing(notification);
            Paired?.Invoke(this, pairing);
            completion.TrySetResult(pairing);
        }

        void OnError(object? _, Exception ex)
        {
            Failed?.Invoke(this, ex);
            completion.TrySetException(ex);
        }

#pragma warning disable RCS1261 // CancellationTokenRegistration.DisposeAsync not available in netstandard2.0
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
#pragma warning restore RCS1261

        _fcm.OnServerPairing += OnServerPairing;
        _fcm.ErrorOccurred += OnError;

        try
        {
            // Reference behaviour (push-receiver): re-check-in the device immediately before every
            // MCS connect so Google's device registry entry is fresh when pushes get routed.
            await _androidFcmRegister.CheckInAsync(credentials.Gcm, cancellationToken).ConfigureAwait(false);
            await _fcm.ConnectAsync(cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc/>
    public void Dispose() => _fcm.Dispose();
}
