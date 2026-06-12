using RustPlus.Camera.ConsoleApp.Features;
using RustPlus.ConsoleApp.Utils;

// Fill credentials.json (copy credentials.sample.json) with the ip/port/playerId/playerToken
// printed by the RustPlus.Register.ConsoleApp sample when you "Pair with Server" in game.
// Put it next to this project (gitignored), or pass its path as the first argument.
//
// Interactive mode (default): prompts for a camera identifier and opens a managed session
// (CameraController) with ASCII preview, PNG save, movement/look, PTZ zoom and turret actions.
//
// Headless capture mode (render-fixture generation):
//   RustPlus.Camera.ConsoleApp [credentialsPath] capture <cameraId> <durationSeconds> [outputDir]
var captureIndex = Array.FindIndex(args, a => a.Equals("capture", StringComparison.OrdinalIgnoreCase));
var configFilePath = args.Length > 0 && captureIndex != 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "credentials.json");

CredentialsReaderUtility.Credentials credentials;
try
{
    credentials = configFilePath.GetConfig();
}
catch (FileNotFoundException)
{
    Console.WriteLine($"Config file not found at: {configFilePath}");
    Console.WriteLine("Copy credentials.sample.json to credentials.json and fill in your server details.");
    return;
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to load config: {ex.Message}");
    return;
}

using var rustPlus = new RustPlusApi.RustPlus(new RustPlusApi.RustPlusConnection(credentials.Ip, credentials.Port,
    credentials.PlayerId, credentials.PlayerToken));

try
{
    await rustPlus.ConnectAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to connect to {credentials.Ip}:{credentials.Port} — {ex.Message}");
    Console.WriteLine("Check that the server is up and the credentials are current.");
    return;
}

if (captureIndex >= 0)
{
    if (args.Length <= captureIndex + 2 || !int.TryParse(args[captureIndex + 2], out var seconds) || seconds <= 0)
    {
        Console.WriteLine("Usage: RustPlus.Camera.ConsoleApp [credentialsPath] capture <cameraId> <durationSeconds> [outputDir]");
        await rustPlus.DisconnectAsync();
        return;
    }

    var outDir = args.Length > captureIndex + 3 ? args[captureIndex + 3] : Environment.CurrentDirectory;
    var exitCode = await new CameraCapture(rustPlus).RunAsync(args[captureIndex + 1], TimeSpan.FromSeconds(seconds), outDir);
    await rustPlus.DisconnectAsync();
    Environment.Exit(exitCode);
}

var ids = new EntityIdStore();
var again = true;
while (again)
{
    await new CameraSession(rustPlus, ids).RunAsync();

    Console.Write("\nOpen another camera? (y/N): ");
    again = string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
}

await rustPlus.DisconnectAsync();
