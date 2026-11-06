using Microsoft.Extensions.Logging;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;

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

// The first Ctrl+C cancels the run instead of killing the process, so a login in progress closes its
// browser and cleans up its temporary profile. A second Ctrl+C is let through and ends the process at once.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    if (cts.IsCancellationRequested)
    {
        return;
    }

    e.Cancel = true;
    cts.Cancel();
};
var cancellationToken = cts.Token;

// Used only when the library cannot drive a browser (none found, Snap-only, or it failed to start).
// The first argument is why; print it, then how to fetch the address by hand, then read one line.
var registration = new FcmRegistration
{
    SteamLoginOptions = new SteamLoginOptions
    {
        ReadPastedAddress = async (reason, ct) =>
        {
            Console.WriteLine($"     {reason}");
            Console.WriteLine($"     {SteamLoginService.PasteInstructions}");
            Console.Write("Paste the address here: ");
            // Console.ReadLine cannot be cancelled, so read on a background task and stop waiting on Ctrl+C.
            var line = await Task.Run(Console.ReadLine, CancellationToken.None).WaitAsync(ct);
            return line ?? "";
        },
    },
};

try
{
    Console.WriteLine("1/4  Acquiring FCM credentials (GCM check-in, Firebase, FCM, Expo)…");
    var credentials = await registration.AcquireCredentialsAsync(cancellationToken);

    Console.WriteLine(
        "2/4  Opening a browser window (Edge, Chrome, Brave or Chromium) for the Steam login: sign in there…");
    var steamLogin = await registration.RegisterWithRustPlusAsync(credentials, onLoginUrl: url =>
    {
        Console.WriteLine("     Login address:");
        Console.WriteLine($"     {url}");
    }, cancellationToken: cancellationToken);
    Console.WriteLine($"     Signed in as {steamLogin.SteamId}.");

    Console.WriteLine($"3/4  Saving credentials to {configPath}…");
    CredentialsStore.Save(configPath, credentials);

    Console.WriteLine("4/4  Now open Rust, go to a server and choose 'Pair with Server'. Waiting…");
    using var listener = new PairingListener(credentials, loggerFactory: loggerFactory);
    listener.Listening += (_, _) => Console.WriteLine("     Listening for pairing notifications…");

    var pairing = await listener.WaitForServerPairingAsync(cancellationToken);

    Console.WriteLine();
    Console.WriteLine("Paired! Use these arguments:");
    Console.WriteLine(
        $"  new RustPlus(new RustPlusConnection(\"{pairing.Ip}\", {pairing.Port}, {pairing.PlayerId}, {pairing.PlayerToken}));");
    Console.WriteLine($"  (server: {pairing.Name})");
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.WriteLine();
    Console.WriteLine("Cancelled.");
}
