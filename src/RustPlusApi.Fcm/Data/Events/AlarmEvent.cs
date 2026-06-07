namespace RustPlusApi.Fcm.Data.Events;

/// <summary>Describes a Rust+ smart alarm that has been triggered.</summary>
public sealed record AlarmEvent
{
    /// <summary>The alarm title configured in the Rust+ app.</summary>
    public string Title { get; set; } = null!;

    /// <summary>The alarm message configured in the Rust+ app.</summary>
    public string Message { get; set; } = null!;
}
