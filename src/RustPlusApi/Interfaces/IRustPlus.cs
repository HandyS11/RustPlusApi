using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Clans;
using RustPlusApi.Data.Entities;
using RustPlusApi.Data.Events;

namespace RustPlusApi.Interfaces;

public interface IRustPlus : IRustPlusSocket
{
    event EventHandler<SmartSwitchEventArg>? OnSmartSwitchTriggered;
    event EventHandler<StorageMonitorEventArg>? OnStorageMonitorTriggered;
    event EventHandler<TeamMessageEventArg>? OnTeamChatReceived;
    event EventHandler<ClanMessageEventArg>? OnClanChatReceived;
    event EventHandler<ClanChangedEventArg>? OnClanChanged;
    event EventHandler<CameraRaysEventArg>? OnCameraRaysReceived;

    Task<Response<SubscriptionInfo?>> CheckSubscriptionAsync(uint alarmId);
    Task<Response<AlarmInfo?>> GetAlarmInfoAsync(uint entityId);
    Task<Response<ClanInfo?>> GetClanInfoAsync();
    Task<Response<bool?>> SetClanMotdAsync(string message);
    Task<Response<ClanChatInfo?>> GetClanChatAsync();
    Task<Response<bool?>> SendClanMessageAsync(string message);
    Task<Response<NexusAuth?>> GetNexusAuthAsync(string appKey);
    Task<Response<CameraInfo?>> SubscribeToCameraAsync(string cameraId);
    Task<Response<bool?>> SendCameraInputAsync(CameraButtons buttons, float mouseDeltaX = 0, float mouseDeltaY = 0);
    Task<Response<bool?>> UnsubscribeFromCameraAsync();
    Task<Response<ServerInfo?>> GetInfoAsync();
    Task<Response<ServerMap?>> GetMapAsync();
    Task<Response<MapMarkers?>> GetMapMarkersAsync();
    Task<Response<SmartSwitchInfo?>> GetSmartSwitchInfoAsync(uint entityId);
    Task<Response<StorageMonitorInfo?>> GetStorageMonitorInfoAsync(uint entityId);
    Task<Response<TeamChatInfo?>> GetTeamChatAsync();
    Task<Response<TeamInfo?>> GetTeamInfoAsync();
    Task<Response<TimeInfo?>> GetTimeAsync();
    Task<Response<bool?>> PromoteToLeaderAsync(ulong steamId);
    Task<Response<TeamMessage?>> SendTeamMessageAsync(string message);
    Task<Response<SmartSwitchInfo?>> SetSmartSwitchValueAsync(uint smartSwitchId, bool smartSwitchValue);
    Task<Response<bool?>> SetSubscriptionAsync(uint entityId, bool doSubscribe = true);
    Task<Response<SmartSwitchInfo?>> StrobeSmartSwitchAsync(uint entityId, int timeoutMilliseconds = 1000, bool value = true);
    Task<Response<SmartSwitchInfo?>> ToggleSmartSwitchAsync(uint entityId);
}