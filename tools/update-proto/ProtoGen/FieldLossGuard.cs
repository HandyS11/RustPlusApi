namespace ProtoGen;

/// <summary>A committed field that the regenerated model no longer contains.</summary>
/// <param name="Message">Qualified message name the field belongs to.</param>
/// <param name="Number">Proto field number that went missing.</param>
/// <param name="Label">The label the field carried in the committed proto.</param>
internal sealed record FieldLoss(string Message, int Number, string Label);

/// <summary>
/// Guards against silent field loss.
/// </summary>
/// <remarks>
/// <para>
/// ProtoGen recovers wire information by pattern-matching decompiled C#. When it meets a dispatch
/// shape it does not recognise it produces a message with no fields and no error — which renders as
/// a clean, plausible-looking field removal in the diff. Issue #120 is exactly that: 13 committed
/// fields vanished at once and the drift report presented them as genuine server changes.
/// </para>
/// <para>
/// Real removals do happen, so this is a stop rather than a ban: the operator confirms one with
/// <c>--allow-field-removal</c>. The default is to refuse, because a wrong proto silently breaks
/// serialization at runtime whereas a failed refresh only costs a re-run.
/// </para>
/// </remarks>
internal static class FieldLossGuard
{
    /// <summary>Finds every committed field missing from the regenerated model.</summary>
    /// <param name="committed">The committed proto, used as the baseline.</param>
    /// <param name="server">The model recovered from the decompiled server.</param>
    /// <param name="scopeMessages">Messages the emitter regenerated authoritatively. Anything outside
    /// this set is copied verbatim from the committed proto and so cannot lose a field.</param>
    /// <returns>The losses, in committed declaration order; empty when nothing was lost.</returns>
    public static List<FieldLoss> Check(
        CommittedProto committed,
        ServerParser server,
        IReadOnlySet<string> scopeMessages)
    {
        var losses = new List<FieldLoss>();

        foreach (var field in committed.Fields)
        {
            // Out of scope -> emitted verbatim from the committed proto, nothing to lose.
            if (!scopeMessages.Contains(field.Message))
            {
                continue;
            }

            // The message dropping out of the server model entirely is a different signal (a removed
            // message), and the emitter falls back to the committed block for it.
            if (!server.Messages.TryGetValue(field.Message, out var message))
            {
                continue;
            }

            if (!message.Fields.Exists(f => f.Number == field.Number))
            {
                losses.Add(new FieldLoss(field.Message, field.Number, field.Label));
            }
        }

        return losses;
    }

    /// <summary>Renders the losses as an operator-facing error report.</summary>
    /// <param name="losses">The losses reported by <see cref="Check" />.</param>
    /// <returns>A multi-line message naming every lost field and how to proceed.</returns>
    public static string Describe(IReadOnlyList<FieldLoss> losses)
    {
        var lines = new List<string>
        {
            $"error: {losses.Count} committed field(s) are missing from the regenerated proto:",
        };
        lines.AddRange(losses.Select(l => $"  - {l.Message} #{l.Number} ({l.Label})"));
        lines.Add(string.Empty);
        lines.Add("This usually means ProtoGen failed to recognise the decompiled dispatch shape");
        lines.Add("rather than that the server dropped these fields — a whole message emptying out,");
        lines.Add("or many messages losing a field at once, is the signature of a parser gap.");
        lines.Add("Check the decompiled Deserialize for one of the messages above before believing it.");
        lines.Add(string.Empty);
        lines.Add("If the removal really is genuine, re-run with --allow-field-removal.");
        return string.Join(Environment.NewLine, lines);
    }
}
