namespace RustPlusApi.Data;

public sealed record VendingMachineItem
{
    public int Id { get; init; }
    public int StackSize { get; init; }
    public int CurrencyId { get; init; }
    public int CostPerStack { get; init; }
    public int StackSizeAmount { get; init; }
    public bool IsItemBlueprint { get; init; }
    public bool IsCurrencyBlueprint { get; init; }
    public float ItemLife { get; init; }
    public float ItemMaxLife { get; init; }
}
