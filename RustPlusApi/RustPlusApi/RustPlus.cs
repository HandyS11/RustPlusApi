using System.Diagnostics;

using RustPlusApi.Data;
using RustPlusApi.Data.Events;
using RustPlusApi.Extensions;
using RustPlusApi.Utils;

using RustPlusContracts;

namespace RustPlusApi
{
    /// <summary>
    /// A Rust+ API client made in C#.
    /// </summary>
    /// <param name="server">The IP address of the Rust+ server.</param>
    /// <param name="port">The port dedicated for the Rust+ companion app (not the one used to connect in-game).</param>
    /// <param name="playerId">Your Steam ID.</param>
    /// <param name="playerToken">Your player token acquired with FCM.</param>
    /// <param name="useFacepunchProxy">Specifies whether to use the Facepunch proxy.</param>
    public class RustPlus(string server, int port, ulong playerId, int playerToken, bool useFacepunchProxy = false)
        : RustPlusLegacy(server, port, playerId, playerToken, useFacepunchProxy)
    {
        public event EventHandler<SmartSwitchEventArg>? OnSmartSwitchTriggered; // Alarm will also be triggered since there is no physical difference between them
        public event EventHandler<StorageMonitorEventArg>? OnStorageMonitorTriggered;

        public event EventHandler<TeamMessageEventArg>? OnTeamChatReceived;

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
            Debug.WriteLine($"Unknown broadcast:\n{broadcast}");
        }

        /// <summary>
        /// Processes the request asynchronously and returns the result.
        /// </summary>
        /// <typeparam name="T">The type of the result.</typeparam>
        /// <param name="request">The request to be processed.</param>
        /// <param name="successSelector">The function to select the result from the response.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the processed result.</returns>
        public async Task<Response<T?>> ProcessRequestAsync<T>(AppRequest request, Func<AppMessage, T> successSelector)
        {
            var response = await SendRequestAsync(request);

            return IsError(response)
                ? ResponseHelper.BuildGenericOutput<T>(false, default!, response.Response.Error.Error)
                : ResponseHelper.BuildGenericOutput(true, successSelector(response));
        }

        /// <summary>
        /// Retrieves the information of an entity asynchronously.
        /// </summary>
        /// <typeparam name="T">The type of the entity information.</typeparam>
        /// <param name="entityId">The ID of the entity.</param>
        /// <param name="selector">The function to select the entity information from the response.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the entity information.</returns>
        public async Task<Response<T?>> GetEntityInfoAsync<T>(uint entityId, Func<AppMessage, T> selector)
        {
            var request = new AppRequest
            {
                EntityId = entityId,
                GetEntityInfo = new AppEmpty()
            };
            return await ProcessRequestAsync(request, selector);
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
            return await ProcessRequestAsync<SubscriptionInfo?>(request, r => r.Response.Flag.ToSubscriptionInfo());
        }

        /// <summary>
        /// Retrieves the information of an alarm asynchronously.
        /// </summary>
        /// <param name="entityId">The ID of the alarm entity.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the alarm information.</returns>
        public async Task<Response<AlarmInfo?>> GetAlarmInfoAsync(uint entityId)
        {
            return await GetEntityInfoAsync<AlarmInfo?>(entityId, r => r.Response.EntityInfo.ToAlarmInfo());
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
            return await ProcessRequestAsync<ServerInfo?>(request, r => r.Response.Info.ToServerInfo());
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
            return await ProcessRequestAsync<ServerMap?>(request, r => r.Response.Map.ToServerMap());
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
            return await ProcessRequestAsync<MapMarkers?>(request, r => r.Response.MapMarkers.ToMapMarkers());
        }

        /// <summary>
        /// Retrieves the information of a smart switch asynchronously.
        /// </summary>
        /// <param name="entityId">The ID of the smart switch entity.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the smart switch information.</returns>
        public async Task<Response<SmartSwitchInfo?>> GetSmartSwitchInfoAsync(uint entityId)
        {
            return await GetEntityInfoAsync<SmartSwitchInfo?>(entityId, r => r.Response.EntityInfo.ToSmartSwitchInfo());
        }

        /// <summary>
        /// Retrieves the information of a storage monitor asynchronously.
        /// </summary>
        /// <param name="entityId">The ID of the storage monitor entity.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the storage monitor information.</returns>
        public async Task<Response<StorageMonitorInfo?>> GetStorageMonitorInfoAsync(uint entityId)
        {
            return await GetEntityInfoAsync<StorageMonitorInfo?>(entityId, r => r.Response.EntityInfo.ToStorageMonitorInfo());
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
            return await ProcessRequestAsync<TeamChatInfo?>(request, r => r.Response.TeamChat.ToTeamChatInfo());
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
            return await ProcessRequestAsync<TeamInfo?>(request, r => r.Response.TeamInfo.ToTeamInfo());
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
            return await ProcessRequestAsync<TimeInfo?>(request, r => r.Response.Time.ToTimeInfo());
        }

        /// <summary>
        /// Promotes a player to leader asynchronously.
        /// </summary>
        /// <param name="steamId">The Steam ID of the player to promote.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the promotion result.</returns>
        public async Task<Response<object?>> PromoteToLeaderAsync(ulong steamId)
        {
            var request = new AppRequest
            {
                PromoteToLeader = new AppPromoteToLeader
                {
                    SteamId = steamId
                }
            };
            return await ProcessRequestAsync<object?>(request, r => r.Response);
        }

        /*
        public async Task SendTeamMessageAsync(string message, Func<AppMessage, bool>? callback = null)
        {
           var request = new AppRequest
           {
               SendTeamMessage = new AppSendMessage
               {
                   Message = message
               }
           };
           await SendRequestAsync(request, callback);
        }

        public async Task SetEntityValueAsync(int entityId, bool value, Func<AppMessage, bool>? callback = null)
        {
           var request = new AppRequest
           {
               EntityId = (uint)entityId,
               SetEntityValue = new AppSetEntityValue
               {
                   Value = value
               }
           };
           await SendRequestAsync(request, callback);
        }

        */
    }
}