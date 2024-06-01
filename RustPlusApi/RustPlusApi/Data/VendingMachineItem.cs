namespace RustPlusApi.Data
{
    public class VendingMachineItem
    {
        public int Id { get; set; }
        public int StackSize { get; set; }
        public int CurrencyId { get; set; }
        public int CostPerStack { get; set; }
        public int StackSizeAmount { get; set; }
        public bool IsItemBlueprint { get; set; }
        public bool IsCurrencyBlueprint { get; set; }
        public float ItemLife { get; set; }
        public float ItemMaxLife { get; set; }
    }
}
