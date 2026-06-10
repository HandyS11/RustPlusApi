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

var isRunning = true;
while (isRunning)
{
    Console.Clear();
    Console.WriteLine("Choose an option:");
    Console.WriteLine("0. Exit");
    Console.WriteLine("1. Common Features");
    Console.WriteLine("2. Team Features");
    Console.WriteLine("3. Electricity Features");

    Console.Write("\nPlease enter your choice: ");
    switch (Console.ReadLine())
    {
        case "0":
            isRunning = false;
            break;
        case "1":
            await CommonFeatureMenuAsync();
            break;
        case "2":
            await TeamFeatureMenuAsync();
            break;
        case "3":
            await ElectricityFeatureMenuAsync();
            break;
        default:
            Console.WriteLine("Invalid choice, please try again.");
            break;
    }

    Console.WriteLine("\nPress any key to continue...");
    Console.ReadKey(intercept: true);
}

await rustPlus.DisconnectAsync();
return;

async Task CommonFeatureMenuAsync()
{
    while (true)
    {
        Console.Clear();
        Console.WriteLine("Common Menu:");
        Console.WriteLine("0. Back to main menu");
        Console.WriteLine("1. Get Info");
        Console.WriteLine("2. Get Map");
        Console.WriteLine("3. Get Map Markers");
        Console.WriteLine("4. Get Time");

        Console.Write("\nPlease enter your choice: ");
        switch (Console.ReadLine())
        {
            case "0":
                return;
            case "1":
                await new GetInfo(rustPlus).GetInfoAsync();
                break;
            case "2":
                await new GetMap(rustPlus).GetMapAsync();
                break;
            case "3":
                await new GetMapMarkers(rustPlus).GetMapMarkersAsync();
                break;
            case "4":
                await new GetTime(rustPlus).GetTimeAsync();
                break;
            default:
                Console.WriteLine("Invalid choice, please try again.");
                break;
        }

        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey(intercept: true);
    }
}

async Task TeamFeatureMenuAsync()
{
    while (true)
    {
        Console.Clear();
        Console.WriteLine("Team Menu:");
        Console.WriteLine("0. Back to main menu");
        Console.WriteLine("1. Get Team Info");
        Console.WriteLine("2. Get Team Chat");
        Console.WriteLine("3. Promote to Leader");
        Console.WriteLine("4. Send Team Message");

        Console.Write("\nPlease enter your choice: ");
        switch (Console.ReadLine())
        {
            case "0":
                return;
            case "1":
                await new GetTeamInfo(rustPlus).GetTeamInfoAsync();
                break;
            case "2":
                await new GetTeamChat(rustPlus).GetTeamChatAsync();
                break;
            case "3":
                Console.WriteLine("\nType the steam ID to promote to leader:");
                var input = Console.ReadLine();
                if (!ulong.TryParse(input, out var steamId))
                {
                    Console.WriteLine("Invalid input, please try again.");
                    break;
                }
                await new PromoteToLeader(rustPlus).PromoteToLeaderAsync(steamId);
                break;
            case "4":
                Console.WriteLine("\nType your message to send to the team:");
                var message = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(message))
                {
                    Console.WriteLine("Message cannot be empty, please try again.");
                    break;
                }
                await new SendTeamMessage(rustPlus).SendTeamMessageAsync(message);
                break;
            default:
                Console.WriteLine("Invalid choice, please try again.");
                break;
        }

        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey(intercept: true);
    }
}

async Task ElectricityFeatureMenuAsync()
{
    while (true)
    {
        Console.Clear();
        Console.WriteLine("Electricity Menu:");
        Console.WriteLine("0. Back to main menu");
        Console.WriteLine("1. Get Alarm Info");
        Console.WriteLine("2. Check Subscription");
        Console.WriteLine("3. Set Subscription");
        Console.WriteLine("4. Get Storage Monitor Info");
        Console.WriteLine("5. Get Smart Switch Info");
        Console.WriteLine("6. Set Smart Switch Value");
        Console.WriteLine("7. Strobe Smart Switch");
        Console.WriteLine("8. Toggle Smart Switch");

        Console.Write("\nPlease enter your choice: ");
        switch (Console.ReadLine())
        {
            case "0":
                return;
            case "1":
                await new GetAlarmInfo(rustPlus).GetAlarmInfoAsync(GetEntityId("alarmId"));
                break;
            case "2":
                await new CheckSubscription(rustPlus).CheckSubscriptionAsync(GetEntityId("alarmId"));
                break;
            case "3":
                Console.Write("\nType 'y' to activate the alarm, any other key to deactivate: ");
                var input = Console.ReadLine();
                var doSubscribe = string.Equals(input?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
                await new SetSubscription(rustPlus).SetSubscriptionAsync(GetEntityId("alarmId"), doSubscribe);
                break;
            case "4":
                await new GetStorageMonitorInfo(rustPlus).GetStorageMonitorInfoAsync(GetEntityId("storageMonitorId"));
                break;
            case "5":
                await new GetSmartSwitchInfo(rustPlus).GetSmartSwitchInfoAsync(GetEntityId("smartSwitchId"));
                break;
            case "6":
                Console.Write("\nType 'y' to activate the smart switch, any other key to deactivate: ");
                input = Console.ReadLine();
                var smartSwitchValue = string.Equals(input?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
                await new SetSmartSwitchValue(rustPlus).SetSmartSwitchValueAsync(GetEntityId("smartSwitchId"), smartSwitchValue);
                break;
            case "7":
                await new StrobeSmartSwitch(rustPlus).StrobeSmartSwitchAsync(GetEntityId("smartSwitchId"));
                break;
            case "8":
                await new ToggleSmartSwitch(rustPlus).ToggleSmartSwitchAsync(GetEntityId("smartSwitchId"));
                break;
        }

        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey(intercept: true);
    }
}

ulong GetEntityId(string type)
{
    while (true)
    {
        Console.Write($"\nType the {type}: ");
        if (ulong.TryParse(Console.ReadLine(), out var entityId))
        {
            return entityId;
        }
        Console.WriteLine("Invalid input, please try again.");
    }
}
