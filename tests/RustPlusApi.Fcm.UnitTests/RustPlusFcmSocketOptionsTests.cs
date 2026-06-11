using System;
using Xunit;

namespace RustPlusApi.Fcm.UnitTests;

/// <summary>Clone is the single place that knows every option; this guards it copying all of them.</summary>
public class RustPlusFcmSocketOptionsTests
{
    [Fact]
    public void Clone_CopiesEveryPublicProperty()
    {
        var options = new RustPlusFcmSocketOptions
        {
            HeartbeatInterval = TimeSpan.FromMinutes(1),
            InactivityTimeout = TimeSpan.FromMinutes(2),
        };

        var clone = options.Clone();

        Assert.NotSame(options, clone);
        // Reflection sweep: a property added later but forgotten in Clone() fails here as long as
        // it is also initialised above to a non-default value — extend both together.
        foreach (var property in typeof(RustPlusFcmSocketOptions).GetProperties())
        {
            Assert.Equal(property.GetValue(options), property.GetValue(clone));
        }
    }
}
