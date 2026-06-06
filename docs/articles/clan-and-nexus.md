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

`ClanInfo` exposes the clan id, name, creation time, creator, MOTD (+ author/timestamp), logo,
colour, roles, members, invites and max member count. Optional fields are `null` when the server
doesn't send them.

### Clan events

```csharp
rustPlus.OnClanChatReceived += (_, message) =>
    Console.WriteLine($"[{message.ClanId}] {message.Name}: {message.Message}");

rustPlus.OnClanChanged += (_, e) =>
    Console.WriteLine($"Clan updated: {e.ClanInfo?.Name}");
```

`OnClanChanged` fires whenever the clan changes (roles, members, MOTD, …) and carries the full
updated `ClanInfo`.

## Nexus

Nexus auth is used by servers participating in Rust's Nexus (cross-server) system.

```csharp
var nexus = await rustPlus.GetNexusAuthAsync(appKey);   // Response<NexusAuth?>
if (nexus.IsSuccess)
    Console.WriteLine($"{nexus.Data!.ServerId} / {nexus.Data.PlayerToken}");
```
