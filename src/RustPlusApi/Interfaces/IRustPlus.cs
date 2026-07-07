using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Clans;
using RustPlusApi.Data.Entities;
using RustPlusApi.Data.Events;

namespace RustPlusApi.Interfaces;

/// <summary>High-level Rust+ API contract — typed events and all supported server commands.</summary>
public interface IRustPlus : IRustPlusSocket
{
    /// <summary>Occurs when an <c>EntityChanged</c> broadcast is classified as a binary-state smart device
    /// (a smart switch or a smart alarm): the payload carries no container state (no items, no
    /// capacity, no protection). The broadcast omits the entity type, so a storage broadcast whose
    /// payload is only <c>value</c> is indistinguishable from a switch and lands here too — route on
    /// <see cref="OnEntityChanged"/> with your paired entity ids when that matters.</summary>
    event EventHandler<SmartDeviceEventArg>? OnSmartDeviceTriggered;

    /// <summary>Occurs when an <c>EntityChanged</c> broadcast is classified as a storage monitor: the payload
    /// carries items, a capacity, or tool-cupboard protection. Storage broadcasts with
    /// <c>value == true</c> and no items carry no contents snapshot and are NOT raised here (they
    /// remain observable via <see cref="OnEntityChanged"/>). Tool-cupboard broadcasts are sometimes
    /// partial — <c>capacity</c> may be absent and only the protection flag identifies them.</summary>
    event EventHandler<StorageMonitorEventArg>? OnStorageMonitorTriggered;

    /// <summary>Raised for every <c>EntityChanged</c> broadcast, before any device-type heuristic,
    /// with the full raw payload. The broadcast carries no entity type; consumers that know their
    /// paired entity ids should route on <see cref="EntityChangedEventArg.Id"/>.</summary>
    event EventHandler<EntityChangedEventArg>? OnEntityChanged;

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
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<SubscriptionInfo?>> CheckSubscriptionAsync(ulong alarmId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the current state of a smart alarm entity.</summary>
    /// <param name="entityId">Entity ID of the alarm.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <remarks>The underlying <c>getEntityInfo</c> request also subscribes this connection to the
    /// entity's <c>EntityChanged</c> broadcasts server-side — even when the read itself fails on a
    /// type mismatch.</remarks>
    Task<Response<SmartDeviceInfo?>> GetAlarmInfoAsync(ulong entityId, CancellationToken cancellationToken = default);

    /// <summary>Returns the full clan snapshot for the authenticated player's clan.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<ClanInfo?>> GetClanInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the clan's message of the day.</summary>
    /// <param name="message">The new MOTD text.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response> SetClanMotdAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>Returns recent clan chat messages.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<ClanChatInfo?>> GetClanChatAsync(CancellationToken cancellationToken = default);

    /// <summary>Posts a message to the clan chat.</summary>
    /// <param name="message">The message text to send.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response> SendClanMessageAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>Obtains a Nexus cross-server authentication token.</summary>
    /// <param name="appKey">The application key identifying the target Nexus server.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<NexusAuth?>> GetNexusAuthAsync(string appKey, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to a camera's ray stream and returns its info.</summary>
    /// <param name="cameraId">In-game identifier of the camera entity.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<CameraInfo?>> SubscribeToCameraAsync(string cameraId, CancellationToken cancellationToken = default);

    /// <summary>Sends movement / action input to the currently subscribed camera.</summary>
    /// <param name="buttons">Bitmask of buttons to press.</param>
    /// <param name="mouseDeltaX">Horizontal mouse delta.</param>
    /// <param name="mouseDeltaY">Vertical mouse delta.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response> SendCameraInputAsync(CameraButtons buttons,
        float mouseDeltaX = 0,
        float mouseDeltaY = 0,
        CancellationToken cancellationToken = default);

    /// <summary>Unsubscribes from the currently subscribed camera.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response> UnsubscribeFromCameraAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns server metadata (name, map, player counts, etc.).</summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<ServerInfo?>> GetInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the server map image and monument list.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<ServerMap?>> GetMapAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all current map markers (players, cargo ship, vending machines, etc.).</summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<MapMarkers?>> GetMapMarkersAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the state of a binary-state smart device (a smart switch or a smart alarm),
    /// whichever of the two types the entity actually is.</summary>
    /// <param name="entityId">Entity ID of the smart device.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <remarks>The underlying <c>getEntityInfo</c> request also subscribes this connection to the
    /// entity's <c>EntityChanged</c> broadcasts server-side — even when the read itself fails on a
    /// type mismatch.</remarks>
    Task<Response<SmartDeviceInfo?>> GetSmartDeviceInfoAsync(ulong entityId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the current state of a smart switch entity.</summary>
    /// <param name="entityId">Entity ID of the smart switch.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <remarks>The underlying <c>getEntityInfo</c> request also subscribes this connection to the
    /// entity's <c>EntityChanged</c> broadcasts server-side — even when the read itself fails on a
    /// type mismatch.</remarks>
    Task<Response<SmartDeviceInfo?>> GetSmartSwitchInfoAsync(ulong entityId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the current contents and protection state of a storage monitor.</summary>
    /// <param name="entityId">Entity ID of the storage monitor.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <remarks>The underlying <c>getEntityInfo</c> request also subscribes this connection to the
    /// entity's <c>EntityChanged</c> broadcasts server-side — even when the read itself fails on a
    /// type mismatch.</remarks>
    Task<Response<StorageMonitorInfo?>> GetStorageMonitorInfoAsync(ulong entityId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns recent team chat messages.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<TeamChatInfo?>> GetTeamChatAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the full team snapshot including all member statuses and map notes.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<TeamInfo?>> GetTeamInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the current in-game time and day/night cycle parameters.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<TimeInfo?>> GetTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>Promotes a team member to team leader.</summary>
    /// <param name="steamId">Steam64 ID of the member to promote.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response> PromoteToLeaderAsync(ulong steamId, CancellationToken cancellationToken = default);

    /// <summary>Posts a message to the team chat.</summary>
    /// <param name="message">The message text to send.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<TeamMessage?>> SendTeamMessageAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>Sets a smart switch to a specific on/off state.</summary>
    /// <param name="smartSwitchId">Entity ID of the smart switch.</param>
    /// <param name="smartSwitchValue"><see langword="true"/> to turn on, <see langword="false"/> to turn off.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<SmartDeviceInfo?>> SetSmartSwitchValueAsync(ulong smartSwitchId,
        bool smartSwitchValue,
        CancellationToken cancellationToken = default);

    /// <summary>Subscribes or unsubscribes from push notifications for a smart alarm.</summary>
    /// <param name="entityId">Entity ID of the alarm.</param>
    /// <param name="doSubscribe"><see langword="true"/> to subscribe, <see langword="false"/> to unsubscribe.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response> SetSubscriptionAsync(ulong entityId,
        bool doSubscribe = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulses a smart switch on then off (or off then on) with the specified delay, implementing a
    /// simple strobe / one-shot trigger pattern.
    /// </summary>
    /// <param name="entityId">Entity ID of the smart switch.</param>
    /// <param name="timeoutMilliseconds">Duration to hold the initial state before reverting, in milliseconds.</param>
    /// <param name="value">Initial state to pulse the switch to.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<SmartDeviceInfo?>> StrobeSmartSwitchAsync(ulong entityId,
        int timeoutMilliseconds = 1000,
        bool value = true,
        CancellationToken cancellationToken = default);

    /// <summary>Toggles a smart switch — turns it on if off, or off if on.</summary>
    /// <param name="entityId">Entity ID of the smart switch.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task<Response<SmartDeviceInfo?>> ToggleSmartSwitchAsync(ulong entityId,
        CancellationToken cancellationToken = default);
}
