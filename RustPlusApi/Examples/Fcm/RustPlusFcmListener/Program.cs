using Newtonsoft.Json;

using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;

using static __Constants.ExamplesConst;

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

var listener = new RustPlusFcmListener(credentials);

listener.Connecting += (_, _) =>
{
    Console.WriteLine($"[CONNECTING]: {DateTime.Now}");
};

listener.Connected += (_, _) =>
{
    Console.WriteLine($"[CONNECTED]: {DateTime.Now}");
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

/* Specials events */

listener.OnServerPairing += (_, pairing) =>
{
    Console.WriteLine($"[SERVER PAIRING]:\n{JsonConvert.SerializeObject(pairing, JsonSettings)}");
};

listener.OnEntityParing += (_, pairing) =>
{
    Console.WriteLine($"[ENTITY PAIRING]:\n{JsonConvert.SerializeObject(pairing, JsonSettings)}");
};

listener.OnAlarmTriggered += (_, alarm) =>
{
    Console.WriteLine($"[ALARM TRIGGERED]:\n{JsonConvert.SerializeObject(alarm, JsonSettings)}");
};

await listener.ConnectAsync();

Console.ReadLine();
listener.Disconnect();