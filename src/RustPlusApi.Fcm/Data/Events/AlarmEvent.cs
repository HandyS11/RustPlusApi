namespace RustPlusApi.Fcm.Data.Events;

/// <summary>Describes a Rust+ smart alarm that has been triggered.</summary>
public sealed record AlarmEvent
{
    /// <summary>The ID of the Rust+ server the alarm was triggered on.</summary>
    public Guid ServerId { get; init; }

    /// <summary>The alarm title configured in the Rust+ app.</summary>
    public string Title { get; init; } = null!;

    /// <summary>The alarm message configured in the Rust+ app.</summary>
    public string Message { get; init; } = null!;
}
