using Newtonsoft.Json;

using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Configuration;
using RustPlusApi.Fcm.Data;

using static __Constants.ExamplesConst;

// Path to the JavaScript config file
var configPath = @"<path of rustplus.js config file>\rustplus.config.json";

Credentials credentials;
JavaScriptConfig jsConfig;
try
{
    // Step 1: Read the JavaScript config file
    jsConfig = FcmConfigurationReader.ReadJavaScriptConfig(configPath);

    // Step 2: Convert to credentials format
    credentials = FcmConfigurationReader.ConvertToCredentials(jsConfig);

    Console.WriteLine($"Loaded credentials - AndroidId: {credentials.Gcm.AndroidId}");
}
catch (FileNotFoundException)
{
    Console.WriteLine($"Config file not found at: {configPath}");
    Console.WriteLine("Please run 'npx @liamcottle/rustplus.js fcm-register' first and update the path above.");
    return;
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to load config: {ex.Message}");
    return;
}

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