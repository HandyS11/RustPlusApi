using RustPlusApi.Data.Entities;

namespace RustPlusApi.Data.Events;

/// <summary>Event argument raised for every <c>EntityChanged</c> broadcast, exposing the full raw
/// payload before any device-type heuristic. The broadcast carries no entity type; consumers that
/// know their paired entity ids should route on <see cref="Id"/>.</summary>
public sealed record EntityChangedEventArg
{
    /// <summary>Container capacity, or <see langword="null"/> when omitted (storage payloads only).</summary>
    public int? Capacity { get; init; }

    /// <summary>Tool-cupboard protection flag, or <see langword="null"/> when omitted.</summary>
    public bool? HasProtection { get; init; }

    /// <summary>Entity ID of the entity that changed.</summary>
    public ulong Id { get; init; }

    /// <summary>Items in the container; empty for binary-state payloads and for storage broadcasts
    /// that carry no contents snapshot.</summary>
    public IEnumerable<StorageMonitorItemInfo> Items { get; init; } = [];

    /// <summary>UTC time when the tool-cupboard protection expires, or <see langword="null"/> when omitted.</summary>
    public DateTime? ProtectionExpiry { get; init; }

    /// <summary>Binary state (on / triggered), or <see langword="null"/> when omitted.</summary>
    public bool? Value { get; init; }
}
