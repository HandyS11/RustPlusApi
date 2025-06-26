using System.Text.Json;
using RustPlusApi.Fcm.Data;
using RustPlusFcmListener;
using static Constants.ExamplesConst;

// Path to the JavaScript config file, see sample-config.json for an example.
// Make sure to run 'npx @liamcottle/rustplus.js fcm-register' first to generate this file.
const string configPath = @"<path>\rustplus.config.json";

Credentials credentials;
try
{
    // Step 1: Read the JavaScript config file
    var jsConfig = FcmConfigurationReader.ReadJavaScriptConfig(configPath);

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

var listener = new RustPlusApi.Fcm.RustPlusFcmListener(credentials);

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
    Console.WriteLine($"[SERVER PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonOptions)}");
};

listener.OnEntityParing += (_, pairing) =>
{
    Console.WriteLine($"[ENTITY PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonOptions)}");
};

listener.OnAlarmTriggered += (_, alarm) =>
{
    Console.WriteLine($"[ALARM TRIGGERED]:\n{JsonSerializer.Serialize(alarm, JsonOptions)}");
};

await listener.ConnectAsync();

Console.ReadLine();
listener.Disconnect();