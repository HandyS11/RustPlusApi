using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Extensions;

var credentials = new Credentials
{
    Keys = new Keys
    {
        PrivateKey = "",
        PublicKey = "",
        AuthSecret = "",
    },
    Gcm = new Gcm
    {
        AndroidId = 0,
        SecurityToken = 0,
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
    Console.WriteLine($"[NOTIFICATION]: {DateTime.Now}:\n{message.ToFcmMessageString()}");
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