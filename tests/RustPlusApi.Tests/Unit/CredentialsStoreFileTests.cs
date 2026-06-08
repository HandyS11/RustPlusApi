using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>Covers the file persistence paths and the invalid-JSON throw of CredentialsStore.</summary>
public class CredentialsStoreFileTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsViaDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"creds-{Guid.NewGuid():N}.json");
        try
        {
            var credentials = new Credentials
            {
                Gcm = new Gcm { AndroidId = 1, SecurityToken = 2 },
                Fcm = new FcmToken { Token = "t" },
                ExpoPushToken = "ExponentPushToken[a]"
            };
            CredentialsStore.Save(path, credentials);

            var loaded = CredentialsStore.Load(path);

            Assert.Equal(1UL, loaded.Gcm.AndroidId);
            Assert.Equal("t", loaded.Fcm!.Token);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Deserialize_NullLiteral_Throws() =>
        Assert.Throws<InvalidOperationException>(() => CredentialsStore.Deserialize("null"));

    /// <summary>Asserts that <see cref="CredentialsStore.Serialize"/> uses WriteIndented=true,
    /// which means the JSON output contains newlines — kills the WriteIndented boolean mutant.</summary>
    [Fact]
    public void Serialize_ProducesIndentedJson()
    {
        var credentials = new Credentials { Gcm = new Gcm { AndroidId = 7, SecurityToken = 3 } };

        var json = CredentialsStore.Serialize(credentials);

        // WriteIndented = true → output contains a newline; if mutated to false → single-line, no '\n'.
        Assert.Contains('\n', json);
    }

    /// <summary>Asserts the exact exception message thrown when JSON deserializes to null —
    /// kills the String mutation that replaces the message with "".</summary>
    [Fact]
    public void Deserialize_NullLiteral_ExceptionMessageIsNonEmpty()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CredentialsStore.Deserialize("null"));
        Assert.False(string.IsNullOrEmpty(ex.Message));
        Assert.Contains("deserialize", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
