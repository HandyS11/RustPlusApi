# Clan & Nexus

## Clan

The clan family mirrors the in-game clan system: info, message of the day, and clan chat.

```csharp
var clan = await rustPlus.GetClanInfoAsync();        // Response<ClanInfo?>
if (clan.IsSuccess)
{
    Console.WriteLine(clan.Data!.Name);
    foreach (var member in clan.Data.Members ?? [])
        Console.WriteLine($"  {member.SteamId} (role {member.RoleId})");
}

await rustPlus.SetClanMotdAsync("Welcome to the clan!");
var chat = await rustPlus.GetClanChatAsync();         // Response<ClanChatInfo?>
await rustPlus.SendClanMessageAsync("gg");
```

### `ClanInfo` properties

`GetClanInfoAsync()` returns a `ClanInfo` snapshot with the following properties:

| Property | Type | Description |
| --- | --- | --- |
| `ClanId` | `long` | Unique identifier of the clan. |
| `Name` | `string` | Display name of the clan. |
| `Created` | `DateTime` | UTC timestamp when the clan was created. |
| `Creator` | `ulong` | Steam64 ID of the clan creator. |
| `Motd` | `string?` | Message of the day, or `null` if not set. |
| `MotdTimestamp` | `DateTime?` | UTC timestamp when the MOTD was last changed. |
| `MotdAuthor` | `ulong?` | Steam64 ID of the player who last changed the MOTD. |
| `Logo` | `byte[]?` | Raw bytes of the clan logo image, or `null`. |
| `Color` | `int?` | Clan colour as a packed ARGB integer, or `null`. |
| `Roles` | `IEnumerable<ClanRole>?` | Roles defined in this clan. |
| `Members` | `IEnumerable<ClanMember>?` | Current members of the clan. |
| `Invites` | `IEnumerable<ClanInvite>?` | Pending invitations to the clan. |
| `MaxMemberCount` | `int?` | Member cap, or `null` if uncapped. |
| `Score` | `long?` | Clan score, or `null` if not reported. |

Optional fields are `null` when the server does not include them in the response.

### `ClanRole` properties

Each `ClanRole` in `ClanInfo.Roles` describes a permission role:

| Property | Type | Description |
| --- | --- | --- |
| `RoleId` | `int` | Unique identifier within the clan. |
| `Rank` | `int` | Rank order; lower values are higher ranks. |
| `Name` | `string` | Display name of the role. |
| `CanSetMotd` | `bool` | May set the clan MOTD. |
| `CanSetLogo` | `bool` | May set the clan logo. |
| `CanInvite` | `bool` | May invite players. |
| `CanKick` | `bool` | May kick other members. |
| `CanPromote` | `bool` | May promote others. |
| `CanDemote` | `bool` | May demote others. |
| `CanSetPlayerNotes` | `bool` | May set officer notes on members. |
| `CanAccessLogs` | `bool` | May view clan audit logs. |
| `CanAccessScoreEvents` | `bool` | May view clan score events. |

### `ClanMember` properties

Each `ClanMember` in `ClanInfo.Members`:

| Property | Type | Description |
| --- | --- | --- |
| `SteamId` | `ulong` | Steam64 ID of the member. |
| `RoleId` | `int` | References `ClanRole.RoleId`. |
| `Joined` | `DateTime` | UTC timestamp when the member joined. |
| `LastSeen` | `DateTime` | UTC timestamp of last online activity. |
| `Notes` | `string?` | Officer notes attached to this member. |
| `Online` | `bool?` | `true` if currently online, `null` if not reported. |

### `ClanInvite` properties

Each `ClanInvite` in `ClanInfo.Invites`:

| Property | Type | Description |
| --- | --- | --- |
| `SteamId` | `ulong` | Steam64 ID of the invited player. |
| `Recruiter` | `ulong` | Steam64 ID of the member who sent the invite. |
| `Timestamp` | `DateTime` | UTC timestamp when the invitation was created. |

### Clan events

```csharp
rustPlus.OnClanChatReceived += (_, message) =>
    Console.WriteLine($"[{message.ClanId}] {message.Name}: {message.Message}");

rustPlus.OnClanChanged += (_, e) =>
    Console.WriteLine($"Clan updated: {e.ClanInfo?.Name}");
```

`OnClanChanged` fires whenever the clan snapshot changes — role assignments, member joins/leaves,
MOTD updates, etc. — and carries the full updated `ClanInfo`. It is a push broadcast from the
server; no polling is required.

`OnClanChatReceived` fires for each individual clan chat message as it arrives. The two events are
independent: a new message in chat fires only `OnClanChatReceived`, while structural clan changes
(membership, MOTD, roles) fire only `OnClanChanged`.

## Nexus

Nexus auth is used by servers participating in Rust's Nexus (cross-server) system.

```csharp
var nexus = await rustPlus.GetNexusAuthAsync(appKey);   // Response<NexusAuth?>
if (nexus.IsSuccess)
    Console.WriteLine($"{nexus.Data!.ServerId} / {nexus.Data.PlayerToken}");
```
