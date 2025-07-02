namespace RustPlusApi.Data.Notes;

public sealed record PlayerNote : Note
{
    public NoteIcons Icon { get; init; }
    public NoteColors Color { get; init; }
    public string? Text { get; init; }
}
