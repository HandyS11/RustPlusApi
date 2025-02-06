namespace RustPlusApi.Data.Notes;

public class PlayerNote : Note
{
    public NoteIcons Icon { get; set; }
    public NoteColors Color { get; set; }
    public string? Text { get; set; }
}
