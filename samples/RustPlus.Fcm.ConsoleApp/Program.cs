using System.Diagnostics;
using System.Text.Json;
using RustPlus.Fcm.ConsoleApp.Utils;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;

// Path to the JavaScript config file, see sample-config.json for an example.
// Make sure to run 'npx @liamcottle/rustplus.js fcm-register' first to generate this file.
const string configPath = @"<path of rustplus.js config file>\rustplus.config.json";

Credentials credentials;
try
{
    var jsConfig = configPath.ReadJavaScriptConfig();
    credentials = jsConfig.ConvertToCredentials();

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

var listener = new RustPlusFcm(credentials);

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

listener.OnParing += (_, pairing) =>
{
    Debug.WriteLine($"[PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
};

listener.OnServerPairing += (_, pairing) =>
{
    Console.WriteLine($"[SERVER PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
};

listener.OnEntityParing += (_, pairing) =>
{
    Debug.WriteLine($"[ENTITY PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
};

listener.OnSmartSwitchParing += (_, pairing) =>
{
    Console.WriteLine($"[SMART SWITCH PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
};

listener.OnStorageMonitorParing += (_, pairing) =>
{
    Console.WriteLine($"[STORAGE MONITOR PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
};

listener.OnSmartAlarmParing += (_, pairing) =>
{
    Console.WriteLine($"[SMART ALARM PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
};

listener.OnAlarmTriggered += (_, alarm) =>
{
    Console.WriteLine($"[ALARM TRIGGERED]:\n{JsonSerializer.Serialize(alarm, JsonUtilities.JsonOptions)}");
};

await listener.ConnectAsync();

Console.ReadLine();
listener.Disconnect();