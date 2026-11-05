using System;
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>Clone is the single place that knows every option; this guards it copying all of them.</summary>
public class RustPlusSocketOptionsTests
{
    [Fact]
    public void Clone_CopiesEveryPublicProperty()
    {
        var options = new RustPlusSocketOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            KeepAliveInterval = TimeSpan.FromSeconds(2),
            TeardownTimeout = TimeSpan.FromSeconds(3),
            ReceiveBufferSize = 1234,
            MaxMessageSize = 5678,
        };

        var clone = options.Clone();

        Assert.NotSame(options, clone);
        // Reflection sweep: a property added later but forgotten in Clone() fails here as long as
        // it is also initialised above to a non-default value — extend both together.
        foreach (var property in typeof(RustPlusSocketOptions).GetProperties())
        {
            Assert.Equal(property.GetValue(options), property.GetValue(clone));
        }
    }

    [Fact]
    public void MaxMessageSize_DefaultsToABoundThatCannotDropARealMapMessage()
    {
        // Real map images are a few megabytes; the default has to sit well above that so it never
        // drops legitimate traffic, while still bounding a hostile server's reassembly buffer.
        Assert.Equal(64 * 1024 * 1024, new RustPlusSocketOptions().MaxMessageSize);
    }
}
