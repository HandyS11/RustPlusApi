using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm.Interfaces;

/// <summary>Extends <see cref="IRustPlusFcmSocket"/> with typed Rust+ pairing and alarm events.</summary>
public interface IRustPlusFcm : IRustPlusFcmSocket
{
    /// <summary>Raised when any pairing FCM message is received.</summary>
    event EventHandler<FcmMessage>? OnPairing;

    /// <summary>Raised when an entity pairing notification is received.</summary>
    event EventHandler<Notification<EntityEvent?>>? OnEntityPairing;

    /// <summary>Raised when a server pairing notification is received.</summary>
    event EventHandler<Notification<ServerEvent?>>? OnServerPairing;

    /// <summary>Raised when a smart switch pairing notification is received.</summary>
    event EventHandler<Notification<ulong?>>? OnSmartSwitchPairing;

    /// <summary>Raised when a smart alarm pairing notification is received.</summary>
    event EventHandler<Notification<ulong?>>? OnSmartAlarmPairing;

    /// <summary>Raised when a storage monitor pairing notification is received.</summary>
    event EventHandler<Notification<ulong?>>? OnStorageMonitorPairing;

    /// <summary>Raised when a smart alarm is triggered. Payload is an <see cref="AlarmNotification"/>.</summary>
    event EventHandler<AlarmNotification?>? OnAlarmTriggered;
}
