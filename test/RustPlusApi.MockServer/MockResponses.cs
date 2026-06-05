using Google.Protobuf;
using RustPlusContracts;

namespace RustPlusApi.MockServer;

/// <summary>
/// Canned <see cref="AppMessage"/> fixtures and the default request responder used by
/// <see cref="MockRustPlusServer"/>. The fixtures are realistic enough to exercise the
/// typed <c>Response&lt;T&gt;</c> mappers offline.
/// </summary>
public static class MockResponses
{
    /// <summary>
    /// The default responder: matches on which request field is set and replies with a
    /// canned response carrying the same <c>seq</c>. Unknown requests get a bare success.
    /// </summary>
    public static AppMessage Default(AppRequest request)
    {
        var response = new AppResponse { Seq = request.Seq };

        if (request.GetInfo is not null) response.Info = SampleInfo();
        else if (request.GetTime is not null) response.Time = SampleTime();
        else if (request.GetMap is not null) response.Map = SampleMap();
        else if (request.GetEntityInfo is not null) response.EntityInfo = SampleSmartSwitch(true);
        else if (request.CheckSubscription is not null) response.Flag = new AppFlag { Value = true };
        else if (request.GetClanInfo is not null) response.ClanInfo = SampleClanInfo();
        else if (request.GetClanChat is not null) response.ClanChat = SampleClanChat();
        else if (request.GetNexusAuth is not null) response.NexusAuth = SampleNexusAuth();
        else response.Success = new AppSuccess();

        return new AppMessage { Response = response };
    }

    /// <summary>Builds an error <see cref="AppMessage"/> carrying the given error string.</summary>
    public static AppMessage Error(uint seq, string error) =>
        new() { Response = new AppResponse { Seq = seq, Error = new AppError { Error = error } } };

    /// <summary>Wraps a broadcast in an <see cref="AppMessage"/> for injection.</summary>
    public static AppMessage Broadcast(AppBroadcast broadcast) =>
        new() { Broadcast = broadcast };

    /// <summary>A team-chat broadcast, for testing <c>OnTeamChatReceived</c>.</summary>
    public static AppBroadcast TeamMessageBroadcast(ulong steamId, string name, string message) =>
        new()
        {
            TeamMessage = new AppNewTeamMessage
            {
                Message = new AppTeamMessage
                {
                    SteamId = steamId,
                    Name = name,
                    Message = message,
                    Color = "#FFFFFF",
                    Time = 1_000
                }
            }
        };

    /// <summary>An entity-changed broadcast (smart switch), for testing <c>OnSmartSwitchTriggered</c>.</summary>
    public static AppBroadcast SmartSwitchBroadcast(uint entityId, bool value) =>
        new()
        {
            EntityChanged = new AppEntityChanged
            {
                EntityId = entityId,
                Payload = new AppEntityPayload { Value = value, Capacity = 0 }
            }
        };

    /// <summary>A clan-chat broadcast, for testing <c>OnClanChatReceived</c>.</summary>
    public static AppBroadcast ClanMessageBroadcast(long clanId, ulong steamId, string name, string message) =>
        new()
        {
            ClanMessage = new AppNewClanMessage
            {
                ClanId = clanId,
                Message = new AppClanMessage
                {
                    SteamId = steamId,
                    Name = name,
                    Message = message,
                    Time = 1_700_000_000
                }
            }
        };

    /// <summary>A clan-changed broadcast, for testing <c>OnClanChanged</c>.</summary>
    public static AppBroadcast ClanChangedBroadcast() =>
        new() { ClanChanged = new AppClanChanged { ClanInfo = SampleClan() } };

    public static AppInfo SampleInfo() => new()
    {
        Name = "Mock Rust Server",
        HeaderImage = "https://example.invalid/header.png",
        Url = "https://example.invalid",
        Map = "Procedural Map",
        MapSize = 4000,
        WipeTime = 1_700_000_000,
        Players = 42,
        MaxPlayers = 200,
        QueuedPlayers = 3,
        Seed = 1337,
        Salt = 7331,
        LogoImage = "https://example.invalid/logo.png",
        Nexus = "",
        NexusZone = ""
    };

    public static AppTime SampleTime() => new()
    {
        DayLengthMinutes = 60f,
        TimeScale = 1f,
        Sunrise = 7f,
        Sunset = 20f,
        Time = 12.5f
    };

    public static AppMap SampleMap() => new()
    {
        Width = 2000,
        Height = 2000,
        JpgImage = ByteString.CopyFrom(1, 2, 3, 4),
        OceanMargin = 500,
        Background = "#000000"
    };

    public static AppEntityInfo SampleSmartSwitch(bool value) => new()
    {
        Type = AppEntityType.Switch,
        Payload = new AppEntityPayload { Value = value, Capacity = 0 }
    };

    public static AppClanInfo SampleClanInfo() => new() { ClanInfo = SampleClan() };

    public static ClanInfo SampleClan() => new()
    {
        ClanId = 4242,
        Name = "Mock Clan",
        Created = 1_600_000_000,
        Creator = 76561198000000001,
        Motd = "Welcome to the mock clan",
        MotdTimestamp = 1_700_000_000,
        MotdAuthor = 76561198000000001,
        Color = 16711680,
        MaxMemberCount = 50,
        Members =
        {
            new ClanInfo.Types.Member
            {
                SteamId = 76561198000000001,
                RoleId = 0,
                Joined = 1_600_000_000,
                LastSeen = 1_700_000_000,
                Online = true
            }
        }
    };

    public static AppClanChat SampleClanChat() => new()
    {
        Messages =
        {
            new AppClanMessage
            {
                SteamId = 76561198000000001,
                Name = "Tester",
                Message = "clan chat fixture",
                Time = 1_700_000_000
            }
        }
    };

    public static AppNexusAuth SampleNexusAuth() => new()
    {
        ServerId = "mock-server-id",
        PlayerToken = 987654321
    };
}
