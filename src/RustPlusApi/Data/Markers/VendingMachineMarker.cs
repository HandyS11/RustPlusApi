namespace RustPlusApi.Data.Markers;

/// <summary>Map marker for a vending machine, including its current sell orders.</summary>
public sealed record VendingMachineMarker : Marker
{
    /// <summary>Name of the vending machine (set by its owner).</summary>
    public string? Name { get; init; }

    /// <summary><see langword="true"/> if the vending machine has no stock remaining.</summary>
    public bool? IsOutOfStock { get; init; }

    /// <summary>Active sell orders in the vending machine.</summary>
    public IEnumerable<VendingMachineItem>? VendingMachineItems { get; init; }
}
