using RustPlus.ConsoleApp.Features;
using RustPlus.ConsoleApp.Utils;

// Fill credentials.json (copy credentials.sample.json) with the ip/port/playerId/playerToken
// printed by the RustPlus.Register.ConsoleApp sample when you "Pair with Server" in game.
// Put it next to this project (gitignored), or pass its path as the first argument.
var configFilePath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "credentials.json");

CredentialsReaderUtility.Credentials credentials;
try
{
    credentials = configFilePath.GetConfig();
}
catch (FileNotFoundException)
{
    Console.WriteLine($"Config file not found at: {configFilePath}");
    Console.WriteLine("Copy credentials.sample.json to credentials.json and fill in your server details.");
    return;
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to load config: {ex.Message}");
    return;
}

using var rustPlus = new RustPlusApi.RustPlus(credentials.Ip, credentials.Port, credentials.PlayerId, credentials.PlayerToken);

try
{
    await rustPlus.ConnectAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to connect to {credentials.Ip}:{credentials.Port} — {ex.Message}");
    Console.WriteLine("Check that the server is up and the credentials are current.");
    return;
}

var ids = new EntityIdStore();

await Menu.RunAsync("Main menu",
    new MenuItem("Common Features", () => Menu.RunAsync("Common",
        new MenuItem("Get Info", () => new GetInfo(rustPlus).GetInfoAsync()),
        new MenuItem("Get Map", () => new GetMap(rustPlus).GetMapAsync()),
        new MenuItem("Get Map Markers", () => new GetMapMarkers(rustPlus).GetMapMarkersAsync()),
        new MenuItem("Get Time", () => new GetTime(rustPlus).GetTimeAsync()),
        new MenuItem("Get Nexus Auth", () =>
        {
            Console.Write("\nType the Nexus app key: ");
            var appKey = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(appKey))
            {
                Console.WriteLine("App key cannot be empty.");
                return Task.CompletedTask;
            }
            return new GetNexusAuth(rustPlus).GetNexusAuthAsync(appKey);
        }))),
    new MenuItem("Team Features", () => Menu.RunAsync("Team",
        new MenuItem("Get Team Info", () => new GetTeamInfo(rustPlus).GetTeamInfoAsync()),
        new MenuItem("Get Team Chat", () => new GetTeamChat(rustPlus).GetTeamChatAsync()),
        new MenuItem("Promote to Leader", () =>
            new PromoteToLeader(rustPlus).PromoteToLeaderAsync(ids.GetUlong("steamId"))),
        new MenuItem("Send Team Message", () =>
        {
            Console.Write("\nType your message to send to the team: ");
            var message = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(message))
            {
                Console.WriteLine("Message cannot be empty.");
                return Task.CompletedTask;
            }
            return new SendTeamMessage(rustPlus).SendTeamMessageAsync(message);
        }))),
    new MenuItem("Clan Features", () => Menu.RunAsync("Clan",
        new MenuItem("Get Clan Info", () => new GetClanInfo(rustPlus).GetClanInfoAsync()),
        new MenuItem("Get Clan Chat", () => new GetClanChat(rustPlus).GetClanChatAsync()),
        new MenuItem("Send Clan Message", () =>
        {
            Console.Write("\nType your message to send to the clan: ");
            var message = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(message))
            {
                Console.WriteLine("Message cannot be empty.");
                return Task.CompletedTask;
            }
            return new SendClanMessage(rustPlus).SendClanMessageAsync(message);
        }),
        new MenuItem("Set Clan MOTD", () =>
        {
            Console.Write("\nType the new clan message of the day: ");
            var message = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(message))
            {
                Console.WriteLine("Message cannot be empty.");
                return Task.CompletedTask;
            }
            return new SetClanMotd(rustPlus).SetClanMotdAsync(message);
        }))),
    new MenuItem("Electricity Features", () => Menu.RunAsync("Electricity",
        new MenuItem("Get Alarm Info", () =>
            new GetAlarmInfo(rustPlus).GetAlarmInfoAsync(ids.GetUlong("alarmId"))),
        new MenuItem("Check Subscription", () =>
            new CheckSubscription(rustPlus).CheckSubscriptionAsync(ids.GetUlong("alarmId"))),
        new MenuItem("Set Subscription", () =>
        {
            var alarmId = ids.GetUlong("alarmId");
            Console.Write("\nType 'y' to subscribe to alarm notifications, any other key to unsubscribe: ");
            var doSubscribe = string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
            return new SetSubscription(rustPlus).SetSubscriptionAsync(alarmId, doSubscribe);
        }),
        new MenuItem("Get Storage Monitor Info", () =>
            new GetStorageMonitorInfo(rustPlus).GetStorageMonitorInfoAsync(ids.GetUlong("storageMonitorId"))),
        new MenuItem("Get Smart Switch Info", () =>
            new GetSmartSwitchInfo(rustPlus).GetSmartSwitchInfoAsync(ids.GetUlong("smartSwitchId"))),
        new MenuItem("Set Smart Switch Value", () =>
        {
            var smartSwitchId = ids.GetUlong("smartSwitchId");
            Console.Write("\nType 'y' to activate the smart switch, any other key to deactivate: ");
            var value = string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
            return new SetSmartSwitchValue(rustPlus).SetSmartSwitchValueAsync(smartSwitchId, value);
        }),
        new MenuItem("Strobe Smart Switch", () =>
            new StrobeSmartSwitch(rustPlus).StrobeSmartSwitchAsync(ids.GetUlong("smartSwitchId"))),
        new MenuItem("Toggle Smart Switch", () =>
            new ToggleSmartSwitch(rustPlus).ToggleSmartSwitchAsync(ids.GetUlong("smartSwitchId"))))),
    new MenuItem("Camera Features", () => new CameraSession(rustPlus, ids).RunAsync()),
    new MenuItem("Live Events", () => new LiveEvents(rustPlus).RunAsync()));

await rustPlus.DisconnectAsync();
