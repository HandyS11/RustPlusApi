using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using Xunit;

namespace RustPlusApi.Fcm.UnitTests;

public class NotificationHierarchyTests
{
    [Fact]
    public void NotificationOfT_DerivesNotificationBase_AndCarriesServerIdAndPersistentId()
    {
        var serverId = Guid.NewGuid();
        NotificationBase n = new Notification<int?>
        {
            ServerId = serverId,
            PersistentId = "pid-1",
            PlayerId = 7,
            PlayerToken = 9,
            Data = 42
        };

        Assert.Equal(serverId, n.ServerId);
        Assert.Equal("pid-1", n.PersistentId);
        var typed = Assert.IsType<Notification<int?>>(n);
        Assert.Equal(7ul, typed.PlayerId);
        Assert.Equal(9, typed.PlayerToken);
        Assert.Equal(42, typed.Data);
    }
}
