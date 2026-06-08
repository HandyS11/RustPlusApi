using ProtoGen;

// Usage: ProtoGen <decompiled.cs> <committed.proto> <output.proto>
// Regenerates RustPlusContracts.proto from the decompiled SilentOrbit server contracts,
// preserving committed conventions (labels, nesting, snake_case, ordering).

if (args.Length != 3)
{
    await Console.Error.WriteLineAsync("usage: ProtoGen <decompiled.cs> <committed.proto> <output.proto>").ConfigureAwait(false);
    return 2;
}

var (decompiledPath, committedPath, outputPath) = (args[0], args[1], args[2]);

foreach (var (label, path) in new[] { ("decompiled", decompiledPath), ("committed proto", committedPath) })
{
    if (!File.Exists(path))
    {
        await Console.Error.WriteLineAsync($"error: {label} not found: {path}").ConfigureAwait(false);
        return 2;
    }
}

await Console.Error.WriteLineAsync($">> parsing server contracts: {decompiledPath}").ConfigureAwait(false);
var server = ServerParser.Parse(decompiledPath);
await Console.Error.WriteLineAsync($">> parsed {server.Messages.Count} messages, {server.Enums.Count} enums (namespace ProtoBuf)").ConfigureAwait(false);

var committed = CommittedProto.Parse(committedPath);

// Roots: everything the wire exposes hangs off the request and the message envelope.
string[] roots = ["AppRequest", "AppMessage"];
var proto = Emitter.Emit(server, committed, roots);

await File.WriteAllTextAsync(outputPath, proto).ConfigureAwait(false);
await Console.Error.WriteLineAsync($">> wrote {outputPath}").ConfigureAwait(false);
return 0;
