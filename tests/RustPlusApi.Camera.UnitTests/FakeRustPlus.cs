using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Clans;
using RustPlusApi.Data.Entities;
using RustPlusApi.Data.Events;
using RustPlusApi.Interfaces;
using AppMessage = RustPlusContracts.AppMessage;
using AppRequest = RustPlusContracts.AppRequest;

namespace RustPlusApi.Camera.UnitTests;

/// <summary>
/// An <see cref="IRustPlus"/> stand-in for the <see cref="CameraController"/> paths the mock-server
/// integration tests cannot reach: a subscribe or unsubscribe that <em>throws</em> rather than
/// returning a failed <see cref="Response"/>, and a client that still reports
/// <see cref="IsConnected"/> while its transport is broken.
/// </summary>
/// <remarks>Only the five camera members <see cref="CameraController"/> actually touches are
/// implemented; every other member throws, so a controller change that starts calling one fails
/// loudly here instead of silently passing against a stub that returns defaults.</remarks>
internal sealed class FakeRustPlus : IRustPlus
{
    /// <summary>Camera info handed back by a successful <see cref="SubscribeToCameraAsync"/>.</summary>
    internal static readonly CameraInfo DroneInfo = new()
    {
        Width = 100,
        Height = 50,
        ControlFlags = CameraControlFlags.Movement | CameraControlFlags.Mouse | CameraControlFlags.SprintAndDuck
    };

    private int _subscribeCount;
    private int _unsubscribeCount;

    /// <summary>Invoked by every <see cref="SubscribeToCameraAsync"/> call, with the 1-based call
    /// number, so a test can succeed once and then fail or throw on renewal.</summary>
    internal Func<int, CancellationToken, Task<Response<CameraInfo?>>> OnSubscribe { get; set; } =
        static (_, _) => Task.FromResult(new Response<CameraInfo?>
        {
            IsSuccess = true, Data = DroneInfo
        });

    /// <summary>Invoked by every <see cref="UnsubscribeFromCameraAsync"/> call.</summary>
    internal Func<Task<Response>> OnUnsubscribe { get; set; } =
        static () => Task.FromResult(new Response
        {
            IsSuccess = true
        });

    /// <summary>How many times a subscribe was requested (the initial one plus every renewal).</summary>
    internal int SubscribeCount => Volatile.Read(ref _subscribeCount);

    /// <summary>How many times an unsubscribe was requested.</summary>
    internal int UnsubscribeCount => Volatile.Read(ref _unsubscribeCount);

    /// <inheritdoc/>
    public bool IsConnected { get; set; } = true;

    /// <inheritdoc/>
    public event EventHandler<CameraRaysEventArg>? OnCameraRaysReceived;

    /// <inheritdoc/>
    public Task<Response<CameraInfo?>> SubscribeToCameraAsync(string cameraId,
        CancellationToken cancellationToken = default) =>
        OnSubscribe(Interlocked.Increment(ref _subscribeCount), cancellationToken);

    /// <inheritdoc/>
    public Task<Response> UnsubscribeFromCameraAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _unsubscribeCount);
        return OnUnsubscribe();
    }

    /// <inheritdoc/>
    public Task<Response> SendCameraInputAsync(CameraButtons buttons,
        float mouseDeltaX = 0,
        float mouseDeltaY = 0,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new Response
        {
            IsSuccess = true
        });

    /// <inheritdoc/>
    public void Dispose()
    {
        // Nothing to release: the fake owns no transport.
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => default;

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task DisconnectAsync(bool forceClose = false) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<SubscriptionInfo?>> CheckSubscriptionAsync(ulong alarmId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<SmartDeviceInfo?>> GetAlarmInfoAsync(ulong entityId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<ClanInfo?>> GetClanInfoAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response> SetClanMotdAsync(string message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<ClanChatInfo?>> GetClanChatAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response> SendClanMessageAsync(string message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<NexusAuth?>> GetNexusAuthAsync(string appKey,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<ServerInfo?>> GetInfoAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<ServerMap?>> GetMapAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<MapMarkers?>> GetMapMarkersAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<SmartDeviceInfo?>> GetSmartDeviceInfoAsync(ulong entityId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<SmartDeviceInfo?>> GetSmartSwitchInfoAsync(ulong entityId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<StorageMonitorInfo?>> GetStorageMonitorInfoAsync(ulong entityId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<TeamChatInfo?>> GetTeamChatAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<TeamInfo?>> GetTeamInfoAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<TimeInfo?>> GetTimeAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response> KickFromTeamAsync(ulong steamId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response> PromoteToLeaderAsync(ulong steamId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<TeamMessage?>> SendTeamMessageAsync(string message,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<SmartDeviceInfo?>> SetSmartSwitchValueAsync(ulong smartSwitchId,
        bool smartSwitchValue,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response> SetSubscriptionAsync(ulong entityId,
        bool doSubscribe = true,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<SmartDeviceInfo?>> StrobeSmartSwitchAsync(ulong entityId,
        int timeoutMilliseconds = 1000,
        bool value = true,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Response<SmartDeviceInfo?>> ToggleSmartSwitchAsync(ulong entityId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Raises <see cref="OnCameraRaysReceived"/> as the socket layer would.</summary>
    /// <param name="frame">The frame to publish.</param>
    internal void RaiseFrame(CameraRaysEventArg frame) => OnCameraRaysReceived?.Invoke(this, frame);

    // Everything below is outside this fake's purpose: the socket-lifecycle events are never
    // raised, and every non-camera call throws so a controller change that starts using one fails
    // loudly here rather than passing against a stub that quietly returns a default.
#pragma warning disable CS0067 // deliberately never raised: nothing under test subscribes to these

    /// <inheritdoc/>
    public event EventHandler<SmartDeviceEventArg>? OnSmartDeviceTriggered;

    /// <inheritdoc/>
    public event EventHandler<StorageMonitorEventArg>? OnStorageMonitorTriggered;

    /// <inheritdoc/>
    public event EventHandler<EntityChangedEventArg>? OnEntityChanged;

    /// <inheritdoc/>
    public event EventHandler<TeamMessageEventArg>? OnTeamChatReceived;

    /// <inheritdoc/>
    public event EventHandler<ClanMessageEventArg>? OnClanChatReceived;

    /// <inheritdoc/>
    public event EventHandler<TeamChangedEventArg>? OnTeamChanged;

    /// <inheritdoc/>
    public event EventHandler<ClanChangedEventArg>? OnClanChanged;

    /// <inheritdoc/>
    public event EventHandler? Connecting;

    /// <inheritdoc/>
    public event EventHandler? Connected;

    /// <inheritdoc/>
    public event EventHandler? SendingRequest;

    /// <inheritdoc/>
    public event EventHandler<AppRequest>? RequestSent;

    /// <inheritdoc/>
    public event EventHandler<AppMessage>? MessageReceived;

    /// <inheritdoc/>
    public event EventHandler<AppMessage>? NotificationReceived;

    /// <inheritdoc/>
    public event EventHandler<AppMessage>? ResponseReceived;

    /// <inheritdoc/>
    public event EventHandler? Disconnecting;

    /// <inheritdoc/>
    public event EventHandler? Disconnected;

    /// <inheritdoc/>
    public event EventHandler<Exception>? ErrorOccurred;

#pragma warning restore CS0067
}
