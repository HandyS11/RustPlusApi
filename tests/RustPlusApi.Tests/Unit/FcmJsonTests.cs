using RustPlusApi.Fcm.Data;
using System.Text.Json;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>
/// Guards the v2 §8 JSON cleanup: the bespoke <c>Int32StringConverter</c>/<c>StringToUInt64Converter</c>
/// were replaced by STJ's native <c>JsonNumberHandling</c>. Rust+ encodes these numeric body fields
/// as JSON strings, so reading-from-string (and writing-as-string) must still work.
/// </summary>
public class FcmJsonTests
{
    /// <summary>Mirrors RustPlusFcmSocket's parsing options (Rust+ sends camelCase keys).</summary>
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private const string PairingBodyJson = """
        {
          "id": "11111111-1111-1111-1111-111111111111",
          "ip": "1.2.3.4",
          "port": "28083",
          "name": "Mock Server",
          "playerId": "76561198000000000",
          "playerToken": "123456789",
          "type": "entity",
          "entityType": "1",
          "entityId": "98765"
        }
        """;

    [Fact]
    public void Body_ReadsNumericFieldsEncodedAsStrings()
    {
        var body = JsonSerializer.Deserialize<Body>(PairingBodyJson, Options);

        Assert.NotNull(body);
        Assert.Equal(28083, body!.Port);
        Assert.Equal(76561198000000000UL, body.PlayerId);
        Assert.Equal(1, body.EntityType);
        Assert.Equal(98765, body.EntityId);
    }

    [Fact]
    public void Body_WritesNumericFieldsAsStrings_AndRoundTrips()
    {
        var original = JsonSerializer.Deserialize<Body>(PairingBodyJson, Options)!;

        var json = JsonSerializer.Serialize(original);
        // WriteAsString: the numbers are emitted as quoted strings, not bare numbers.
        Assert.Contains("\"28083\"", json, StringComparison.Ordinal);
        Assert.Contains("\"76561198000000000\"", json, StringComparison.Ordinal);

        var roundTripped = JsonSerializer.Deserialize<Body>(json, Options)!;
        Assert.Equal(original.Port, roundTripped.Port);
        Assert.Equal(original.PlayerId, roundTripped.PlayerId);
        Assert.Equal(original.EntityId, roundTripped.EntityId);
    }

    [Fact]
    public void Body_AcceptsNumericJsonToo()
    {
        // Defensive: also accept real JSON numbers, not only strings.
        const string json = """{ "ip": "1.2.3.4", "port": 28083, "playerId": 42, "name": "n", "playerToken": "t", "type": "server" }""";

        var body = JsonSerializer.Deserialize<Body>(json, Options);

        Assert.Equal(28083, body!.Port);
        Assert.Equal(42UL, body.PlayerId);
    }
}
