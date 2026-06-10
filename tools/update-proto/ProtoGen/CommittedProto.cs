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
        var state = new ParseState();
        foreach (var rawLine in File.ReadLines(protoPath))
        {
            result.ProcessLine(rawLine, state);
        }

        return result;
    }

    /// <summary>Transient state threaded through the line-by-line parse: the nesting stack, the current
    /// top-level declaration name, and the raw lines accumulated for it.</summary>
    private sealed class ParseState
    {
        public readonly List<string> Stack = [];
        public string? TopLevelName;
        public List<string> Block = [];
    }

    /// <summary>Folds a single source line into <paramref name="state"/>: tracks declaration open/close
    /// and records field labels.</summary>
    /// <param name="rawLine">The untrimmed source line.</param>
    /// <param name="state">The running parse state to update.</param>
    private void ProcessLine(string rawLine, ParseState state)
    {
        if (state.TopLevelName is not null)
        {
            state.Block.Add(rawLine);
        }

        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
        {
            return;
        }

        if (TryHandleDeclOpen(line, rawLine, state) || TryHandleDeclClose(line, state))
        {
            return;
        }

        HandleField(line, state);
    }

    /// <summary>Handles a <c>message</c>/<c>enum</c> opening brace, pushing it onto the nesting stack.</summary>
    /// <param name="line">The trimmed source line.</param>
    /// <param name="rawLine">The untrimmed source line (kept to seed the top-level block).</param>
    /// <param name="state">The running parse state to update.</param>
    /// <returns><c>true</c> if the line opened a declaration.</returns>
    private bool TryHandleDeclOpen(string line, string rawLine, ParseState state)
    {
        var open = DeclOpen().Match(line);
        if (!open.Success)
        {
            return false;
        }

        if (state.Stack.Count == 0)
        {
            state.TopLevelName = open.Groups[2].Value;
            state.Block = [rawLine];
        }

        state.Stack.Add(open.Groups[2].Value);
        DeclOrder.Add(string.Join('.', state.Stack));
        return true;
    }

    /// <summary>Handles a closing brace, popping the stack and capturing the raw block when a top-level
    /// declaration ends.</summary>
    /// <param name="line">The trimmed source line.</param>
    /// <param name="state">The running parse state to update.</param>
    /// <returns><c>true</c> if the line closed a declaration.</returns>
    private bool TryHandleDeclClose(string line, ParseState state)
    {
        if (!line.StartsWith('}'))
        {
            return false;
        }

        if (state.Stack.Count > 0)
        {
            state.Stack.RemoveAt(state.Stack.Count - 1);
        }

        if (state.Stack.Count == 0 && state.TopLevelName is not null)
        {
            RawTopLevelBlocks[state.TopLevelName] = string.Join('\n', state.Block).TrimEnd();
            state.TopLevelName = null;
        }

        return true;
    }

    /// <summary>Records the label (required/optional/repeated) for a field line within the current scope.</summary>
    /// <param name="line">The trimmed source line.</param>
    /// <param name="state">The running parse state to update.</param>
    private void HandleField(string line, ParseState state)
    {
        var field = FieldLine().Match(line);
        if (field.Success && state.Stack.Count > 0)
        {
            var qualified = string.Join('.', state.Stack);
            _labels[$"{qualified}#{field.Groups[2].Value}"] = field.Groups[1].Value;
        }
    }

    [GeneratedRegex(@"^(message|enum)\s+([A-Za-z0-9_]+)\s*\{$")]
    private static partial Regex DeclOpen();

    // e.g. "required uint32 width = 1;"  /  "repeated AppMap.Monument monuments = 5;"
    [GeneratedRegex(@"^(required|optional|repeated)\s+[A-Za-z0-9_.]+\s+[A-Za-z0-9_]+\s*=\s*(\d+)\s*;")]
    private static partial Regex FieldLine();
}
