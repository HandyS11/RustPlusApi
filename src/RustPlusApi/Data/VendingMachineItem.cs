namespace RustPlusApi.Data;

/// <summary>A single sell order listed in a vending machine.</summary>
public sealed record VendingMachineItem
{
    /// <summary>Rust item definition ID of the item being sold.</summary>
    public int Id { get; init; }

    /// <summary>Quantity of the item offered per transaction.</summary>
    public int StackSize { get; init; }

    /// <summary>Rust item definition ID of the accepted currency.</summary>
    public int CurrencyId { get; init; }

    /// <summary>Amount of currency required per transaction.</summary>
    public int CostPerStack { get; init; }

    /// <summary>Total quantity of this item available in the machine.</summary>
    public int StackSizeAmount { get; init; }

    /// <summary><see langword="true"/> if the sold item is a blueprint.</summary>
    public bool IsItemBlueprint { get; init; }

    /// <summary><see langword="true"/> if the currency item is a blueprint.</summary>
    public bool IsCurrencyBlueprint { get; init; }

    /// <summary>Current condition of the item (0–1 scale).</summary>
    public float ItemLife { get; init; }

    /// <summary>Maximum condition value of the item (0–1 scale).</summary>
    public float ItemMaxLife { get; init; }
}
