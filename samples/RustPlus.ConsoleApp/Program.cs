using RustPlus.ConsoleApp.Events;
using RustPlus.ConsoleApp.Features;
using static RustPlus.ConsoleApp.Credentials;

var rustPlus = new RustPlusApi.RustPlus(Ip, Port, PlayerId, PlayerToken);

//await rustPlus.ConnectAsync();

//new SmartSwitchChanges(rustPlus).Setup();
//new StorageMonitorChanges(rustPlus).Setup();
//new TeamChatChanges(rustPlus).Setup();

var isRunning = true;
while (isRunning)
{
    Console.Clear();
    Console.WriteLine("Choose an option:");
    Console.WriteLine("0. Exit");
    Console.WriteLine("1. Common Features");

    Console.Write("\nPlease enter your choice: ");
    var choice = Console.ReadLine();

    switch (choice)
    {
        case "0":
            isRunning = false;
            break;
        case "1":
            await CommonFeatureMenu();
            break;
        case "2":
            await TeamFeatureMenu();
            break;
        default:
            Console.WriteLine("Invalid choice, please try again.");
            break;
    }
    
    Console.WriteLine("\nPress any key to continue...");
    Console.ReadLine();
}

await rustPlus.DisconnectAsync();
return;

async Task CommonFeatureMenu()
{
    var isInCommon = true;
    while (isInCommon)
    {
        Console.Clear();
        Console.WriteLine("Common Menu:");
        Console.WriteLine("0. Back to main menu");
        Console.WriteLine("1. Get Info");
        Console.WriteLine("2. Get Map");
        Console.WriteLine("3. Get Map Markers");
        Console.WriteLine("4. Get Time");

        Console.Write("\nPlease enter your choice: ");
        var choice = Console.ReadLine();

        switch (choice)
        {
            case "0":
                isInCommon = false;
                break;
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
        Console.ReadLine();
    }
}

async Task TeamFeatureMenu()
{
    var isInTeam = true;
    while (isInTeam)
    {
        Console.Clear();
        Console.WriteLine("Team Menu:");
        Console.WriteLine("0. Back to main menu");
        Console.WriteLine("1. Get Team Info");
        Console.WriteLine("2. Get Team Chat");
        Console.WriteLine("3. Promote to Leader");
        Console.WriteLine("4. Send Team Message");

        Console.Write("\nPlease enter your choice: ");
        var choice = Console.ReadLine();

        switch (choice)
        {
            case "0":
                isInTeam = false;
                break;
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
        Console.ReadLine();
    }
}