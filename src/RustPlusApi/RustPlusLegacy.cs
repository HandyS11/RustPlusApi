using RustPlusContracts;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi;

/// <summary>
/// Initializes a new instance of the <see cref="RustPlusLegacy"/> class,
/// connecting to a Rust+ server using the specified parameters.
/// </summary>
/// <param name="server">The IP address of the Rust+ server.</param>
/// <param name="port">The port dedicated for the Rust+ companion app (not the one used to connect in-game).</param>
/// <param name="playerId">Your Steam ID.</param>
/// <param name="playerToken">Your player token acquired with FCM.</param>
/// <param name="useFacepunchProxy">Specifies whether to use the Facepunch proxy.</param>
/// <seealso cref="RustPlusSocket"/>
[Obsolete("This class is deprecated and will be removed in a future version. Use RustPlus instead.")]
public class RustPlusLegacy(string server, int port, ulong playerId, int playerToken, bool useFacepunchProxy = false)
    : RustPlusSocket(server, port, playerId, playerToken, useFacepunchProxy)
{
    /// <summary>
    /// Checks the subscription status for a specific entity in the Rust+ server asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the entity to check the subscription for.</param>
    /// <returns>The subscription status.</returns>
    public async Task<AppMessage> CheckSubscriptionLegacyAsync(uint entityId)
    {
        var request = new AppRequest
        {
            CheckSubscription = new AppEmpty(),
            EntityId = entityId
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Retrieves the clan chat from the Rust+ server asynchronously.
    /// </summary>
    /// <returns>The clan chat.</returns>
    public async Task<AppMessage> GetClanChatLegacyAsync()
    {
        var request = new AppRequest
        {
            GetClanChat = new AppEmpty()
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Retrieves information about an entity from the Rust+ server asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the entity to retrieve information for.</param>
    /// <returns>The entity information.</returns>
    public async Task<AppMessage> GetEntityInfoLegacyAsync(uint entityId)
    {
        var request = new AppRequest
        {
            EntityId = entityId,
            GetEntityInfo = new AppEmpty()
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Retrieves the information from the Rust+ server asynchronously.
    /// </summary>
    /// <returns>The information.</returns>
    public async Task<AppMessage> GetInfoLegacyAsync()
    {
        var request = new AppRequest
        {
            GetInfo = new AppEmpty()
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Retrieves the map from the Rust+ server asynchronously.
    /// </summary>
    /// <returns>The map.</returns>
    public async Task<AppMessage> GetMapLegacyAsync()
    {
        var request = new AppRequest
        {
            GetMap = new AppEmpty()
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Retrieves the map markers from the Rust+ server asynchronously.
    /// </summary>
    /// <returns>The map markers.</returns>
    public async Task<AppMessage> GetMapMarkersLegacyAsync()
    {
        var request = new AppRequest
        {
            GetMapMarkers = new AppEmpty()
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Retrieves the Nexus authentication from the Rust+ server asynchronously.
    /// </summary>
    /// <param name="apiKey">The API key for Nexus authentication.</param>
    /// <returns>The Nexus authentication response.</returns>
    public async Task<AppMessage> GetNexusAuthLegacyAsync(string apiKey)
    {
        var request = new AppRequest
        {
            GetNexusAuth = new AppGetNexusAuth
            {
                AppKey = apiKey,
            },
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Retrieves the team chat from the Rust+ server asynchronously.
    /// </summary>
    /// <returns>The team chat.</returns>
    public async Task<AppMessage> GetTeamChatLegacyAsync()
    {
        var request = new AppRequest
        {
            GetTeamChat = new AppEmpty()
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Retrieves the team information from the Rust+ server asynchronously.
    /// </summary>
    /// <returns>The team information.</returns>
    public async Task<AppMessage> GetTeamInfoLegacyAsync()
    {
        var request = new AppRequest
        {
            GetTeamInfo = new AppEmpty()
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Retrieves the time from the Rust+ server asynchronously.
    /// </summary>
    /// <returns>The time.</returns>
    public async Task<AppMessage> GetTimeLegacyAsync()
    {
        var request = new AppRequest
        {
            GetTime = new AppEmpty()
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Promotes a player to leader in the Rust+ server asynchronously.
    /// </summary>
    /// <param name="steamId">The Steam ID of the player to promote.</param>
    /// <returns>The response from the server.</returns>
    public async Task<AppMessage> PromoteToLeaderLegacyAsync(ulong steamId)
    {
        var request = new AppRequest
        {
            PromoteToLeader = new AppPromoteToLeader
            {
                SteamId = steamId
            }
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Sends a team message to the Rust+ server asynchronously.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <returns>The response from the server.</returns>
    public async Task<AppMessage> SendTeamMessageLegacyAsync(string message)
    {
        var request = new AppRequest
        {
            SendTeamMessage = new AppSendMessage
            {
                Message = message
            }
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Sets the Clan Message of the Day (MOTD) in the Rust+ server asynchronously.
    /// </summary>
    /// <param name="message">The message to set as the Clan MOTD.</param>
    /// <returns>The response from the server.</returns>
    public async Task<AppMessage> SetClanMotdLegacyAsync(string message)
    {
        var request = new AppRequest
        {
            SetClanMotd = new AppSendMessage
            {
                Message = message
            }
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Sets the value of an entity in the Rust+ server asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>The response from the server.</returns>
    public async Task<AppMessage> SetEntityValueLegacyAsync(uint entityId, bool value)
    {
        var request = new AppRequest
        {
            EntityId = entityId,
            SetEntityValue = new AppSetEntityValue
            {
                Value = value
            }
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Sets the subscription status for a specific entity in the Rust+ server asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the entity to set the subscription for.</param>
    /// <param name="enablePushNotification">Specifies whether to enable push notifications for the entity. Default is true.</param>
    /// <returns>The response from the server.</returns>
    public async Task<AppMessage> SetSubscriptionLegacyAsync(uint entityId, bool enablePushNotification = true)
    {
        var request = new AppRequest
        {
            SetSubscription = new AppFlag
            {
                Value = enablePushNotification
            },
            EntityId = entityId
        };
        return await SendRequestAsync(request);
    }

    /// <summary>
    /// Strobes an entity in the Rust+ server asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="timeoutMilliseconds">The timeout in milliseconds.</param>
    /// <param name="value">The value to set.</param>
    public async Task StrobeEntityLegacyAsync(uint entityId, int timeoutMilliseconds = 1000, bool value = true)
    {
        await SetEntityValueLegacyAsync(entityId, value);
        await Task.Delay(timeoutMilliseconds);
        await SetEntityValueLegacyAsync(entityId, !value);
    }

    /// <summary>
    /// Toggles the value of an entity in the Rust+ server asynchronously.
    /// </summary>
    /// <param name="entityId">The ID of the entity.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ToggleEntityValueLegacyAsync(uint entityId)
    {
        var entityInfo = await GetEntityInfoLegacyAsync(entityId);
        var value = entityInfo.Response.EntityInfo.Payload.Value;
        await SetEntityValueLegacyAsync(entityId, !value);
    }
}
