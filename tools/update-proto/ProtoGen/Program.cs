using ProtoGen;

// Usage: ProtoGen <decompiled.cs> <committed.proto> <output.proto>
// Regenerates RustPlusContracts.proto from the decompiled SilentOrbit server contracts,
// preserving committed conventions (labels, nesting, snake_case, ordering).

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: ProtoGen <decompiled.cs> <committed.proto> <output.proto>");
    return 2;
}

var (decompiledPath, committedPath, outputPath) = (args[0], args[1], args[2]);

foreach (var (label, path) in new[] { ("decompiled", decompiledPath), ("committed proto", committedPath) })
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"error: {label} not found: {path}");
        return 2;
    }
}

Console.Error.WriteLine($">> parsing server contracts: {decompiledPath}");
var server = ServerParser.Parse(decompiledPath);
Console.Error.WriteLine($">> parsed {server.Messages.Count} messages, {server.Enums.Count} enums (namespace ProtoBuf)");

var committed = CommittedProto.Parse(committedPath);

// Roots: everything the wire exposes hangs off the request and the message envelope.
string[] roots = ["AppRequest", "AppMessage"];
var proto = Emitter.Emit(server, committed, roots);

File.WriteAllText(outputPath, proto);
Console.Error.WriteLine($">> wrote {outputPath}");
return 0;
