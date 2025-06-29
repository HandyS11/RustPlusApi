using static RustPlus.ConsoleApp.Credentials;

var rustPlus = new RustPlusApi.RustPlus(Ip, Port, PlayerId, PlayerToken);

await rustPlus.ConnectAsync();

var isRunning = true;
while (isRunning)
{
    Console.WriteLine("Choose an option:");
    Console.WriteLine("0. Exit");

    var choice = Console.ReadLine();

    switch (choice)
    {
        case "0":
            isRunning = false;
            break;
        default:
            Console.WriteLine("Invalid choice, please try again.");
            break;
    }
}

await rustPlus.DisconnectAsync();