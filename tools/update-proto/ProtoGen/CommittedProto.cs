using System.Text.RegularExpressions;

namespace ProtoGen;

/// <summary>
/// Light line-based reader of the committed <c>RustPlusContracts.proto</c>. We need the bits the
/// server binary does NOT carry: the <c>required</c>/<c>optional</c> labels (per message + field
/// number) and the declaration order/nesting of messages and enums, so the regenerated file keeps
/// the same layout and the diff shows only real wire changes.
/// </summary>
internal sealed partial class CommittedProto
{
    /// <summary>Maps "QualifiedName#FieldNumber" to the field label (required/optional/repeated).</summary>
    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);

    /// <summary>Qualified names of every message/enum declaration, in document order.</summary>
    public List<string> DeclOrder { get; } = [];

    /// <summary>Raw source text of each top-level declaration, keyed by simple name. Used to preserve
    /// hand-maintained "well-known" types (Vector2/3/4, Color, Ray) that the server serializes via
    /// helpers rather than as proto messages.</summary>
    public Dictionary<string, string> RawTopLevelBlocks { get; } = new(StringComparer.Ordinal);

    public string LabelFor(string qualified, int number, bool repeated)
    {
        if (_labels.TryGetValue($"{qualified}#{number}", out var l))
        {
            return l;
        }

        // New (server-only) scalar/message fields default to optional (proto2-safe).
        return repeated ? "repeated" : "optional";
    }

    public static CommittedProto Parse(string protoPath)
    {
        var result = new CommittedProto();
        var stack = new List<string>();
        string? topLevelName = null;
        var block = new List<string>();
        foreach (var rawLine in File.ReadLines(protoPath))
        {
            if (topLevelName is not null)
            {
                block.Add(rawLine);
            }

            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var open = DeclOpen().Match(line);
            if (open.Success)
            {
                if (stack.Count == 0)
                {
                    topLevelName = open.Groups[2].Value;
                    block = [rawLine];
                }
                stack.Add(open.Groups[2].Value);
                result.DeclOrder.Add(string.Join('.', stack));
                continue;
            }
            if (line.StartsWith('}'))
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                if (stack.Count == 0 && topLevelName is not null)
                {
                    result.RawTopLevelBlocks[topLevelName] = string.Join('\n', block).TrimEnd();
                    topLevelName = null;
                }
                continue;
            }

            var field = FieldLine().Match(line);
            if (field.Success && stack.Count > 0)
            {
                var qualified = string.Join('.', stack);
                result._labels[$"{qualified}#{field.Groups[2].Value}"] = field.Groups[1].Value;
            }
        }
        return result;
    }

    [GeneratedRegex(@"^(message|enum)\s+([A-Za-z0-9_]+)\s*\{$")]
    private static partial Regex DeclOpen();

    // e.g. "required uint32 width = 1;"  /  "repeated AppMap.Monument monuments = 5;"
    [GeneratedRegex(@"^(required|optional|repeated)\s+[A-Za-z0-9_.]+\s+[A-Za-z0-9_]+\s*=\s*(\d+)\s*;")]
    private static partial Regex FieldLine();
}
