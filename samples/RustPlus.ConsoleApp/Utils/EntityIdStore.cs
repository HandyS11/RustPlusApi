namespace RustPlus.ConsoleApp.Utils;

/// <summary>
/// Remembers the last id entered for each entity kind during the session so the user can press
/// Enter to reuse it instead of retyping raw ids. In-memory only (not persisted to disk).
/// </summary>
internal sealed class EntityIdStore
{
    private readonly Dictionary<string, string> _lastUsed = new();

    /// <summary>Prompts for a string id, reusing the remembered value on empty input.</summary>
    /// <param name="kind">Human-readable label for the id (e.g. "smartSwitchId").</param>
    public string GetString(string kind)
    {
        while (true)
        {
            var remembered = _lastUsed.GetValueOrDefault(kind);
            Console.Write(remembered is null
                ? $"\nType the {kind}: "
                : $"\nType the {kind} [last: {remembered}] (Enter to reuse): ");

            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                if (remembered is not null)
                {
                    return remembered;
                }
                Console.WriteLine("A value is required, please try again.");
                continue;
            }

            var value = input.Trim();
            _lastUsed[kind] = value;
            return value;
        }
    }

    /// <summary>Prompts for a ulong id, reusing the remembered value on empty input.</summary>
    /// <param name="kind">Human-readable label for the id (e.g. "smartSwitchId").</param>
    public ulong GetUlong(string kind)
    {
        while (true)
        {
            var value = GetString(kind);
            if (ulong.TryParse(value, out var id))
            {
                return id;
            }
            Console.WriteLine("Invalid input, please try again.");
            _lastUsed.Remove(kind); // don't keep an unparseable value as "last"
        }
    }
}
