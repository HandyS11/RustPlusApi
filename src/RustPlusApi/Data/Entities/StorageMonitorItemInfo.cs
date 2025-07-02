namespace RustPlusApi.Data.Entities;

public sealed record StorageMonitorItemInfo
{
    public int Id { get; init; }
    public int? Quantity { get; init; }
    public bool? IsItemBlueprint { get; init; }
}
