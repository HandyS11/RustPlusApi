using RustPlusApi.Extensions;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>Locks the entity-info mappers, including the type-guard throws and the
/// storage-monitor item projection.</summary>
public class EntityInfoMapperTests
{
    private static AppEntityInfo Entity(AppEntityType type, AppEntityPayload payload) =>
        new() { Type = type, Payload = payload };

    [Fact]
    public void ToSmartSwitchInfo_MapsActiveState()
    {
        var info = Entity(AppEntityType.Switch, new AppEntityPayload { Value = true }).ToSmartSwitchInfo();
        Assert.True(info.IsActive);
    }

    [Fact]
    public void ToSmartSwitchInfo_WrongType_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            Entity(AppEntityType.Alarm, new AppEntityPayload()).ToSmartSwitchInfo());

    [Fact]
    public void ToAlarmInfo_MapsActiveState()
    {
        var info = Entity(AppEntityType.Alarm, new AppEntityPayload { Value = true }).ToAlarmInfo();
        Assert.True(info.IsActive);
    }

    [Fact]
    public void ToAlarmInfo_WrongType_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            Entity(AppEntityType.Switch, new AppEntityPayload()).ToAlarmInfo());

    [Fact]
    public void ToStorageMonitorInfo_MapsCapacityItemsAndExpiry()
    {
        var payload = new AppEntityPayload
        {
            Capacity = 48,
            HasProtection = true,
            ProtectionExpiry = 1_700_000_000,
            Items = { new AppEntityPayload.Item { ItemId = 1, Quantity = 5, ItemIsBlueprint = true } }
        };

        var info = Entity(AppEntityType.StorageMonitor, payload).ToStorageMonitorInfo();

        Assert.Equal(48, info.Capacity);
        Assert.True(info.HasProtection);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime, info.ProtectionExpiry);
        var item = Assert.Single(info.Items!);
        Assert.Equal(1, item.Id);
        Assert.Equal(5, item.Quantity);
        Assert.True(item.IsItemBlueprint);
    }

    [Fact]
    public void ToStorageMonitorInfo_WrongType_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            Entity(AppEntityType.Switch, new AppEntityPayload()).ToStorageMonitorInfo());
}
