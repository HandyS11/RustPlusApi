using Newtonsoft.Json;

using RustPlusApi;

using RustPlusContracts;

using static __Constants.ExamplesConst;

var rustPlus = new RustPlusLegacy(Ip, Port, PlayerId, PlayerToken);

rustPlus.Connecting += (_, _) => Console.WriteLine("Connecting...");

rustPlus.Connected += async (_, _) =>
{
    Console.WriteLine("Connected...");

    var request = new AppRequest
    {
        GetInfo = new AppEmpty()
    };
    var message = await rustPlus.SendRequestAsync(request);
};

rustPlus.RequestSent += (_, message) => Console.WriteLine($"Request:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

rustPlus.MessageReceived += (_, message) => Console.WriteLine($"Message:\n{JsonConvert.SerializeObject(message, JsonSettings)}");

rustPlus.Disconnected += (_, _) => Console.WriteLine("Disconnected...");

rustPlus.ErrorOccurred += (_, ex) => Console.WriteLine($"Error:\n{ex}");

await rustPlus.ConnectAsync();

await Task.Delay(5000);

rustPlus.Dispose();