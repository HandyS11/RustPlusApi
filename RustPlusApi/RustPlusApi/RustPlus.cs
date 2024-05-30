﻿using System.Diagnostics;

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
            }
            else
            {
                Debug.WriteLine($"Unknown broadcast:\n{broadcast}");
            }
        }

        /// <summary>
        /// Processes the request asynchronously and returns the result.
        /// </summary>
        /// <typeparam name="T">The type of the result.</typeparam>
        /// <param name="request">The request to be processed.</param>
        /// <param name="successSelector">The function to select the result from the response.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the processed result.</returns>
        private async Task<Response<T?>> ProcessRequestAsync<T>(AppRequest request, Func<AppMessage, T> successSelector)
        {
            var response = await SendRequestAsync(request);

            return IsError(response)
                ? ResponseHelper.BuildGenericOutput<T>(false, default!, response.Response.Error.Error)
                : ResponseHelper.BuildGenericOutput(true, successSelector(response));
        }

        /// <summary>
        /// Retrieves the information of a smart switch asynchronously.
        /// </summary>
        /// <param name="entityId">The ID of the smart switch entity.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the smart switch information.</returns>
        public async Task<Response<SmartSwitchInfo?>> GetSmartSwitchInfoAsync(uint entityId)
        {
            var request = new AppRequest
            {
                EntityId = entityId,
                GetEntityInfo = new AppEmpty()
            };
            return await ProcessRequestAsync<SmartSwitchInfo?>(request, r => r.Response.EntityInfo.ToSmartSwitchInfo());
        }

        /// <summary>
        /// Retrieves the information of an alarm asynchronously.
        /// </summary>
        /// <param name="entityId">The ID of the alarm entity.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the alarm information.</returns>
        public async Task<Response<AlarmInfo?>> GetAlarmInfoAsync(uint entityId)
        {
            var request = new AppRequest
            {
                EntityId = entityId,
                GetEntityInfo = new AppEmpty()
            };
            return await ProcessRequestAsync<AlarmInfo?>(request, r => r.Response.EntityInfo.ToAlarmInfo());
        }

        /// <summary>
        /// Retrieves the information of a storage monitor asynchronously.
        /// </summary>
        /// <param name="entityId">The ID of the storage monitor entity.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains a <see cref="Response{T}"/> with the storage monitor information.</returns>
        public async Task<Response<StorageMonitorInfo?>> GetStorageMonitorInfoAsync(uint entityId)
        {
            var request = new AppRequest
            {
                EntityId = entityId,
                GetEntityInfo = new AppEmpty()
            };
            return await ProcessRequestAsync<StorageMonitorInfo?>(request, r => r.Response.EntityInfo.ToStorageMonitorInfo());
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

        /*
        
        public async Task GetMapMarkersAsync(Func<AppMessage, bool>? callback = null)
        {
           var request = new AppRequest
           {
               GetMapMarkers = new AppEmpty()
           };
           await SendRequestAsync(request, callback);
        }

        public async Task GetTeamChatAsync(Func<AppMessage, bool>? callback = null)
        {
           var request = new AppRequest
           {
               GetTeamChat = new AppEmpty(),
           };
           await SendRequestAsync(request, callback);
        }

        public async Task GetTeamInfoAsync(Func<AppMessage, bool>? callback = null)
        {
           var request = new AppRequest
           {
               GetTeamInfo = new AppEmpty()
           };
           await SendRequestAsync(request, callback);
        }

        public async Task GetTimeAsync(Func<AppMessage, bool>? callback = null)
        {
           var request = new AppRequest
           {
               GetTime = new AppEmpty()
           };
           await SendRequestAsync(request, callback);
        }

        public async Task PromoteToLeaderAsync(ulong steamId, Func<AppMessage, bool>? callback = null)
        {
           var request = new AppRequest
           {
               PromoteToLeader = new AppPromoteToLeader
               {
                   SteamId = steamId
               }
           };
           await SendRequestAsync(request, callback);
        }

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