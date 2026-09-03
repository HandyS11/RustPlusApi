using ProtoGen;

// Usage: ProtoGen <decompiled.cs> <committed.proto> <output.proto> [--allow-field-removal]
// Regenerates RustPlusContracts.proto from the decompiled SilentOrbit server contracts,
// preserving committed conventions (labels, nesting, snake_case, ordering).
//
// Exit codes: 0 = written; 2 = usage/input error; 3 = committed fields went missing (see
// FieldLossGuard); 4 = a root message was not found in the decompiled contracts. Drift itself
// is not detected here — update-proto.sh diffs the output. Anything other than 0 or 1 fails
// the ProtoRefresh workflow, so every non-zero code here surfaces as a red run.

const int exitUsage = 2;
const int exitFieldLoss = 3;
const int exitMissingRoot = 4;

// Roots: everything the wire exposes hangs off the request and the message envelope.
string[] roots = ["AppRequest", "AppMessage"];

const string allowFieldRemovalFlag = "--allow-field-removal";
const string usage =
    $"usage: ProtoGen <decompiled.cs> <committed.proto> <output.proto> [{allowFieldRemovalFlag}]";

// Reject unknown options rather than ignoring them: a typo such as "--allow-field-removals"
// would otherwise be dropped silently and the operator would see the guard still firing with no
// indication why their flag had no effect.
var unknownFlags = args
    .Where(a => a.StartsWith('-') && !string.Equals(a, allowFieldRemovalFlag, StringComparison.Ordinal))
    .ToArray();
if (unknownFlags.Length > 0)
{
    await Console.Error.WriteLineAsync($"error: unknown option(s): {string.Join(", ", unknownFlags)}")
        .ConfigureAwait(false);
    await Console.Error.WriteLineAsync(usage).ConfigureAwait(false);
    return exitUsage;
}

var allowFieldRemoval = args.Contains(allowFieldRemovalFlag, StringComparer.Ordinal);
var positional = args.Where(a => !a.StartsWith('-')).ToArray();

if (positional.Length != 3)
{
    await Console.Error.WriteLineAsync(usage).ConfigureAwait(false);
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

// Without a root there is no authoritative closure: every committed type falls through to the
// "preserved verbatim" path, the output reproduces the committed proto exactly, and both the
// diff and the field-loss guard come up empty. A total parse failure would be reported as
// "no changes — the committed proto matches the current server", which is the worst possible
// lie this tool can tell. Refuse instead.
var missingRoots = server.MissingRoots(roots);
if (missingRoots.Count > 0)
{
    await Console.Error
        .WriteLineAsync(
            $"error: root message(s) not found in the decompiled contracts: {string.Join(", ", missingRoots)}")
        .ConfigureAwait(false);
    await Console.Error
        .WriteLineAsync("The contracts may have been renamed or relocated, or the decompiled input may be")
        .ConfigureAwait(false);
    await Console.Error
        .WriteLineAsync("the wrong assembly. Regenerating without a root would silently reproduce the")
        .ConfigureAwait(false);
    await Console.Error
        .WriteLineAsync("committed proto and report no drift, so this is a hard failure.")
        .ConfigureAwait(false);
    return exitMissingRoot;
}

var committed = CommittedProto.Parse(committedPath);
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
        .WriteLineAsync($">> ⚠️  {losses.Count} committed field(s) removed, allowed by {allowFieldRemovalFlag}")
        .ConfigureAwait(false);
    return 0;
}

await Console.Error.WriteLineAsync(FieldLossGuard.Describe(losses)).ConfigureAwait(false);
return exitFieldLoss;
