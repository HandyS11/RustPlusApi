using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm.Interfaces;

public interface IRustPlusFcm : IRustPlusFcmSocket
{
    event EventHandler<FcmMessage>? OnParing;
    event EventHandler<Notification<EntityEvent?>>? OnEntityParing;
    event EventHandler<Notification<ServerEvent?>>? OnServerPairing;
    event EventHandler<Notification<int?>>? OnSmartSwitchParing;
    event EventHandler<Notification<int?>>? OnSmartAlarmParing;
    event EventHandler<Notification<int?>>? OnStorageMonitorParing;
    event EventHandler<AlarmEvent?>? OnAlarmTriggered;
}