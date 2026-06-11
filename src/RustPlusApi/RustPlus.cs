using Microsoft.Extensions.Logging;
using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Clans;
using RustPlusApi.Data.Entities;
using RustPlusApi.Data.Events;
using RustPlusApi.Extensions;
using RustPlusApi.Interfaces;
using RustPlusApi.Utils;
using RustPlusContracts;
using ClanInfo = RustPlusApi.Data.Clans.ClanInfo;

// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi;

/// <summary>
/// Initializes a new instance of the <see cref="RustPlus"/> class,
/// connecting to a Rust+ server using the specified parameters.
/// </summary>
/// <param name="connection">The server endpoint and player credentials to connect as.</param>
/// <param name="options">Tuning options (timeouts, keep-alive, buffer size); defaults are used when <see langword="null"/>.</param>
/// <param name="loggerFactory">Routes the client's diagnostics into your logging stack; logging is
/// disabled (a no-op <c>NullLogger</c>) when <see langword="null"/>.</param>
/// <seealso cref="RustPlusSocket"/>
public class RustPlus(
    RustPlusConnection connection,
    RustPlusSocketOptions? options = null,
    ILoggerFactory? loggerFactory = null)
    : RustPlusSocket(connection, options, loggerFactory), IRustPlus
{
    /// <summary>
    /// Occurs when a <see cref="SmartSwitchEventArg"/> is triggered by a smart switch or alarm.
    /// </summary>
    public event EventHandler<SmartSwitchEventArg>? OnSmartSwitchTriggered;

    /// <summary>
    /// Occurs when a <see cref="StorageMonitorEventArg"/> is triggered by a storage monitor.
    /// </summary>
    public event EventHandler<StorageMonitorEventArg>? OnStorageMonitorTriggered;

    /// <summary>
    /// Occurs when a team chat message is received, providing a <see cref="TeamMessageEventArg"/>.
    /// </summary>
    public event EventHandler<TeamMessageEventArg>? OnTeamChatReceived;

    /// <summary>
    /// Occurs when a clan chat message is received, providing a <see cref="ClanMessageEventArg"/>.
    /// </summary>
    public event EventHandler<ClanMessageEventArg>? OnClanChatReceived;

    /// <summary>
    /// Occurs when the clan changes (roles, members, MOTD, …), providing a <see cref="ClanChangedEventArg"/>.
    /// </summary>
    public event EventHandler<ClanChangedEventArg>? OnClanChanged;

    /// <summary>
    /// Occurs when a camera frame is received for the subscribed camera, providing a <see cref="CameraRaysEventArg"/>.
    /// </summary>
    public event EventHandler<CameraRaysEventArg>? OnCameraRaysReceived;

    /// <summary>
    /// Parses the notification received from the Rust+ server.
    /// </summary>
    /// <param name="broadcast">The broadcast received from the server.</param>
    protected override void ParseNotification(AppBroadcast? broadcast)
    {
        if (broadcast is null)
        {
            return;
        }

        if (broadcast.EntityChanged is not null)
        {
            // There is no physical difference between a SmartSwitch and an Alarm
            // If you check the status of an alarm, it will return the same as a smart switch
            if (broadcast.EntityChanged.Payload.Capacity is 0)
            {
                OnSmartSwitchTriggered?.Invoke(this, broadcast.EntityChanged.ToSmartSwitchEvent());
            }
            else
            {
                OnStorageMonitorTriggered?.Invoke(this, broadcast.EntityChanged.ToStorageMonitorEvent());
            }

            return;
        }

        if (broadcast.TeamMessage is not null)
        {
            OnTeamChatReceived?.Invoke(this, broadcast.TeamMessage.Message.ToTeamMessageEvent());
            return;
        }

        if (broadcast.ClanMessage is not null)
        {
            OnClanChatReceived?.Invoke(this, broadcast.ClanMessage.ToClanMessageEvent());
            return;
        }

        if (broadcast.ClanChanged is not null)
        {
            OnClanChanged?.Invoke(this, broadcast.ClanChanged.ToClanChangedEvent());
            return;
        }

        if (broadcast.CameraRays is not null)
        {
            OnCameraRaysReceived?.Invoke(this, broadcast.CameraRays.ToCameraRaysEvent());
            return;
        }

        Logger.LogUnknownBroadcast(broadcast);
    }

    /// <summary>
    /// Processes the request asynchronously and returns the result.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="request">The request to be processed.</param>
    /// <param name="successSelector">The function to select the result from the response.</param>
    /// <param name="broadcastReplyMatcher">When non-null, the success reply is delivered as a broadcast
    /// (no seq) and is matched by this predicate, so the selector reads <c>response.Broadcast</c> rather
    /// than <c>response.Response</c>. Unrelated broadcasts stay pure notifications.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the processed result.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <exception cref="InvalidOperationException">Thrown when the client is not connected.</exception>
    protected async Task<Response<T?>> ProcessRequestAsync<T>(AppRequest request,
        Func<AppMessage, T> successSelector,
        Func<AppBroadcast, bool>? broadcastReplyMatcher = null,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(request, broadcastReplyMatcher, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IsError(response)
            ? ResponseHelper.BuildGenericOutput<T>(false, default!, GetErrorMessage(response))
            : ResponseHelper.BuildGenericOutput(true, successSelector(response));
    }

    /// <summary>
    /// Processes an acknowledge-only request asynchronously: success is the absence of a server
    /// error, and no payload is returned.
    /// </summary>
    /// <param name="request">The request to be processed.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A <see cref="Task{TResult}"/> whose result is a payload-free <see cref="Response"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the client is not connected.</exception>
    protected async Task<Response> ProcessAckRequestAsync(AppRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);

        return IsError(response)
            ? ResponseHelper.BuildAckOutput(false, GetErrorMessage(response))
            : ResponseHelper.BuildAckOutput(true);
    }

    /// <summary>
    /// Retrieves the information of an entity asynchronously.
    /// </summary>
    /// <typeparam name="T">The type of the entity information.</typeparam>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="selector">The function to select the entity information from the response.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the entity information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    protected async Task<Response<T?>> GetEntityInfoAsync<T>(ulong entityId,
        Func<AppMessage, T> selector,
        CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            EntityId = entityId, GetEntityInfo = new AppEmpty()
        };
        return await ProcessRequestAsync(request, selector, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks the subscription status of an alarm asynchronously.
    /// </summary>
    /// <param name="alarmId">The ID of the alarm entity.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the subscription information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<SubscriptionInfo?>> CheckSubscriptionAsync(ulong alarmId,
        CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            CheckSubscription = new AppEmpty(), EntityId = alarmId
        };
        return await ProcessRequestAsync<SubscriptionInfo?>(
            request,
            r => r.Response.Flag.ToSubscriptionInfo(), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the information of an alarm asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the alarm entity.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the alarm information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<AlarmInfo?>> GetAlarmInfoAsync(ulong entityId,
        CancellationToken cancellationToken = default)
    {
        return await GetEntityInfoAsync<AlarmInfo?>(entityId, r => r.Response.EntityInfo.ToAlarmInfo(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the clan information asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the clan information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<ClanInfo?>> GetClanInfoAsync(CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            GetClanInfo = new AppEmpty()
        };
        return await ProcessRequestAsync<ClanInfo?>(request, r => r.Response.ClanInfo.ToClanInfo(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the clan message of the day (MOTD) asynchronously.
    /// </summary>
    /// <param name="message">The message to set as the clan MOTD.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a payload-free <see cref="Response"/> indicating the success of the operation.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response> SetClanMotdAsync(string message, CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            SetClanMotd = new AppSendMessage
            {
                Message = message
            }
        };
        return await ProcessAckRequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the clan chat asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the clan chat information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<ClanChatInfo?>> GetClanChatAsync(CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            GetClanChat = new AppEmpty()
        };
        return await ProcessRequestAsync<ClanChatInfo?>(request, r => r.Response.ClanChat.ToClanChatInfo(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a clan message asynchronously.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a payload-free <see cref="Response"/> indicating the success of the operation.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response> SendClanMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            SendClanMessage = new AppSendMessage
            {
                Message = message
            }
        };
        return await ProcessAckRequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the Nexus authentication asynchronously.
    /// </summary>
    /// <param name="appKey">The app key for Nexus authentication.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the Nexus authentication.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<NexusAuth?>> GetNexusAuthAsync(string appKey,
        CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            GetNexusAuth = new AppGetNexusAuth
            {
                AppKey = appKey
            }
        };
        return await ProcessRequestAsync<NexusAuth?>(request, r => r.Response.NexusAuth.ToNexusAuth(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Subscribes to a camera asynchronously, starting the <see cref="OnCameraRaysReceived"/> stream.
    /// </summary>
    /// <param name="cameraId">The identifier of the camera/CCTV entity to subscribe to.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the camera information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<CameraInfo?>> SubscribeToCameraAsync(string cameraId,
        CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            CameraSubscribe = new AppCameraSubscribe
            {
                CameraId = cameraId
            }
        };
        return await ProcessRequestAsync<CameraInfo?>(
                request,
                r => r.Response.CameraSubscribeInfo.ToCameraInfo(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Sends input (movement/mouse/buttons) to the subscribed camera asynchronously.
    /// </summary>
    /// <param name="buttons">The pressed <see cref="CameraButtons"/> bitmask.</param>
    /// <param name="mouseDeltaX">The horizontal mouse delta.</param>
    /// <param name="mouseDeltaY">The vertical mouse delta.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a payload-free <see cref="Response"/> indicating the success of the operation.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response> SendCameraInputAsync(CameraButtons buttons,
        float mouseDeltaX = 0,
        float mouseDeltaY = 0,
        CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            CameraInput = new AppCameraInput
            {
                Buttons = (int)buttons,
                MouseDelta = new Vector2
                {
                    X = mouseDeltaX, Y = mouseDeltaY
                }
            }
        };
        return await ProcessAckRequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Unsubscribes from the currently subscribed camera asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a payload-free <see cref="Response"/> indicating the success of the operation.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response> UnsubscribeFromCameraAsync(CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            CameraUnsubscribe = new AppEmpty()
        };
        return await ProcessAckRequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the server information asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the server information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<ServerInfo?>> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            GetInfo = new AppEmpty()
        };
        return await ProcessRequestAsync<ServerInfo?>(request, r => r.Response.Info.ToServerInfo(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the server map asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the server map.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<ServerMap?>> GetMapAsync(CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            GetMap = new AppEmpty()
        };
        return await ProcessRequestAsync<ServerMap?>(request, r => r.Response.Map.ToServerMap(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the map markers asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the map markers.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<MapMarkers?>> GetMapMarkersAsync(CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            GetMapMarkers = new AppEmpty()
        };
        return await ProcessRequestAsync<MapMarkers?>(request, r => r.Response.MapMarkers.ToMapMarkers(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the information of a smart switch asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the smart switch entity.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the smart switch information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<SmartSwitchInfo?>> GetSmartSwitchInfoAsync(ulong entityId,
        CancellationToken cancellationToken = default)
    {
        return await GetEntityInfoAsync<SmartSwitchInfo?>(
            entityId,
            r => r.Response.EntityInfo.ToSmartSwitchInfo(), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the information of a storage monitor asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the storage monitor entity.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the storage monitor information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<StorageMonitorInfo?>> GetStorageMonitorInfoAsync(ulong entityId,
        CancellationToken cancellationToken = default)
    {
        return await GetEntityInfoAsync<StorageMonitorInfo?>(
                entityId,
                r => r.Response.EntityInfo.ToStorageMonitorInfo(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the team chat information asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the team chat information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<TeamChatInfo?>> GetTeamChatAsync(CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            GetTeamChat = new AppEmpty()
        };
        return await ProcessRequestAsync<TeamChatInfo?>(
            request,
            r => r.Response.TeamChat.ToTeamChatInfo(), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the team information asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the team information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<TeamInfo?>> GetTeamInfoAsync(CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            GetTeamInfo = new AppEmpty()
        };
        return await ProcessRequestAsync<TeamInfo?>(request, r => r.Response.TeamInfo.ToTeamInfo(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the current time information asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the time information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<TimeInfo?>> GetTimeAsync(CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            GetTime = new AppEmpty()
        };
        return await ProcessRequestAsync<TimeInfo?>(request, r => r.Response.Time.ToTimeInfo(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Promotes a player to leader asynchronously.
    /// </summary>
    /// <param name="steamId">The Steam ID of the player to promote.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a payload-free <see cref="Response"/> indicating the success of the operation.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response> PromoteToLeaderAsync(ulong steamId, CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            PromoteToLeader = new AppPromoteToLeader
            {
                SteamId = steamId
            }
        };
        return await ProcessAckRequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a team message asynchronously.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the team message.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<TeamMessage?>> SendTeamMessageAsync(string message,
        CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            SendTeamMessage = new AppSendMessage
            {
                Message = message
            }
        };
        return await ProcessRequestAsync<TeamMessage?>(
            request,
            // The live server acks sendTeamMessage with an immediate seq success {}; the team-chat
            // broadcast echoing the message follows separately. Whichever resolves the request first
            // must produce a result: the echo carries the full message (name, colour, server time),
            // the bare ack means the message was accepted, so reconstruct it from what we sent.
            r => r.Broadcast?.TeamMessage?.Message is { } echoed
                ? echoed.ToTeamMessage()
                : new TeamMessage
                {
                    SteamId = PlayerId, Name = string.Empty, Message = message, Time = DateTime.UtcNow
                },
            // Match only the broadcast echoing our own message (our Steam ID): another player's
            // message arriving first cannot be mistaken for the reply.
            broadcastReplyMatcher: b => b.TeamMessage?.Message?.SteamId == PlayerId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the value of a smart switch asynchronously.
    /// </summary>
    /// <param name="smartSwitchId">The ID of the smart switch entity.</param>
    /// <param name="smartSwitchValue">The value to set for the smart switch.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the updated smart switch information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<SmartSwitchInfo?>> SetSmartSwitchValueAsync(ulong smartSwitchId,
        bool smartSwitchValue,
        CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            EntityId = smartSwitchId,
            SetEntityValue = new AppSetEntityValue
            {
                Value = smartSwitchValue
            },
        };
        return await ProcessRequestAsync<SmartSwitchInfo?>(
            request,
            // The live server acks setEntityValue with an immediate seq success {}; the EntityChanged
            // broadcast follows separately — and not at all when the state did not change. Whichever
            // resolves the request first must produce a result: the broadcast carries the authoritative
            // state, the bare ack means the server accepted the set, so the state is the requested value.
            r => r.Broadcast?.EntityChanged is { } entityChanged
                ? entityChanged.ToSmartSwitchEvent()
                : new SmartSwitchEventArg
                {
                    Id = smartSwitchId, IsActive = smartSwitchValue
                },
            // Match only the EntityChanged broadcast for this switch: an unrelated broadcast
            // (team chat, another entity) cannot resolve this request.
            broadcastReplyMatcher: b => b.EntityChanged?.EntityId == smartSwitchId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the subscription status of an entity asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="doSubscribe">Specifies whether to subscribe or unsubscribe.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a payload-free <see cref="Response"/> indicating the success of the operation.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response> SetSubscriptionAsync(ulong entityId,
        bool doSubscribe = true,
        CancellationToken cancellationToken = default)
    {
        var request = new AppRequest
        {
            EntityId = entityId,
            SetSubscription = new AppFlag
            {
                Value = doSubscribe
            }
        };
        return await ProcessAckRequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Strobes a smart switch asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the smart switch entity.</param>
    /// <param name="timeoutMilliseconds">The duration of each state in milliseconds.</param>
    /// <param name="value">The initial value of the smart switch.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the updated smart switch information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<SmartSwitchInfo?>> StrobeSmartSwitchAsync(
        ulong entityId,
        int timeoutMilliseconds = 1000,
        bool value = true,
        CancellationToken cancellationToken = default)
    {
        var response = await SetSmartSwitchValueAsync(entityId, value, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            return response;
        }

        await Task.Delay(timeoutMilliseconds, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await SetSmartSwitchValueAsync(entityId, !value, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Toggles a smart switch asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the smart switch entity.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the updated smart switch information.</returns>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    public async Task<Response<SmartSwitchInfo?>> ToggleSmartSwitchAsync(ulong entityId,
        CancellationToken cancellationToken = default)
    {
        var entityInfo = await GetSmartSwitchInfoAsync(entityId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!entityInfo.IsSuccess)
        {
            return entityInfo;
        }

        var value = entityInfo.Data!.IsActive;
        return await SetSmartSwitchValueAsync(entityId, !value, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
