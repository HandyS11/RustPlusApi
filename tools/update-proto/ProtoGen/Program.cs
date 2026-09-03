using ProtoGen;

// Usage: ProtoGen <decompiled.cs> <committed.proto> <output.proto> [--allow-field-removal]
// Regenerates RustPlusContracts.proto from the decompiled SilentOrbit server contracts,
// preserving committed conventions (labels, nesting, snake_case, ordering).
//
// Exit codes: 0 = written; 2 = usage/input error; 3 = committed fields went missing (see
// FieldLossGuard). Drift itself is not detected here — update-proto.sh diffs the output.

const int exitUsage = 2;
const int exitFieldLoss = 3;

var allowFieldRemoval = args.Contains("--allow-field-removal", StringComparer.Ordinal);
var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

if (positional.Length != 3)
{
    await Console.Error
        .WriteLineAsync("usage: ProtoGen <decompiled.cs> <committed.proto> <output.proto> [--allow-field-removal]")
        .ConfigureAwait(false);
    return exitUsage;
}

var (decompiledPath, committedPath, outputPath) = (positional[0], positional[1], positional[2]);

foreach (var (label, path) in new[]
         {
             ("decompiled", decompiledPath), ("committed proto", committedPath)
         })
{
    if (!File.Exists(path))
    {
        await Console.Error.WriteLineAsync($"error: {label} not found: {path}").ConfigureAwait(false);
        return exitUsage;
    }
}

await Console.Error.WriteLineAsync($">> parsing server contracts: {decompiledPath}").ConfigureAwait(false);
var server = ServerParser.Parse(decompiledPath);
await Console.Error
    .WriteLineAsync($">> parsed {server.Messages.Count} messages, {server.Enums.Count} enums (namespace ProtoBuf)")
    .ConfigureAwait(false);

var committed = CommittedProto.Parse(committedPath);

// Roots: everything the wire exposes hangs off the request and the message envelope.
string[] roots = ["AppRequest", "AppMessage"];
var (proto, scopeMessages) = Emitter.Emit(server, committed, roots);

// Write before guarding: when the guard trips, the output is the evidence a human needs to see
// which messages came out empty. It goes to out/, never to the committed proto.
await File.WriteAllTextAsync(outputPath, proto).ConfigureAwait(false);
await Console.Error.WriteLineAsync($">> wrote {outputPath}").ConfigureAwait(false);

var losses = FieldLossGuard.Check(committed, server, scopeMessages);
if (losses.Count == 0)
{
    return 0;
}

if (allowFieldRemoval)
{
    await Console.Error
        .WriteLineAsync($">> ⚠️  {losses.Count} committed field(s) removed, allowed by --allow-field-removal")
        .ConfigureAwait(false);
    return 0;
}

await Console.Error.WriteLineAsync(FieldLossGuard.Describe(losses)).ConfigureAwait(false);
return exitFieldLoss;
