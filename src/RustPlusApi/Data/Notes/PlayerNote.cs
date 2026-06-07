namespace RustPlusApi.Data.Notes;

/// <summary>A player-placed map note (type 1 in the protocol).</summary>
public sealed record PlayerNote : Note
{
    /// <summary>Icon displayed on the note.</summary>
    public NoteIcons Icon { get; init; }

    /// <summary>Colour of the note icon.</summary>
    public NoteColors Color { get; init; }

    /// <summary>Optional label text shown on the note.</summary>
    public string? Text { get; init; }
}
