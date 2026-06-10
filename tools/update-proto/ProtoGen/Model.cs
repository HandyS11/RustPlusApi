namespace ProtoGen;

/// <summary>A protobuf field recovered from the decompiled server (authoritative wire info).</summary>
internal sealed class Field
{
    /// <summary>C# name (camelCase), e.g. "jpgImage".</summary>
    public required string Name { get; init; }

    /// <summary>Proto field number.</summary>
    public required int Number { get; init; }

    /// <summary>Resolved proto type, e.g. "uint32" or "AppMap.Monument".</summary>
    public required string ProtoType { get; init; }

    public required bool Repeated { get; init; }
}

/// <summary>A protobuf message recovered from a SilentOrbit <c>IProto&lt;T&gt;</c> class.</summary>
internal sealed class Message
{
    /// <summary>Qualified name, e.g. "AppMap.Monument".</summary>
    public required string QualifiedName { get; init; }

    /// <summary>Simple name, e.g. "Monument".</summary>
    public required string SimpleName { get; init; }

    /// <summary>Null for top-level messages.</summary>
    public string? ParentQualifiedName { get; init; }

    public List<Field> Fields { get; } = [];
}

internal sealed class EnumDef
{
    public required string QualifiedName { get; init; }
    public required string SimpleName { get; init; }
    public string? ParentQualifiedName { get; init; }
    public List<(string Name, int Value)> Values { get; } = [];
}
