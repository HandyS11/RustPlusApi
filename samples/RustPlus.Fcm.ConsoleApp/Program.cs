using RustPlus.Fcm.ConsoleApp.Utils;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using System.Diagnostics;
using System.Text.Json;

// Credentials come from the RustPlus.Register.ConsoleApp sample (recommended) or from
// 'npx @liamcottle/rustplus.js fcm-register'. Put the resulting file next to this project as
// 'rustplus.config.json' (gitignored), or pass its path as the first argument.
var configPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "rustplus.config.json");

Credentials credentials;
try
{
    credentials = configPath.LoadCredentials();
    Console.WriteLine($"Loaded credentials - AndroidId: {credentials.Gcm.AndroidId}");
}
catch (FileNotFoundException)
{
    Console.WriteLine($"Config file not found at: {configPath}");
    Console.WriteLine(
        "Run the RustPlus.Register.ConsoleApp sample first (or 'npx @liamcottle/rustplus.js fcm-register'),");
    Console.WriteLine(
        "then copy the resulting rustplus.config.json next to this project (or pass its path as an argument).");
    return;
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to load config: {ex.Message}");
    return;
}

// Persist the server-assigned persistentIds between runs so already-processed notifications are not
// redelivered after a restart. Stored next to the config (gitignored). Ids have a server-side
// lifespan; a real app should prune this (e.g. cap the count) — kept simple here.
var persistentIdsPath = Path.Combine(AppContext.BaseDirectory, "persistent-ids.json");
var persistentIds = File.Exists(persistentIdsPath)
    ? JsonSerializer.Deserialize<HashSet<string>>(await File.ReadAllTextAsync(persistentIdsPath)) ?? []
    : new HashSet<string>();
Console.WriteLine($"Loaded {persistentIds.Count} persistent id(s).");

using var listener = new RustPlusFcm(credentials, persistentIds);

listener.PersistentIdReceived += (_, _) =>
    File.WriteAllText(persistentIdsPath, JsonSerializer.Serialize(listener.PersistentIds));

listener.Connecting += (_, _) => Console.WriteLine($"[CONNECTING]: {DateTime.Now}");
listener.Connected += (_, _) => Console.WriteLine($"[CONNECTED]: {DateTime.Now}");
listener.SocketClosed += (_, _) => Console.WriteLine($"[SOCKET CLOSED]: {DateTime.Now}");
listener.ErrorOccurred += (_, error) => Console.WriteLine($"[ERROR]: {error}");
listener.Disconnecting += (_, _) => Console.WriteLine($"[DISCONNECTING]: {DateTime.Now}");
listener.Disconnected += (_, _) => Console.WriteLine($"[DISCONNECTED]: {DateTime.Now}");

/* Specials events */

listener.OnPairing += (_, pairing) =>
{
    // Not display in console to not spam the output.
    Debug.WriteLine($"[PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
};

listener.OnServerPairing += (_, pairing) =>
    Console.WriteLine($"[SERVER PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");

listener.OnEntityPairing += (_, pairing) =>
{
    // Not display in console to not spam the output.
    Debug.WriteLine($"[ENTITY PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
};

listener.OnSmartSwitchPairing += (_, pairing) =>
    Console.WriteLine($"[SMART SWITCH PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
listener.OnStorageMonitorPairing += (_, pairing) =>
    Console.WriteLine($"[STORAGE MONITOR PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
listener.OnSmartAlarmPairing += (_, pairing) =>
    Console.WriteLine($"[SMART ALARM PAIRING]:\n{JsonSerializer.Serialize(pairing, JsonUtilities.JsonOptions)}");
listener.OnAlarmTriggered += (_, alarm) =>
    Console.WriteLine($"[ALARM TRIGGERED]:\n{JsonSerializer.Serialize(alarm, JsonUtilities.JsonOptions)}");

try
{
    await listener.ConnectAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to connect to FCM: {ex.Message}");
    return;
}

Console.ReadLine();
listener.Disconnect();
