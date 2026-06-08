using System.Text;
using System.Text.RegularExpressions;

namespace ProtoGen;

/// <summary>
/// Emits a normalized <c>.proto</c> from the parsed server model, preserving committed conventions
/// (snake_case names, required/optional labels, declaration order, nesting, header). The server is
/// authoritative for field names/types/numbers/repeated and for new fields/messages/enums.
/// </summary>
internal sealed partial class Emitter
{
    private readonly ServerParser _server;
    private readonly CommittedProto _committed;
    private readonly HashSet<string> _scopeMessages = new(StringComparer.Ordinal);
    private readonly HashSet<string> _scopeEnums = new(StringComparer.Ordinal);
    /// <summary>Committed top-level types that the authoritative closure does not cover: external well-known
    /// types (Vector2/3/4) and unreachable/vestigial types (Half3, Color, Ray, ClanActionResult).
    /// Emitted verbatim from the committed proto — no authoritative claim is made about them.</summary>
    private readonly HashSet<string> _preserved = new(StringComparer.Ordinal);

    private Emitter(ServerParser server, CommittedProto committed)
    {
        _server = server;
        _committed = committed;
    }

    public static string Emit(ServerParser server, CommittedProto committed, IEnumerable<string> roots)
    {
        var e = new Emitter(server, committed);
        e.ComputeScope(roots);
        return e.Render();
    }

    /// <summary>Authoritative scope = transitive closure of messages/enums reachable from the roots.
    /// Everything else the committed proto declares at top level is preserved verbatim.</summary>
    /// <param name="roots">Root message names (qualified) to start the closure from.</param>
    private void ComputeScope(IEnumerable<string> roots)
    {
        var queue = new Queue<string>();
        foreach (var r in roots.Where(r => _server.Messages.ContainsKey(r) && _scopeMessages.Add(r)))
            queue.Enqueue(r);

        while (queue.Count > 0)
        {
            var msg = _server.Messages[queue.Dequeue()];
            foreach (var protoType in msg.Fields.Select(f => f.ProtoType))
            {
                if (_server.Messages.ContainsKey(protoType))
                {
                    if (_scopeMessages.Add(protoType))
                        queue.Enqueue(protoType);
                }
                else if (_server.Enums.ContainsKey(protoType))
                {
                    _scopeEnums.Add(protoType);
                }
            }
        }

        // Preserve any committed top-level declaration the closure does not authoritatively cover.
        foreach (var name in _committed.DeclOrder
            .Where(n => !n.Contains('.', StringComparison.Ordinal) &&
                        !_scopeMessages.Contains(n) && !_scopeEnums.Contains(n) &&
                        _committed.RawTopLevelBlocks.ContainsKey(n)))
        {
            _preserved.Add(name);
        }
    }

    private string Render()
    {
        var sb = new StringBuilder()
            .AppendLine("syntax = \"proto2\";")
            .AppendLine("package RustPlusContracts;")
            .AppendLine();
        RenderScope(sb, parentQualified: null, indent: 0);
        return sb.ToString();
    }

    /// <summary>Emit the messages/enums directly under <paramref name="parentQualified"/> (null =
    /// file scope), in committed declaration order, with any server-only additions appended.</summary>
    /// <param name="sb">The string builder to write into.</param>
    /// <param name="parentQualified">Qualified name of the enclosing message, or null for file scope.</param>
    /// <param name="indent">Tab indentation depth.</param>
    private void RenderScope(StringBuilder sb, string? parentQualified, int indent)
    {
        bool first = true;
        foreach (var qn in OrderedChildren(parentQualified))
        {
            if (!first || parentQualified is not null)
                sb.AppendLine();
            first = false;

            // Preserved types win even when the server also defines them (e.g. an unreferenced
            // Half3 struct): we emit the committed definition verbatim rather than the server's.
            if (_preserved.Contains(qn))
                sb.AppendLine(_committed.RawTopLevelBlocks[qn]);
            else if (_server.Messages.TryGetValue(qn, out var m))
                RenderMessage(sb, m, indent);
            else if (_server.Enums.TryGetValue(qn, out var e))
                RenderEnum(sb, e, indent);
        }
    }

    private List<string> OrderedChildren(string? parentQualified)
    {
        bool InScope(string qn) =>
            (_server.Messages.ContainsKey(qn) && _scopeMessages.Contains(qn)) ||
            (_server.Enums.ContainsKey(qn) && _scopeEnums.Contains(qn)) ||
            _preserved.Contains(qn);
        bool IsDirectChild(string qn) => ParentOf(qn) == parentQualified;

        var ordered = _committed.DeclOrder.Where(qn => IsDirectChild(qn) && InScope(qn)).ToList();
        var known = new HashSet<string>(ordered, StringComparer.Ordinal);

        // Append server-only declarations (new messages/enums) deterministically.
        var extra = _scopeMessages.Concat(_scopeEnums)
            .Where(qn => IsDirectChild(qn) && !known.Contains(qn))
            .OrderBy(qn => qn, StringComparer.Ordinal);
        ordered.AddRange(extra);
        return ordered;
    }

    private void RenderMessage(StringBuilder sb, Message msg, int indent)
    {
        var pad = new string('\t', indent);
        sb.Append(pad).Append("message ").Append(msg.SimpleName).AppendLine(" {");

        foreach (var f in msg.Fields)
        {
            var label = _committed.LabelFor(msg.QualifiedName, f.Number, f.Repeated);
            sb.Append(pad).Append('\t')
              .Append(label).Append(' ')
              .Append(EmitTypeName(f.ProtoType, msg.QualifiedName)).Append(' ')
              .Append(ToSnakeCase(f.Name)).Append(" = ").Append(f.Number).AppendLine(";");
        }

        RenderScope(sb, msg.QualifiedName, indent + 1);
        sb.Append(pad).AppendLine("}");
    }

    private static void RenderEnum(StringBuilder sb, EnumDef en, int indent)
    {
        var pad = new string('\t', indent);
        sb.Append(pad).Append("enum ").Append(en.SimpleName).AppendLine(" {");
        foreach (var (name, value) in en.Values)
            sb.Append(pad).Append('\t').Append(name).Append(" = ").Append(value).AppendLine(";");
        sb.Append(pad).AppendLine("}");
    }

    /// <summary>How a type reference is written in a field, matching the committed proto's style:
    /// top-level types by simple name; a nested type referenced from a sibling by its simple name;
    /// a nested type referenced from its own parent by the qualified "Parent.Nested" name.</summary>
    /// <param name="protoType">The fully-qualified proto type to emit.</param>
    /// <param name="currentQualified">Qualified name of the message that contains the field.</param>
    private static string EmitTypeName(string protoType, string currentQualified)
    {
        var parent = ParentOf(protoType);
        if (parent is null)
            return protoType; // scalar or top-level type

        if (ParentOf(currentQualified) == parent && currentQualified != parent)
            return protoType[(parent.Length + 1)..]; // sibling reference -> simple name
        return protoType;                            // parent->child or distant -> qualified
    }

    private static string? ParentOf(string qualified)
    {
        var i = qualified.LastIndexOf('.');
        return i < 0 ? null : qualified[..i];
    }

    public static string ToSnakeCase(string camel)
    {
        var s = LowerUpper().Replace(camel, "$1_$2");
        s = AcronymWord().Replace(s, "$1_$2");
        return s.ToLowerInvariant();
    }

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex LowerUpper();

    [GeneratedRegex(@"([A-Z]+)([A-Z][a-z])")]
    private static partial Regex AcronymWord();
}
