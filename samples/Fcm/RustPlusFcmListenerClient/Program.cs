using System.Text.Json;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using static Constants.ExamplesConst;

// See RustPlusFcmListener/Program.cs for an example where the credentials are read from a JavaScript config file.
var credentials = new Credentials
{
    Gcm = new Gcm
    {
        AndroidId = 5688303636103341924,
        SecurityToken = 2231918422921550740,
    }
};

var listener = new RustPlusFcmListenerClient(credentials);

listener.Connecting += (_, _) =>
{
    Console.WriteLine($"[CONNECTING]: {DateTime.Now}");
};

listener.Connected += (_, _) =>
{
    Console.WriteLine($"[CONNECTED]: {DateTime.Now}");
};

listener.NotificationReceived += (_, message) =>
{
    using var doc = JsonDocument.Parse(message);
    var indentedJson = JsonSerializer.Serialize(doc.RootElement, JsonOptions);
    Console.WriteLine($"[NOTIFICATION]: {DateTime.Now}:\n{indentedJson}");
};

listener.SocketClosed += (_, _) =>
{
    Console.WriteLine($"[SOCKET CLOSED]: {DateTime.Now}");
};

listener.ErrorOccurred += (_, error) =>
{
    Console.WriteLine($"[ERROR]: {error}");
};

listener.Disconnecting += (_, _) =>
{
    Console.WriteLine($"[DISCONNECTING]: {DateTime.Now}");
};

listener.Disconnected += (_, _) =>
{
    Console.WriteLine($"[DISCONNECTED]: {DateTime.Now}");
};

await listener.ConnectAsync();

Console.ReadLine();
listener.Disconnect();