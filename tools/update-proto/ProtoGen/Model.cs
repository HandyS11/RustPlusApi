namespace ProtoGen;

/// <summary>A protobuf field recovered from the decompiled server (authoritative wire info).</summary>
internal sealed class Field
{
    public required string Name { get; init; }          // C# name (camelCase), e.g. "jpgImage"
    public required int Number { get; init; }           // proto field number
    public required string ProtoType { get; init; }     // resolved proto type, e.g. "uint32" or "AppMap.Monument"
    public required bool Repeated { get; init; }
}

/// <summary>A protobuf message recovered from a SilentOrbit <c>IProto&lt;T&gt;</c> class.</summary>
internal sealed class Message
{
    public required string QualifiedName { get; init; } // e.g. "AppMap.Monument"
    public required string SimpleName { get; init; }    // e.g. "Monument"
    public string? ParentQualifiedName { get; init; }   // null for top-level
    public List<Field> Fields { get; } = [];
}

internal sealed class EnumDef
{
    public required string QualifiedName { get; init; }
    public required string SimpleName { get; init; }
    public string? ParentQualifiedName { get; init; }
    public List<(string Name, int Value)> Values { get; } = [];
}
