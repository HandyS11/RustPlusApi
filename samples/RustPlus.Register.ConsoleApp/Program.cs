using RustPlusApi.Fcm.Registration;

// The C# analog of `rustplus.js fcm-register`: run it once, log into Steam in the browser,
// pair in game, and it writes rustplus.config.json + prints the RustPlus(...) args.
//
// NOTE: every step below hits live Google/Expo/Facepunch services and is upstream-fragile.

const string configPath = "rustplus.config.json";

var registration = new FcmRegistration();

Console.WriteLine("1/4  Acquiring FCM credentials (GCM check-in, Firebase, FCM, Expo)…");
var credentials = await registration.AcquireCredentialsAsync();

Console.WriteLine("2/4  Launching Chrome/Chromium for Steam login — sign in through Steam…");
Console.WriteLine("     (Requires Chrome or Chromium installed; set CHROME_PATH if it isn't found.)");
await registration.RegisterWithRustPlusAsync(credentials);

Console.WriteLine($"3/4  Saving credentials to {configPath}…");
CredentialsStore.Save(configPath, credentials);

Console.WriteLine("4/4  Now open Rust, go to a server and choose 'Pair with Server'. Waiting…");
using var listener = new PairingListener(credentials);
listener.Listening += (_, _) => Console.WriteLine("     Listening for pairing notifications…");

var pairing = await listener.WaitForServerPairingAsync();

Console.WriteLine();
Console.WriteLine("Paired! Use these arguments:");
Console.WriteLine($"  new RustPlus(\"{pairing.Ip}\", {pairing.Port}, {pairing.PlayerId}, {pairing.PlayerToken});");
Console.WriteLine($"  (server: {pairing.Name})");
