using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Clans;
using RustPlusApi.Data.Entities;
using RustPlusApi.Data.Events;

namespace RustPlusApi.Interfaces;

/// <summary>High-level Rust+ API contract — typed events and all supported server commands.</summary>
public interface IRustPlus : IRustPlusSocket
{
    /// <summary>Raised when a subscribed smart switch changes state.</summary>
    event EventHandler<SmartSwitchEventArg>? OnSmartSwitchTriggered;

    /// <summary>Raised when a subscribed storage monitor reports a change.</summary>
    event EventHandler<StorageMonitorEventArg>? OnStorageMonitorTriggered;

    /// <summary>Raised when a new team chat message arrives.</summary>
    event EventHandler<TeamMessageEventArg>? OnTeamChatReceived;

    /// <summary>Raised when a new clan chat message arrives.</summary>
    event EventHandler<ClanMessageEventArg>? OnClanChatReceived;

    /// <summary>Raised when the clan snapshot changes.</summary>
    event EventHandler<ClanChangedEventArg>? OnClanChanged;

    /// <summary>Raised when a camera rays broadcast is received for the subscribed camera.</summary>
    event EventHandler<CameraRaysEventArg>? OnCameraRaysReceived;

    /// <summary>Checks whether the player is subscribed to push notifications from a smart alarm.</summary>
    /// <param name="alarmId">Entity ID of the smart alarm.</param>
    Task<Response<SubscriptionInfo?>> CheckSubscriptionAsync(ulong alarmId);

    /// <summary>Returns the current state of a smart alarm entity.</summary>
    /// <param name="entityId">Entity ID of the alarm.</param>
    Task<Response<AlarmInfo?>> GetAlarmInfoAsync(ulong entityId);

    /// <summary>Returns the full clan snapshot for the authenticated player's clan.</summary>
    Task<Response<ClanInfo?>> GetClanInfoAsync();

    /// <summary>Sets the clan's message of the day.</summary>
    /// <param name="message">The new MOTD text.</param>
    Task<Response<bool?>> SetClanMotdAsync(string message);

    /// <summary>Returns recent clan chat messages.</summary>
    Task<Response<ClanChatInfo?>> GetClanChatAsync();

    /// <summary>Posts a message to the clan chat.</summary>
    /// <param name="message">The message text to send.</param>
    Task<Response<bool?>> SendClanMessageAsync(string message);

    /// <summary>Obtains a Nexus cross-server authentication token.</summary>
    /// <param name="appKey">The application key identifying the target Nexus server.</param>
    Task<Response<NexusAuth?>> GetNexusAuthAsync(string appKey);

    /// <summary>Subscribes to a camera's ray stream and returns its info.</summary>
    /// <param name="cameraId">In-game identifier of the camera entity.</param>
    Task<Response<CameraInfo?>> SubscribeToCameraAsync(string cameraId);

    /// <summary>Sends movement / action input to the currently subscribed camera.</summary>
    /// <param name="buttons">Bitmask of buttons to press.</param>
    /// <param name="mouseDeltaX">Horizontal mouse delta.</param>
    /// <param name="mouseDeltaY">Vertical mouse delta.</param>
    Task<Response<bool?>> SendCameraInputAsync(CameraButtons buttons, float mouseDeltaX = 0, float mouseDeltaY = 0);

    /// <summary>Unsubscribes from the currently subscribed camera.</summary>
    Task<Response<bool?>> UnsubscribeFromCameraAsync();

    /// <summary>Returns server metadata (name, map, player counts, etc.).</summary>
    Task<Response<ServerInfo?>> GetInfoAsync();

    /// <summary>Returns the server map image and monument list.</summary>
    Task<Response<ServerMap?>> GetMapAsync();

    /// <summary>Returns all current map markers (players, cargo ship, vending machines, etc.).</summary>
    Task<Response<MapMarkers?>> GetMapMarkersAsync();

    /// <summary>Returns the current state of a smart switch entity.</summary>
    /// <param name="entityId">Entity ID of the smart switch.</param>
    Task<Response<SmartSwitchInfo?>> GetSmartSwitchInfoAsync(ulong entityId);

    /// <summary>Returns the current contents and protection state of a storage monitor.</summary>
    /// <param name="entityId">Entity ID of the storage monitor.</param>
    Task<Response<StorageMonitorInfo?>> GetStorageMonitorInfoAsync(ulong entityId);

    /// <summary>Returns recent team chat messages.</summary>
    Task<Response<TeamChatInfo?>> GetTeamChatAsync();

    /// <summary>Returns the full team snapshot including all member statuses and map notes.</summary>
    Task<Response<TeamInfo?>> GetTeamInfoAsync();

    /// <summary>Returns the current in-game time and day/night cycle parameters.</summary>
    Task<Response<TimeInfo?>> GetTimeAsync();

    /// <summary>Promotes a team member to team leader.</summary>
    /// <param name="steamId">Steam64 ID of the member to promote.</param>
    Task<Response<bool?>> PromoteToLeaderAsync(ulong steamId);

    /// <summary>Posts a message to the team chat.</summary>
    /// <param name="message">The message text to send.</param>
    Task<Response<TeamMessage?>> SendTeamMessageAsync(string message);

    /// <summary>Sets a smart switch to a specific on/off state.</summary>
    /// <param name="smartSwitchId">Entity ID of the smart switch.</param>
    /// <param name="smartSwitchValue"><see langword="true"/> to turn on, <see langword="false"/> to turn off.</param>
    Task<Response<SmartSwitchInfo?>> SetSmartSwitchValueAsync(ulong smartSwitchId, bool smartSwitchValue);

    /// <summary>Subscribes or unsubscribes from push notifications for a smart alarm.</summary>
    /// <param name="entityId">Entity ID of the alarm.</param>
    /// <param name="doSubscribe"><see langword="true"/> to subscribe, <see langword="false"/> to unsubscribe.</param>
    Task<Response<bool?>> SetSubscriptionAsync(ulong entityId, bool doSubscribe = true);

    /// <summary>
    /// Pulses a smart switch on then off (or off then on) with the specified delay, implementing a
    /// simple strobe / one-shot trigger pattern.
    /// </summary>
    /// <param name="entityId">Entity ID of the smart switch.</param>
    /// <param name="timeoutMilliseconds">Duration to hold the initial state before reverting, in milliseconds.</param>
    /// <param name="value">Initial state to pulse the switch to.</param>
    Task<Response<SmartSwitchInfo?>> StrobeSmartSwitchAsync(ulong entityId, int timeoutMilliseconds = 1000, bool value = true);

    /// <summary>Toggles a smart switch — turns it on if off, or off if on.</summary>
    /// <param name="entityId">Entity ID of the smart switch.</param>
    Task<Response<SmartSwitchInfo?>> ToggleSmartSwitchAsync(ulong entityId);
}
