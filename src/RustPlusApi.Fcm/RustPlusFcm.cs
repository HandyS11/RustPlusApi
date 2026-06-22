using Microsoft.Extensions.Logging;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Extensions;
using RustPlusApi.Fcm.Interfaces;
using static RustPlusApi.Fcm.Utils.ResponseHelper;

namespace RustPlusApi.Fcm;

/// <summary>
/// Represents a RustPlus FCM listener, which processes FCM notifications for RustPlus.
/// Inherits from <see cref="RustPlusFcmSocket"/>.
/// </summary>
/// <param name="credentials">The <see cref="Credentials"/> used for authentication.</param>
/// <param name="persistentIds">Already-processed message ids, used for de-duplication, and the
/// collection the socket harvests new ids into — pass a mutable, caller-owned set (prefer a
/// <see cref="HashSet{T}"/>; a <see cref="List{T}"/> makes the duplicate check an O(n) scan). When
/// <see langword="null"/>, de-duplication is disabled. The set is NOT cleared on login, so seeded
/// ids survive reconnect. Read the current ids back via <see cref="PersistentIds"/> (snapshot) or
/// subscribe to <see cref="PersistentIdReceived"/> (incremental) to persist them; ids have a
/// server-side lifespan, so pruning your stored copy is your responsibility.</param>
/// <param name="options">Tuning options (heartbeat interval, inactivity timeout); defaults are used when <see langword="null"/>.</param>
/// <param name="loggerFactory">Routes the client's diagnostics into your logging stack; logging is
/// disabled (a no-op <c>NullLogger</c>) when <see langword="null"/>.</param>
public class RustPlusFcm(
    Credentials credentials,
    ICollection<string>? persistentIds = null,
    RustPlusFcmSocketOptions? options = null,
    ILoggerFactory? loggerFactory = null)
    : RustPlusFcmSocket(credentials, persistentIds, options, loggerFactory), IRustPlusFcm
{
    /// <summary>
    /// Occurs when a pairing <see cref="FcmMessage"/> is received.
    /// </summary>
    public event EventHandler<FcmMessage>? OnPairing;

    /// <summary>
    /// Occurs when an entity pairing notification is received.
    /// </summary>
    /// <remarks>
    /// The event data is a <see cref="Notification{T}"/> containing an <see cref="EntityEvent"/>.
    /// </remarks>
    public event EventHandler<Notification<EntityEvent?>>? OnEntityPairing;

    /// <summary>
    /// Occurs when a server pairing notification is received.
    /// </summary>
    /// <remarks>
    /// The event data is a <see cref="Notification{T}"/> containing a <see cref="ServerEvent"/>.
    /// </remarks>
    public event EventHandler<Notification<ServerEvent?>>? OnServerPairing;

    /// <summary>
    /// Occurs when a smart switch pairing notification is received.
    /// </summary>
    /// <remarks>
    /// The event data is a <see cref="Notification{T}"/> containing a <see cref="ulong"/> entity ID.
    /// </remarks>
    public event EventHandler<Notification<ulong?>>? OnSmartSwitchPairing;

    /// <summary>
    /// Occurs when a smart alarm pairing notification is received.
    /// </summary>
    /// <remarks>
    /// The event data is a <see cref="Notification{T}"/> containing a <see cref="ulong"/> entity ID.
    /// </remarks>
    public event EventHandler<Notification<ulong?>>? OnSmartAlarmPairing;

    /// <summary>
    /// Occurs when a storage monitor pairing notification is received.
    /// </summary>
    /// <remarks>
    /// The event data is a <see cref="Notification{T}"/> containing a <see cref="ulong"/> entity ID.
    /// </remarks>
    public event EventHandler<Notification<ulong?>>? OnStorageMonitorPairing;

    /// <summary>
    /// Occurs when an alarm event is triggered.
    /// </summary>
    /// <remarks>
    /// The event data is an <see cref="AlarmEvent"/>.
    /// </remarks>
    public event EventHandler<AlarmEvent?>? OnAlarmTriggered;

    /// <summary>
    /// Parses an incoming <see cref="FcmMessage"/> and dispatches events based on its channel.
    /// </summary>
    /// <param name="message">The <see cref="FcmMessage"/> to parse.</param>
    /// <remarks>
    /// Invokes <see cref="OnPairing"/>, <see cref="OnAlarmTriggered"/>, and calls <see cref="ParsePairing"/> as appropriate.
    /// </remarks>
    protected override void ParseNotification(FcmMessage message)
    {
        switch (message.Data.ChannelId)
        {
            case "pairing":
                OnPairing?.Invoke(this, message);
                ParsePairing(message.Data.Body);
                break;
            case "alarm":
                OnAlarmTriggered?.Invoke(this, message.Data.ToAlarmEvent());
                break;
            default:
                Logger.LogUnknownChannel(message.Data.ChannelId);
                break;
        }
    }

    /// <summary>
    /// Handles pairing notifications by type and dispatches related events.
    /// </summary>
    /// <param name="body">The <see cref="Body"/> of the notification.</param>
    /// <remarks>
    /// Invokes <see cref="OnEntityPairing"/>, <see cref="OnServerPairing"/>, and calls <see cref="ParsePairingEntity"/>.
    /// </remarks>
    private void ParsePairing(Body body)
    {
        switch (body.Type)
        {
            case "entity":
                var entity = BuildGenericOutput(body, body.ToEntityEvent());
                OnEntityPairing?.Invoke(this, entity);
                ParsePairingEntity(body);
                break;
            case "server":
                var server = BuildGenericOutput(body, body.ToServerEvent());
                OnServerPairing?.Invoke(this, server);
                break;
            default:
                Logger.LogUnknownPairingType(body.Type);
                break;
        }
    }

    /// <summary>
    /// Handles entity-specific pairing notifications and dispatches events based on entity type.
    /// </summary>
    /// <param name="body">The <see cref="Body"/> of the notification.</param>
    /// <remarks>
    /// Invokes <see cref="OnSmartSwitchPairing"/>, <see cref="OnSmartAlarmPairing"/>, and <see cref="OnStorageMonitorPairing"/>.
    /// </remarks>
    private void ParsePairingEntity(Body body)
    {
        var response = BuildGenericOutput(body, body.ToEntityId());

        switch (body.EntityType)
        {
            case 1:
                OnSmartSwitchPairing?.Invoke(this, response);
                break;
            case 2:
                OnSmartAlarmPairing?.Invoke(this, response);
                break;
            case 3:
                OnStorageMonitorPairing?.Invoke(this, response);
                break;
            default:
                Logger.LogUnknownEntityType(body.EntityType);
                break;
        }
    }
}
