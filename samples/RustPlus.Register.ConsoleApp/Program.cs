using Microsoft.Extensions.Logging;
using RustPlusApi.Fcm.Registration;

// The C# analog of `rustplus.js fcm-register`: run it once, log into Steam in the browser,
// pair in game, and it writes rustplus.config.json + prints the RustPlus(...) args.
//
// NOTE: every step below hits live Google/Expo/Facepunch services and is upstream-fragile.

var configPath = Path.GetFullPath("rustplus.config.json");

// Debug-level console logging so notifications the listener skips (no app data, unknown
// channel/pairing type, …) are visible instead of silently dropped — without this, a missed
// pairing push is indistinguishable from one that never reached the socket.
using var loggerFactory = LoggerFactory.Create(static builder => builder
    .SetMinimumLevel(LogLevel.Debug)
    .AddSimpleConsole(static console => console.SingleLine = true));

var registration = new FcmRegistration();

Console.WriteLine("1/4  Acquiring FCM credentials (GCM check-in, Firebase, FCM, Expo)…");
var credentials = await registration.AcquireCredentialsAsync();

Console.WriteLine("2/4  Opening your browser for the Steam login — sign in through Steam…");
var steamLogin = await registration.RegisterWithRustPlusAsync(credentials, onLoginUrl: url =>
{
    Console.WriteLine("     If your browser didn't open, visit this URL yourself:");
    Console.WriteLine($"     {url}");
});
Console.WriteLine($"     Signed in as {steamLogin.SteamId}.");

Console.WriteLine($"3/4  Saving credentials to {configPath}…");
CredentialsStore.Save(configPath, credentials);

Console.WriteLine("4/4  Now open Rust, go to a server and choose 'Pair with Server'. Waiting…");
using var listener = new PairingListener(credentials, loggerFactory: loggerFactory);
listener.Listening += (_, _) => Console.WriteLine("     Listening for pairing notifications…");

var pairing = await listener.WaitForServerPairingAsync();

Console.WriteLine();
Console.WriteLine("Paired! Use these arguments:");
Console.WriteLine(
    $"  new RustPlus(new RustPlusConnection(\"{pairing.Ip}\", {pairing.Port}, {pairing.PlayerId}, {pairing.PlayerToken}));");
Console.WriteLine($"  (server: {pairing.Name})");
