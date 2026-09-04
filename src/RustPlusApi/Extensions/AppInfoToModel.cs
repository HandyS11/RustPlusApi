using RustPlusApi.Data;
using RustPlusContracts;

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf server-info messages to model types.</summary>
public static class AppInfoToModel
{
    /// <summary>Maps an <see cref="AppInfo"/> to a <see cref="ServerInfo"/>.</summary>
    /// <param name="appInfo">The protobuf server info response.</param>
    public static ServerInfo ToServerInfo(this AppInfo appInfo)
    {
        return new ServerInfo
        {
            Name = appInfo.Name,
            HeaderImage = appInfo.HeaderImage,
            Url = appInfo.Url,
            Map = appInfo.Map,
            MapSize = appInfo.MapSize,
            WipeTime = DateTimeOffset.FromUnixTimeSeconds(appInfo.WipeTime).UtcDateTime,
            PlayerCount = appInfo.Players,
            MaxPlayerCount = appInfo.MaxPlayers,
            QueuedPlayerCount = appInfo.QueuedPlayers,
            Seed = appInfo.Seed,
            Salt = appInfo.Salt,
            LogoImage = appInfo.LogoImage,
            Nexus = appInfo.Nexus,
            NexusId = appInfo.ShouldSerializeNexusId() ? appInfo.NexusId : null,
            NexusZone = appInfo.NexusZone,
            CamerasEnabled = appInfo.ShouldSerializeCamerasEnabled() ? appInfo.CamerasEnabled : null
        };
    }
}
