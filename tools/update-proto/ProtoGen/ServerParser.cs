using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ProtoGen;

/// <summary>
/// Parses the decompiled <c>Rust.Data.dll</c> (namespace <c>ProtoBuf</c>) into proto messages.
/// The server uses SilentOrbit-generated <c>IProto&lt;T&gt;</c> classes (no protobuf-net), so we
/// recover each field's number from the <c>Deserialize</c> switch and its type from the field
/// declaration. See proto-refresh-plan.md.
/// </summary>
internal sealed class ServerParser
{
    /// <summary>Maps simple names to their fully-qualified names (a simple name like "Member" can be ambiguous).</summary>
    private readonly Dictionary<string, List<string>> _bySimpleName = new(StringComparer.Ordinal);

    private readonly Dictionary<string, EnumDef> _enums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Message> _messages = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Message> Messages => _messages;
    public IReadOnlyDictionary<string, EnumDef> Enums => _enums;

    public static ServerParser Parse(string decompiledCsPath)
    {
        var text = File.ReadAllText(decompiledCsPath);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetCompilationUnitRoot();

        var parser = new ServerParser();

        // The companion contracts live in namespace `ProtoBuf` exactly (not ProtoBuf.Nexus etc.).
        foreach (var ns in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            if (ns.Name.ToString() != "ProtoBuf")
            {
                continue;
            }

            foreach (var member in ns.Members)
            {
                parser.Discover(member, parentQualified: null);
            }
        }

        // Second pass: now that every type name is registered, fill fields (needs type resolution).
        foreach (var ns in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            if (ns.Name.ToString() != "ProtoBuf")
            {
                continue;
            }

            // class- and struct-based messages (e.g. Half3 is a struct : IProto<Half3>).
            foreach (var type in ns.DescendantNodes().OfType<TypeDeclarationSyntax>()
                         .Where(t => t is ClassDeclarationSyntax or StructDeclarationSyntax))
            {
                parser.FillFields(type);
            }
        }

        return parser;
    }

    /// <summary>First pass: register every message/enum and the parent/child structure.</summary>
    /// <param name="member">The syntax node to register.</param>
    /// <param name="parentQualified">Qualified name of the enclosing type, or null at file scope.</param>
    private void Discover(MemberDeclarationSyntax member, string? parentQualified)
    {
        switch (member)
        {
            case TypeDeclarationSyntax cls and (ClassDeclarationSyntax or StructDeclarationSyntax)
                when IsProtoMessage(cls):
                DiscoverMessage(cls, parentQualified);
                break;
            case EnumDeclarationSyntax en:
                DiscoverEnum(en, parentQualified);
                break;
        }
    }

    /// <summary>Registers a proto message type and recurses into its members.</summary>
    /// <param name="cls">The class/struct declaration backing the message.</param>
    /// <param name="parentQualified">Qualified name of the enclosing type, or null at file scope.</param>
    private void DiscoverMessage(TypeDeclarationSyntax cls, string? parentQualified)
    {
        var simple = cls.Identifier.Text;
        var qualified = parentQualified is null ? simple : $"{parentQualified}.{simple}";
        _messages[qualified] = new Message
        {
            QualifiedName = qualified, SimpleName = simple, ParentQualifiedName = parentQualified
        };
        Register(simple, qualified);

        foreach (var inner in cls.Members)
        {
            Discover(inner, qualified);
        }
    }

    /// <summary>Registers an enum type and its (implicit or explicit) member values.</summary>
    /// <param name="en">The enum declaration to register.</param>
    /// <param name="parentQualified">Qualified name of the enclosing type, or null at file scope.</param>
    private void DiscoverEnum(EnumDeclarationSyntax en, string? parentQualified)
    {
        var simple = en.Identifier.Text;
        var qualified = parentQualified is null ? simple : $"{parentQualified}.{simple}";
        var def = new EnumDef
        {
            QualifiedName = qualified, SimpleName = simple, ParentQualifiedName = parentQualified
        };
        var next = 0;
        foreach (var m in en.Members)
        {
            var val = next;
            if (m.EqualsValue?.Value is { } v && TryParseInt(v, out var explicitVal))
            {
                val = explicitVal;
            }

            def.Values.Add((m.Identifier.Text, val));
            next = val + 1;
        }

        _enums[qualified] = def;
        Register(simple, qualified);
    }

    /// <summary>Second pass: extract fields for one message type (resolves types using the full registry).</summary>
    /// <param name="cls">The type declaration to process.</param>
    private void FillFields(TypeDeclarationSyntax cls)
    {
        var simple = cls.Identifier.Text;
        // Resolve this class to its qualified name via the registry (handles nesting/ambiguity by
        // checking the enclosing class chain).
        var qualified = ResolveDeclaredQualifiedName(cls);
        if (qualified is null || !_messages.TryGetValue(qualified, out var msg) || msg.Fields.Count > 0)
        {
            return;
        }

        var decls = CollectFieldDeclarations(cls);

        var deser = cls.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m =>
            m.Identifier.Text == "Deserialize" &&
            m.Modifiers.Any(SyntaxKind.StaticKeyword) &&
            m.ParameterList.Parameters.Count == 3 &&
            m.ParameterList.Parameters[1].Type?.ToString() == simple);
        if (deser is null)
        {
            return;
        }

        var seen = new HashSet<int>();
        foreach (var sw in deser.DescendantNodes().OfType<SwitchStatementSyntax>())
        {
            var fieldNumberMode = IsKeyFieldSwitch(sw);
            foreach (var section in sw.Sections)
            {
                AddFieldsFromSection(section, decls, fieldNumberMode, qualified, msg, seen);
            }
        }

        msg.Fields.Sort((a, b) => a.Number.CompareTo(b.Number));
    }

    /// <summary>Collects the non-static, non-const field declarations of a message as
    /// name -> (csType, repeated, elementType).</summary>
    /// <param name="cls">The type declaration to scan.</param>
    private static Dictionary<string, (string CsType, bool Repeated, string Element)> CollectFieldDeclarations(
        TypeDeclarationSyntax cls)
    {
        var decls = new Dictionary<string, (string CsType, bool Repeated, string Element)>(StringComparer.Ordinal);
        foreach (var fd in cls.Members.OfType<FieldDeclarationSyntax>())
        {
            if (fd.Modifiers.Any(SyntaxKind.StaticKeyword) || fd.Modifiers.Any(SyntaxKind.ConstKeyword))
            {
                continue;
            }

            var typeStr = fd.Declaration.Type.ToString();
            var (repeated, element) = UnwrapList(typeStr);
            foreach (var v in fd.Declaration.Variables)
            {
                decls[v.Identifier.Text] = (typeStr, repeated, element);
            }
        }

        return decls;
    }

    /// <summary>Recovers every field assigned in one <c>Deserialize</c> switch section and appends it to
    /// <paramref name="msg"/>, skipping field numbers already <paramref name="seen"/>.</summary>
    /// <param name="section">The switch section to analyze.</param>
    /// <param name="decls">Declared field types for the enclosing message.</param>
    /// <param name="fieldNumberMode">True when case labels are field numbers rather than wire keys.</param>
    /// <param name="qualified">Qualified name of the message being filled (resolution scope).</param>
    /// <param name="msg">The message to append recovered fields to.</param>
    /// <param name="seen">Field numbers already recorded for this message.</param>
    private void AddFieldsFromSection(
        SwitchSectionSyntax section,
        Dictionary<string, (string CsType, bool Repeated, string Element)> decls,
        bool fieldNumberMode,
        string qualified,
        Message msg,
        HashSet<int> seen)
    {
        var fieldName = FindFieldName(section);
        if (fieldName is null || !decls.TryGetValue(fieldName, out var d))
        {
            return;
        }

        var readMethod = FindReadMethod(section);

        foreach (var label in section.Labels.OfType<CaseSwitchLabelSyntax>())
        {
            if (!TryParseInt(label.Value, out var raw) || raw <= 0)
            {
                continue;
            }

            var number = fieldNumberMode ? raw : raw >> 3;
            if (number <= 0 || !seen.Add(number))
            {
                continue;
            }

            var protoType = ResolveProtoType(d.Repeated ? d.Element : d.CsType, readMethod, qualified);
            msg.Fields.Add(new Field
            {
                Name = fieldName, Number = number, ProtoType = protoType, Repeated = d.Repeated,
            });
        }
    }

    // ---- helpers ----

    private static bool IsProtoMessage(TypeDeclarationSyntax cls) =>
        cls.BaseList?.Types.Any(t => t.Type.ToString().StartsWith("IProto<", StringComparison.Ordinal)) == true;

    private void Register(string simple, string qualified)
    {
        if (!_bySimpleName.TryGetValue(simple, out var list))
        {
            _bySimpleName[simple] = list = [];
        }

        list.Add(qualified);
    }

    private string? ResolveDeclaredQualifiedName(TypeDeclarationSyntax cls)
    {
        // Build the qualified name by walking enclosing class/struct declarations.
        var parts = new List<string>
        {
            cls.Identifier.Text
        };
        for (var p = cls.Parent; p is ClassDeclarationSyntax or StructDeclarationSyntax; p = p.Parent)
        {
            parts.Insert(0, ((TypeDeclarationSyntax)p).Identifier.Text);
        }

        var qualified = string.Join('.', parts);
        return _messages.ContainsKey(qualified) ? qualified : null;
    }

    /// <summary>When the switch expression is <c>key.Field</c>, case labels are field numbers; otherwise they are wire keys.</summary>
    /// <param name="sw">The switch statement to inspect.</param>
    private static bool IsKeyFieldSwitch(SwitchStatementSyntax sw) =>
        sw.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Field" };

    /// <summary>The field assigned in a switch section: prefer assignment / .Add / ref target.</summary>
    /// <param name="section">The switch section to analyze.</param>
    private static string? FindFieldName(SwitchSectionSyntax section)
    {
        // instance.X = ...
        foreach (var asg in section.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (InstanceMember(asg.Left) is { } n)
            {
                return n;
            }
        }

        // instance.X.Add(...)
        foreach (var inv in section.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Add" } add &&
                InstanceMember(add.Expression) is { } n)
            {
                return n;
            }
        }

        // ... ref instance.X ...
        foreach (var arg in section.DescendantNodes().OfType<ArgumentSyntax>())
        {
            if (arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) && InstanceMember(arg.Expression) is { } n)
            {
                return n;
            }
        }

        // fallback: first instance.X anywhere
        foreach (var ma in section.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (InstanceMember(ma) is { } n)
            {
                return n;
            }
        }

        return null;
    }

    private static string? InstanceMember(ExpressionSyntax expr) =>
        expr is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "instance" } } ma
            ? ma.Name.Identifier.Text
            : null;

    private static string? FindReadMethod(SwitchSectionSyntax section)
    {
        foreach (var inv in section.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "ProtocolParser" }
                } ma)
            {
                return ma.Name.Identifier.Text;
            }
        }

        return null;
    }

    private static (bool Repeated, string Element) UnwrapList(string csType)
    {
        if (csType.StartsWith("List<", StringComparison.Ordinal) && csType.EndsWith('>'))
        {
            return (true, csType["List<".Length..^1].Trim());
        }

        return (false, csType);
    }

    private string ResolveProtoType(string csType, string? readMethod, string scopeQualified)
    {
        if (readMethod == "ReadZInt32")
        {
            return "sint32";
        }

        if (readMethod == "ReadZInt64")
        {
            return "sint64";
        }

        var scalar = csType switch
        {
            "bool" => "bool",
            "float" => "float",
            "double" => "double",
            "string" => "string",
            "byte[]" => "bytes",
            "int" => "int32",
            "uint" => "uint32",
            "long" => "int64",
            "ulong" => "uint64",
            _ => null,
        };
        if (scalar is not null)
        {
            return scalar;
        }

        // Known server message/enum reference: resolve the simple name within the current scope.
        var resolved = ResolveTypeName(csType, scopeQualified);
        if (resolved is not null)
        {
            return resolved;
        }

        // Wrapper types that aren't proto messages (e.g. NetworkableId, ArraySegment<byte>): the
        // ProtocolParser read method reveals the real wire scalar.
        var fromRead = readMethod switch
        {
            "ReadBool" => "bool",
            "ReadSingle" => "float",
            "ReadDouble" => "double",
            "ReadString" => "string",
            "ReadBytes" or "ReadPooledBytes" => "bytes",
            "ReadUInt32" => "uint32",
            "ReadUInt64" => "uint64",
            _ => null,
        };
        // External well-known types (Vector2/3/4, Color, Ray) have no ProtocolParser call (serialized
        // via *Serialized helpers) — return the name so the emitter preserves the committed definition.
        return fromRead ?? csType;
    }

    /// <summary>Resolve a simple type name to a qualified one, preferring nested types of the
    /// current scope or its ancestors, then top-level.</summary>
    /// <param name="simple">The unqualified type name to resolve.</param>
    /// <param name="scopeQualified">Qualified name of the message that contains the reference.</param>
    private string? ResolveTypeName(string simple, string scopeQualified)
    {
        if (!_bySimpleName.TryGetValue(simple, out var candidates))
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        // Walk scope ancestors: "A.B.C" -> "A.B" -> "A" -> ""
        var scopes = new List<string>
        {
            scopeQualified
        };
        var idx = scopeQualified.LastIndexOf('.');
        while (idx >= 0)
        {
            scopeQualified = scopeQualified[..idx];
            scopes.Add(scopeQualified);
            idx = scopeQualified.LastIndexOf('.');
        }

        foreach (var s in scopes)
        {
            var nested = $"{s}.{simple}";
            if (candidates.Contains(nested))
            {
                return nested;
            }
        }

        // prefer a top-level candidate (no dot)
        return candidates.FirstOrDefault(c => !c.Contains('.', StringComparison.Ordinal)) ?? candidates[0];
    }

    private static bool TryParseInt(ExpressionSyntax expr, out int value)
    {
        while (true)
        {
            value = 0;
            switch (expr)
            {
                case LiteralExpressionSyntax { Token.Value: not null } lit:
                    var t = lit.Token.Text.TrimEnd('u', 'U', 'l', 'L');
                    return int.TryParse(t, out value);
                case PrefixUnaryExpressionSyntax { OperatorToken.RawKind: (int)SyntaxKind.MinusToken } neg
                    when TryParseInt(neg.Operand, out var inner):
                    value = -inner;
                    return true;
                case CastExpressionSyntax cast:
                    expr = cast.Expression;
                    continue;
                default:
                    return false;
            }
        }
    }
}
