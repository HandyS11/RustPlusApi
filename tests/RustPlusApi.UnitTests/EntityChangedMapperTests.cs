using RustPlusApi.Extensions;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>Locks <see cref="EntityChangedToModel.ToEntityChangedEvent"/>: full payload passthrough
/// with absent optional fields mapping to <see langword="null"/>.</summary>
public class EntityChangedMapperTests
{
    [Fact]
    public void ToEntityChangedEvent_MapsFullPayload()
    {
        var changed = new AppEntityChanged
        {
            EntityId = 42,
            Payload = new AppEntityPayload
            {
                Value = true,
                Capacity = 24,
                HasProtection = true,
                ProtectionExpiry = 1_700_000_000,
                Items =
                {
                    new AppEntityPayload.Item
                    {
                        ItemId = 1, Quantity = 5, ItemIsBlueprint = false
                    }
                }
            }
        };

        var arg = changed.ToEntityChangedEvent();

        Assert.Equal(42u, arg.Id);
        Assert.True(arg.Value);
        Assert.Equal(24, arg.Capacity);
        Assert.True(arg.HasProtection);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime, arg.ProtectionExpiry);
        var item = Assert.Single(arg.Items);
        Assert.Equal(1, item.Id);
    }

    [Fact]
    public void ToEntityChangedEvent_UnsetOptionals_AreNull()
    {
        var arg = new AppEntityChanged
        {
            EntityId = 7, Payload = new AppEntityPayload()
        }.ToEntityChangedEvent();

        Assert.Equal(7u, arg.Id);
        Assert.Null(arg.Value);
        Assert.Null(arg.Capacity);
        Assert.Null(arg.HasProtection);
        Assert.Null(arg.ProtectionExpiry);
        Assert.Empty(arg.Items);
    }
}
