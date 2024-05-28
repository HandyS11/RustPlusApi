namespace RustPlusApi.Fcm.Data.Events
{
    public class EntityEventArg
    {
        public int EntityType { get; set; }
        public int EntityId { get; set; }
        public string EntityName { get; set; } = null!;
    }
}
