using RustPlusApi.Data;

using RustPlusContracts;

namespace RustPlusApi.Extensions;

public static class AppInfoToModel
{
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
            NexusZone = appInfo.NexusZone
        };
    }
}
