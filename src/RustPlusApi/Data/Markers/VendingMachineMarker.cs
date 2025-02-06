namespace RustPlusApi.Data.Markers;

public class VendingMachineMarker : Marker
{
    public string? Name { get; set; }
    public bool? IsOutOfStock { get; set; }
    public IEnumerable<VendingMachineItem>? VendingMachineItems { get; set; }
}
