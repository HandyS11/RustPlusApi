namespace RustPlusApi.Data.Markers;

public sealed record VendingMachineMarker : Marker
{
    public string? Name { get; init; }
    public bool? IsOutOfStock { get; init; }
    public IEnumerable<VendingMachineItem>? VendingMachineItems { get; init; }
}
