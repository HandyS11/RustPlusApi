using System.Diagnostics;
using System.Text.Json;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Extensions;
using static RustPlusApi.Fcm.Utils.ResponseHelper;

namespace RustPlusApi.Fcm;

/// <summary>
/// Represents a RustPlus FCM listener, which processes FCM notifications for RustPlus.
/// Inherits from <see cref="RustPlusFcmListenerClient"/>.
/// </summary>
/// <param name="credentials">The <see cref="Credentials"/> used for authentication.</param>
/// <param name="persistentIds">The collection of persistent IDs as <see cref="ICollection{T}"/> of <see cref="string"/>.</param>
public class RustPlusFcmListener(Credentials credentials, ICollection<string>? persistentIds = null)
    : RustPlusFcmListenerClient(credentials, persistentIds)
{
    /// <summary>
    /// Occurs when a pairing <see cref="FcmMessage"/> is received.
    /// </summary>
    public event EventHandler<FcmMessage>? OnParing;

    /// <summary>
    /// Occurs when an entity pairing notification is received.
    /// </summary>
    /// <remarks>
    /// The event data is a <see cref="Notification{T}"/> containing an <see cref="EntityEvent"/>.
    /// </remarks>
    public event EventHandler<Notification<EntityEvent?>>? OnEntityParing;

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
    /// The event data is a <see cref="Notification{T}"/> containing an <see cref="int"/> entity ID.
    /// </remarks>
    public event EventHandler<Notification<int?>>? OnSmartSwitchParing;

    /// <summary>
    /// Occurs when a smart alarm pairing notification is received.
    /// </summary>
    /// <remarks>
    /// The event data is a <see cref="Notification{T}"/> containing an <see cref="int"/> entity ID.
    /// </remarks>
    public event EventHandler<Notification<int?>>? OnSmartAlarmParing;

    /// <summary>
    /// Occurs when a storage monitor pairing notification is received.
    /// </summary>
    /// <remarks>
    /// The event data is a <see cref="Notification{T}"/> containing an <see cref="int"/> entity ID.
    /// </remarks>
    public event EventHandler<Notification<int?>>? OnStorageMonitorParing;

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
    /// Invokes <see cref="OnParing"/>, <see cref="OnAlarmTriggered"/>, and calls <see cref="ParsePairing"/> as appropriate.
    /// </remarks>
    protected override void ParseNotification(FcmMessage message)
    {
        switch (message.Data.ChannelId)
        {
            case "pairing":
                OnParing?.Invoke(this, message);
                ParsePairing(message.Data.Body);
                break;
            case "alarm":
                OnAlarmTriggered?.Invoke(this, message.Data.ToAlarmEvent());
                break;
            default:
                Debug.WriteLine($"Unknown channel: {message.Data.ChannelId}");
                break;
        }
    }

    /// <summary>
    /// Handles pairing notifications by type and dispatches related events.
    /// </summary>
    /// <param name="body">The <see cref="Body"/> of the notification.</param>
    /// <remarks>
    /// Invokes <see cref="OnEntityParing"/>, <see cref="OnServerPairing"/>, and calls <see cref="ParsePairingEntity"/>.
    /// </remarks>
    private void ParsePairing(Body body)
    {
        switch (body.Type)
        {
            case "entity":
                var entity = BuildGenericOutput(body, body.ToEntityEvent());
                OnEntityParing?.Invoke(this, entity);
                ParsePairingEntity(body);
                break;
            case "server":
                var server = BuildGenericOutput(body, body.ToServerEvent());
                OnServerPairing?.Invoke(this, server);
                break;
            default:
                Debug.WriteLine($"Unknown pairing type: {body.Type}");
                break;
        }
    }

    /// <summary>
    /// Handles entity-specific pairing notifications and dispatches events based on entity type.
    /// </summary>
    /// <param name="body">The <see cref="Body"/> of the notification.</param>
    /// <remarks>
    /// Invokes <see cref="OnSmartSwitchParing"/>, <see cref="OnSmartAlarmParing"/>, and <see cref="OnStorageMonitorParing"/>.
    /// </remarks>
    private void ParsePairingEntity(Body body)
    {
        var response = BuildGenericOutput(body, body.ToEntityId());

        switch (body.EntityType)
        {
            case 1:
                OnSmartSwitchParing?.Invoke(this, response);
                break;
            case 2:
                OnSmartAlarmParing?.Invoke(this, response);
                break;
            case 3:
                OnStorageMonitorParing?.Invoke(this, response);
                break;
            default:
                Debug.WriteLine($"Unknown entity type: {body.EntityType}");
                break;
        }
    }
}
