using Newtonsoft.Json;

using RustPlusApi;

using RustPlusContracts;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connecting += (_, _) => Console.WriteLine("Connecting...");
rustPlus.Connected += (_, _) => Console.WriteLine("Connected...");

rustPlus.SendingRequest += (_, _) => Console.WriteLine("Sending request...");
rustPlus.RequestSent += (_, message) => Console.WriteLine($"Request:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

// Message is always triggered (notification + response)
rustPlus.MessageReceived += (_, message) => Console.WriteLine($"Message:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

rustPlus.NotificationReceived += (_, message) => Console.WriteLine($"Notification:\n{JsonConvert.SerializeObject(message, JsonSettings)}");
rustPlus.ResponseReceived += (_, message) => Console.WriteLine($"Response:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

rustPlus.Disconnecting += (_, _) => Console.WriteLine("Disconnecting...");
rustPlus.Disconnected += (_, _) => Console.WriteLine("Disconnected...");

rustPlus.ErrorOccurred += (_, ex) => Console.WriteLine($"Error:\n{ex}");

await rustPlus.ConnectAsync();

// Change this part as you wish
var request = new AppRequest
{
    GetTime = new AppEmpty()
};
await rustPlus.SendRequestAsync(request);

await rustPlus.DisconnectAsync();
Console.WriteLine("Every task went well");