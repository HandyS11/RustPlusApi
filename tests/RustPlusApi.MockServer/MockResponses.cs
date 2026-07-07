using RustPlusContracts;

namespace RustPlusApi.MockServer;

/// <summary>
/// Canned <see cref="AppMessage"/> fixtures and the default request responder used by
/// <see cref="MockRustPlusServer"/>. The fixtures are realistic enough to exercise the
/// typed <c>Response&lt;T&gt;</c> mappers offline.
/// </summary>
public static class MockResponses
{
#pragma warning disable S1075 // Mock test data uses a well-known reserved domain, not a real hardcoded URI.
    private const string ExampleBaseUrl = "https://example.invalid";
#pragma warning restore S1075

    /// <summary>
    /// The default responder: matches on which request field is set and replies with a
    /// canned response carrying the same <c>seq</c>. Unknown requests get a bare success.
    /// </summary>
    /// <param name="request">The incoming request to respond to.</param>
    public static AppMessage Default(AppRequest request)
    {
        var response = new AppResponse
        {
            Seq = request.Seq
        };

        if (request.GetInfo is not null)
        {
            response.Info = SampleInfo();
        }
        else if (request.GetTime is not null)
        {
            response.Time = SampleTime();
        }
        else if (request.GetMap is not null)
        {
            response.Map = SampleMap();
        }
        else if (request.GetMapMarkers is not null)
        {
            response.MapMarkers = SampleMapMarkers();
        }
        else if (request.GetTeamInfo is not null)
        {
            response.TeamInfo = SampleTeamInfo();
        }
        else if (request.GetTeamChat is not null)
        {
            response.TeamChat = SampleTeamChat();
        }
        else if (request.GetEntityInfo is not null)
        {
            response.EntityInfo = SampleSmartSwitch(true);
        }
        else if (request.CheckSubscription is not null)
        {
            response.Flag = new AppFlag
            {
                Value = true
            };
        }
        else if (request.GetClanInfo is not null)
        {
            response.ClanInfo = SampleClanInfo();
        }
        else if (request.GetClanChat is not null)
        {
            response.ClanChat = SampleClanChat();
        }
        else if (request.GetNexusAuth is not null)
        {
            response.NexusAuth = SampleNexusAuth();
        }
        else if (request.CameraSubscribe is not null)
        {
            response.CameraSubscribeInfo = SampleCameraInfo();
        }
        else if (request.SendTeamMessage is not null)
        {
            return new AppMessage
            {
                Broadcast = TeamMessageSendBroadcast(request.PlayerId, request.SendTeamMessage.Message)
            };
        }
        else
        {
            response.Success = new AppSuccess();
        }

        return new AppMessage
        {
            Response = response
        };
    }

    /// <summary>Builds an error <see cref="AppMessage"/> carrying the given error string.</summary>
    /// <param name="seq">The sequence number to echo back in the response.</param>
    /// <param name="error">The error string to embed.</param>
    public static AppMessage Error(uint seq, string error) =>
        new()
        {
            Response = new AppResponse
            {
                Seq = seq,
                Error = new AppError
                {
                    Error = error
                }
            }
        };

    /// <summary>Wraps a broadcast in an <see cref="AppMessage"/> for injection.</summary>
    /// <param name="broadcast">The broadcast payload to wrap.</param>
    public static AppMessage Broadcast(AppBroadcast broadcast) =>
        new()
        {
            Broadcast = broadcast
        };

    /// <summary>A team-chat broadcast, for testing <c>OnTeamChatReceived</c>.</summary>
    /// <param name="steamId">Sender's Steam 64-bit ID.</param>
    /// <param name="name">Sender's display name.</param>
    /// <param name="message">Chat message text.</param>
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

    /// <summary>An entity-changed broadcast (smart switch), for testing <c>OnSmartDeviceTriggered</c>.</summary>
    /// <param name="entityId">The entity to report as changed.</param>
    /// <param name="value">The new switch state.</param>
    public static AppBroadcast SmartSwitchBroadcast(uint entityId, bool value) =>
        new()
        {
            EntityChanged = new AppEntityChanged
            {
                EntityId = entityId,
                Payload = new AppEntityPayload
                {
                    Value = value, Capacity = 0
                }
            }
        };

    /// <summary>A clan-chat broadcast, for testing <c>OnClanChatReceived</c>.</summary>
    /// <param name="clanId">The clan's numeric ID.</param>
    /// <param name="steamId">Sender's Steam 64-bit ID.</param>
    /// <param name="name">Sender's display name.</param>
    /// <param name="message">Chat message text.</param>
    public static AppBroadcast ClanMessageBroadcast(long clanId, ulong steamId, string name, string message) =>
        new()
        {
            ClanMessage = new AppNewClanMessage
            {
                ClanId = clanId,
                Message = new AppClanMessage
                {
                    SteamId = steamId, Name = name, Message = message, Time = 1_700_000_000_000
                }
            }
        };

    /// <summary>A clan-changed broadcast, for testing <c>OnClanChanged</c>.</summary>
    public static AppBroadcast ClanChangedBroadcast() =>
        new()
        {
            ClanChanged = new AppClanChanged
            {
                ClanInfo = SampleClan()
            }
        };

    /// <summary>A camera-rays broadcast, for testing <c>OnCameraRaysReceived</c>.</summary>
    public static AppBroadcast CameraRaysBroadcast() =>
        new()
        {
            CameraRays = new AppCameraRays
            {
                VerticalFov = 65f,
                SampleOffset = 0,
                RayData = [0, 1, 2, 3, 4],
                Distance = 100f,
                Entities =
                {
                    new AppCameraRays.Entity
                    {
                        EntityId = 99,
                        Type = AppCameraRays.EntityType.Player,
                        Position = new Vector3
                        {
                            X = 1, Y = 2, Z = 3
                        },
                        Rotation = new Vector3
                        {
                            X = 0, Y = 90, Z = 0
                        },
                        Size = new Vector3
                        {
                            X = 1, Y = 1, Z = 1
                        },
                        Name = "Survivor"
                    }
                }
            }
        };

    public static AppInfo SampleInfo() => new()
    {
        Name = "Mock Rust Server",
        HeaderImage = ExampleBaseUrl + "/header.png",
        Url = ExampleBaseUrl,
        Map = "Procedural Map",
        MapSize = 4000,
        WipeTime = 1_700_000_000,
        Players = 42,
        MaxPlayers = 200,
        QueuedPlayers = 3,
        Seed = 1337,
        Salt = 7331,
        LogoImage = ExampleBaseUrl + "/logo.png",
        Nexus = "",
        NexusId = 123,
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
        JpgImage = [1, 2, 3, 4],
        OceanMargin = 500,
        Background = "#000000"
    };

    public static AppEntityInfo SampleSmartSwitch(bool value) => new()
    {
        Type = AppEntityType.Switch,
        Payload = new AppEntityPayload
        {
            Value = value, Capacity = 0
        }
    };

    public static AppClanInfo SampleClanInfo() => new()
    {
        ClanInfo = SampleClan()
    };

    public static ClanInfo SampleClan() => new()
    {
        ClanId = 4242,
        Name = "Mock Clan",
        Created = 1_600_000_000_000,
        Creator = 76561198000000001,
        Motd = "Welcome to the mock clan",
        MotdTimestamp = 1_700_000_000_000,
        MotdAuthor = 76561198000000001,
        Color = 16711680,
        MaxMemberCount = 50,
        Members =
        {
            new ClanInfo.Member
            {
                SteamId = 76561198000000001,
                RoleId = 0,
                Joined = 1_600_000_000_000,
                LastSeen = 1_700_000_000_000,
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
                SteamId = 76561198000000001, Name = "Tester", Message = "clan chat fixture", Time = 1_700_000_000_000
            }
        }
    };

    public static AppNexusAuth SampleNexusAuth() => new()
    {
        ServerId = "mock-server-id", PlayerToken = 987654321
    };

    public static AppCameraInfo SampleCameraInfo() => new()
    {
        Width = 640,
        Height = 480,
        NearPlane = 0.1f,
        FarPlane = 1000f,
        // Movement | Mouse | Fire
        ControlFlags = 1 | 2 | 8
    };

    /// <summary>A sample <see cref="AppMapMarkers"/> with one player marker, for testing <c>GetMapMarkersAsync</c>.</summary>
    public static AppMapMarkers SampleMapMarkers() => new()
    {
        Markers =
        {
            new AppMarker
            {
                Id = 1,
                Type = AppMarkerType.Player,
                X = 100f,
                Y = 200f,
                Name = "TestPlayer",
                SteamId = 76561198000000001
            }
        }
    };

    /// <summary>A sample <see cref="AppTeamInfo"/> with one member, for testing <c>GetTeamInfoAsync</c>.</summary>
    public static AppTeamInfo SampleTeamInfo() => new()
    {
        LeaderSteamId = 76561198000000001,
        Members =
        {
            new AppTeamInfo.Member
            {
                SteamId = 76561198000000001,
                Name = "Leader",
                X = 500f,
                Y = 500f,
                IsOnline = true,
                SpawnTime = 1_600_000_000,
                IsAlive = true,
                DeathTime = 0
            }
        }
    };

    /// <summary>A sample <see cref="AppTeamChat"/> with one message, for testing <c>GetTeamChatAsync</c>.</summary>
    public static AppTeamChat SampleTeamChat() => new()
    {
        Messages =
        {
            new AppTeamMessage
            {
                SteamId = 76561198000000001,
                Name = "Tester",
                Message = "team chat fixture",
                Color = "#FFFFFF",
                Time = 1_700_000_000
            }
        }
    };

    /// <summary>
    /// A sample storage-monitor <see cref="AppEntityInfo"/>, for testing <c>GetStorageMonitorInfoAsync</c>.
    /// </summary>
    public static AppEntityInfo SampleStorageMonitor() => new()
    {
        Type = AppEntityType.StorageMonitor,
        Payload = new AppEntityPayload
        {
            Capacity = 48,
            HasProtection = false,
            ProtectionExpiry = 0,
            Items =
            {
                new AppEntityPayload.Item
                {
                    ItemId = 1, Quantity = 5, ItemIsBlueprint = false
                }
            }
        }
    };

    /// <summary>
    /// A sample alarm <see cref="AppEntityInfo"/>, for testing <c>GetAlarmInfoAsync</c>.
    /// </summary>
    /// <param name="value">Whether the alarm is active.</param>
    public static AppEntityInfo SampleAlarm(bool value = false) => new()
    {
        Type = AppEntityType.Alarm,
        Payload = new AppEntityPayload
        {
            Value = value, Capacity = 0
        }
    };

    /// <summary>
    /// An <see cref="AppBroadcast"/> carrying a team-message for testing <c>SendTeamMessageAsync</c>.
    /// Echoes the sender's Steam ID, like the real server, so the client's broadcast-reply matcher
    /// (own Steam ID) recognizes it as the reply.
    /// </summary>
    /// <param name="steamId">The Steam ID of the player who sent the message.</param>
    /// <param name="message">The message text that was sent.</param>
    public static AppBroadcast TeamMessageSendBroadcast(ulong steamId, string message) =>
        new()
        {
            TeamMessage = new AppNewTeamMessage
            {
                Message = new AppTeamMessage
                {
                    SteamId = steamId,
                    Name = "Echo",
                    Message = message,
                    Color = "#FFFFFF",
                    Time = 1_700_000_000
                }
            }
        };
}
