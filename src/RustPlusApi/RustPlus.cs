using System.Diagnostics;
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
/// <param name="server">The IP address of the Rust+ server.</param>
/// <param name="port">The port dedicated for the Rust+ companion app (not the one used to connect in-game).</param>
/// <param name="playerId">Your Steam ID.</param>
/// <param name="playerToken">Your player token acquired with FCM.</param>
/// <param name="useFacepunchProxy">Specifies whether to use the Facepunch proxy.</param>
/// <seealso cref="RustPlusSocket"/>
public class RustPlus(string server, int port, ulong playerId, int playerToken, bool useFacepunchProxy = false)
    : RustPlusSocket(server, port, playerId, playerToken, useFacepunchProxy), IRustPlus
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
        if (broadcast is null) return;

        if (broadcast.EntityChanged is not null)
        {
            // There is no physical difference between a SmartSwitch and an Alarm
            // If you check the status of an alarm, it will return the same as a smart switch
            if (broadcast.EntityChanged.Payload.Capacity is 0)
                OnSmartSwitchTriggered?.Invoke(this, broadcast.EntityChanged.ToSmartSwitchEvent());
            else
                OnStorageMonitorTriggered?.Invoke(this, broadcast.EntityChanged.ToStorageMonitorEvent());
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
        Debug.WriteLine($"Unknown broadcast:\n{broadcast}");
    }

    /// <summary>
    /// Processes the request asynchronously and returns the result.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="request">The request to be processed.</param>
    /// <param name="successSelector">The function to select the result from the response.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the processed result.</returns>
    protected async Task<Response<T?>> ProcessRequestAsync<T>(AppRequest request, Func<AppMessage, T> successSelector)
    {
        var response = await SendRequestAsync(request).ConfigureAwait(false);

        return IsError(response)
            ? ResponseHelper.BuildGenericOutput<T>(false, default!, GetErrorMessage(response))
            : ResponseHelper.BuildGenericOutput(true, successSelector(response));
    }

    /// <summary>
    /// Retrieves the information of an entity asynchronously.
    /// </summary>
    /// <typeparam name="T">The type of the entity information.</typeparam>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="selector">The function to select the entity information from the response.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the entity information.</returns>
    protected async Task<Response<T?>> GetEntityInfoAsync<T>(uint entityId, Func<AppMessage, T> selector)
    {
        var request = new AppRequest
        {
            EntityId = entityId,
            GetEntityInfo = new AppEmpty()
        };
        return await ProcessRequestAsync(request, selector).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks the subscription status of an alarm asynchronously.
    /// </summary>
    /// <param name="alarmId">The ID of the alarm entity.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the subscription information.</returns>
    public async Task<Response<SubscriptionInfo?>> CheckSubscriptionAsync(uint alarmId)
    {
        var request = new AppRequest
        {
            CheckSubscription = new AppEmpty(),
            EntityId = alarmId
        };
        return await ProcessRequestAsync<SubscriptionInfo?>(
            request,
            r =>r.Response.Flag.ToSubscriptionInfo()).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the information of an alarm asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the alarm entity.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the alarm information.</returns>
    public async Task<Response<AlarmInfo?>> GetAlarmInfoAsync(uint entityId)
    {
        return await GetEntityInfoAsync<AlarmInfo?>(entityId, r => r.Response.EntityInfo.ToAlarmInfo()).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the clan information asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the clan information.</returns>
    public async Task<Response<ClanInfo?>> GetClanInfoAsync()
    {
        var request = new AppRequest
        {
            GetClanInfo = new AppEmpty()
        };
        return await ProcessRequestAsync<ClanInfo?>(request, r => r.Response.ClanInfo.ToClanInfo()).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the clan message of the day (MOTD) asynchronously.
    /// </summary>
    /// <param name="message">The message to set as the clan MOTD.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> indicating the success of the operation.</returns>
    public async Task<Response<bool?>> SetClanMotdAsync(string message)
    {
        var request = new AppRequest
        {
            SetClanMotd = new AppSendMessage
            {
                Message = message
            }
        };
        return await ProcessRequestAsync<bool?>(request, r => r.Response.Success is not null).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the clan chat asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the clan chat information.</returns>
    public async Task<Response<ClanChatInfo?>> GetClanChatAsync()
    {
        var request = new AppRequest
        {
            GetClanChat = new AppEmpty()
        };
        return await ProcessRequestAsync<ClanChatInfo?>(request, r => r.Response.ClanChat.ToClanChatInfo()).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a clan message asynchronously.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> indicating the success of the operation.</returns>
    public async Task<Response<bool?>> SendClanMessageAsync(string message)
    {
        var request = new AppRequest
        {
            SendClanMessage = new AppSendMessage
            {
                Message = message
            }
        };
        return await ProcessRequestAsync<bool?>(request, r => r.Response.Success is not null).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the Nexus authentication asynchronously.
    /// </summary>
    /// <param name="appKey">The app key for Nexus authentication.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the Nexus authentication.</returns>
    public async Task<Response<NexusAuth?>> GetNexusAuthAsync(string appKey)
    {
        var request = new AppRequest
        {
            GetNexusAuth = new AppGetNexusAuth
            {
                AppKey = appKey
            }
        };
        return await ProcessRequestAsync<NexusAuth?>(request, r => r.Response.NexusAuth.ToNexusAuth()).ConfigureAwait(false);
    }

    /// <summary>
    /// Subscribes to a camera asynchronously, starting the <see cref="OnCameraRaysReceived"/> stream.
    /// </summary>
    /// <param name="cameraId">The identifier of the camera/CCTV entity to subscribe to.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the camera information.</returns>
    public async Task<Response<CameraInfo?>> SubscribeToCameraAsync(string cameraId)
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
            r => r.Response.CameraSubscribeInfo.ToCameraInfo()).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends input (movement/mouse/buttons) to the subscribed camera asynchronously.
    /// </summary>
    /// <param name="buttons">The pressed <see cref="CameraButtons"/> bitmask.</param>
    /// <param name="mouseDeltaX">The horizontal mouse delta.</param>
    /// <param name="mouseDeltaY">The vertical mouse delta.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> indicating the success of the operation.</returns>
    public async Task<Response<bool?>> SendCameraInputAsync(CameraButtons buttons, float mouseDeltaX = 0, float mouseDeltaY = 0)
    {
        var request = new AppRequest
        {
            CameraInput = new AppCameraInput
            {
                Buttons = (int)buttons,
                MouseDelta = new Vector2
                {
                    X = mouseDeltaX,
                    Y = mouseDeltaY
                }
            }
        };
        return await ProcessRequestAsync<bool?>(request, r => r.Response.Success is not null).ConfigureAwait(false);
    }

    /// <summary>
    /// Unsubscribes from the currently subscribed camera asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> indicating the success of the operation.</returns>
    public async Task<Response<bool?>> UnsubscribeFromCameraAsync()
    {
        var request = new AppRequest
        {
            CameraUnsubscribe = new AppEmpty()
        };
        return await ProcessRequestAsync<bool?>(request, r => r.Response.Success is not null).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the server information asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the server information.</returns>
    public async Task<Response<ServerInfo?>> GetInfoAsync()
    {
        var request = new AppRequest
        {
            GetInfo = new AppEmpty()
        };
        return await ProcessRequestAsync<ServerInfo?>(request, r => r.Response.Info.ToServerInfo()).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the server map asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the server map.</returns>
    public async Task<Response<ServerMap?>> GetMapAsync()
    {
        var request = new AppRequest
        {
            GetMap = new AppEmpty()
        };
        return await ProcessRequestAsync<ServerMap?>(request, r => r.Response.Map.ToServerMap()).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the map markers asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the map markers.</returns>
    public async Task<Response<MapMarkers?>> GetMapMarkersAsync()
    {
        var request = new AppRequest
        {
            GetMapMarkers = new AppEmpty()
        };
        return await ProcessRequestAsync<MapMarkers?>(request, r => r.Response.MapMarkers.ToMapMarkers()).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the information of a smart switch asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the smart switch entity.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the smart switch information.</returns>
    public async Task<Response<SmartSwitchInfo?>> GetSmartSwitchInfoAsync(uint entityId)
    {
        return await GetEntityInfoAsync<SmartSwitchInfo?>(
            entityId,
            r => r.Response.EntityInfo.ToSmartSwitchInfo()).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the information of a storage monitor asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the storage monitor entity.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the storage monitor information.</returns>
    public async Task<Response<StorageMonitorInfo?>> GetStorageMonitorInfoAsync(uint entityId)
    {
        return await GetEntityInfoAsync<StorageMonitorInfo?>(
            entityId,
            r => r.Response.EntityInfo.ToStorageMonitorInfo()).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the team chat information asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the team chat information.</returns>
    public async Task<Response<TeamChatInfo?>> GetTeamChatAsync()
    {
        var request = new AppRequest
        {
            GetTeamChat = new AppEmpty()
        };
        return await ProcessRequestAsync<TeamChatInfo?>(
            request,
            r => r.Response.TeamChat.ToTeamChatInfo()).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the team information asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the team information.</returns>
    public async Task<Response<TeamInfo?>> GetTeamInfoAsync()
    {
        var request = new AppRequest
        {
            GetTeamInfo = new AppEmpty()
        };
        return await ProcessRequestAsync<TeamInfo?>(request, r => r.Response.TeamInfo.ToTeamInfo()).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the current time information asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the time information.</returns>
    public async Task<Response<TimeInfo?>> GetTimeAsync()
    {
        var request = new AppRequest
        {
            GetTime = new AppEmpty()
        };
        return await ProcessRequestAsync<TimeInfo?>(request, r => r.Response.Time.ToTimeInfo()).ConfigureAwait(false);
    }

    /// <summary>
    /// Promotes a player to leader asynchronously.
    /// </summary>
    /// <param name="steamId">The Steam ID of the player to promote.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> indicating the success of the operation.</returns>
    public async Task<Response<bool?>> PromoteToLeaderAsync(ulong steamId)
    {
        var request = new AppRequest
        {
            PromoteToLeader = new AppPromoteToLeader
            {
                SteamId = steamId
            }
        };
        return await ProcessRequestAsync<bool?>(request, r => r.Response.Success is not null).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a team message asynchronously.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the team message.</returns>
    public async Task<Response<TeamMessage?>> SendTeamMessageAsync(string message)
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
            r => r.Broadcast.TeamMessage.Message.ToTeamMessage()).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the value of a smart switch asynchronously.
    /// </summary>
    /// <param name="smartSwitchId">The ID of the smart switch entity.</param>
    /// <param name="smartSwitchValue">The value to set for the smart switch.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the updated smart switch information.</returns>
    public async Task<Response<SmartSwitchInfo?>> SetSmartSwitchValueAsync(uint smartSwitchId, bool smartSwitchValue)
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
            r => r.Broadcast.EntityChanged.ToSmartSwitchEvent()).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the subscription status of an entity asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="doSubscribe">Specifies whether to subscribe or unsubscribe.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> indicating the success of the operation.</returns>
    public async Task<Response<bool?>> SetSubscriptionAsync(uint entityId, bool doSubscribe = true)
    {
        var request = new AppRequest
        {
            EntityId = entityId,
            SetSubscription = new AppFlag
            {
                Value = doSubscribe
            }
        };
        return await ProcessRequestAsync<bool?>(request, r => r.Response.Success is not null).ConfigureAwait(false);
    }

    /// <summary>
    /// Strobes a smart switch asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the smart switch entity.</param>
    /// <param name="timeoutMilliseconds">The duration of each state in milliseconds.</param>
    /// <param name="value">The initial value of the smart switch.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the updated smart switch information.</returns>
    public async Task<Response<SmartSwitchInfo?>> StrobeSmartSwitchAsync(
        uint entityId,
        int timeoutMilliseconds = 1000,
        bool value = true)
    {
        var response = await SetSmartSwitchValueAsync(entityId, value).ConfigureAwait(false);

        if (!response.IsSuccess) return response;

        await Task.Delay(timeoutMilliseconds).ConfigureAwait(false);
        return await SetSmartSwitchValueAsync(entityId, !value).ConfigureAwait(false);
    }

    /// <summary>
    /// Toggles a smart switch asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the smart switch entity.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the updated smart switch information.</returns>
    public async Task<Response<SmartSwitchInfo?>> ToggleSmartSwitchAsync(uint entityId)
    {
        var entityInfo = await GetSmartSwitchInfoAsync(entityId).ConfigureAwait(false);

        if (!entityInfo.IsSuccess) return entityInfo;

        var value = entityInfo.Data!.IsActive;
        return await SetSmartSwitchValueAsync(entityId, !value).ConfigureAwait(false);
    }
}