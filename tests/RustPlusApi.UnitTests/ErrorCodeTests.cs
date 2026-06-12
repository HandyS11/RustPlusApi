using RustPlusApi.Data;
using RustPlusApi.Utils;
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>The raw server error identifier is surfaced as a machine-readable
/// <see cref="RustPlusErrorCode"/> alongside the untouched <see cref="ErrorMessage.Message"/>.</summary>
public class ErrorCodeTests
{
    [Theory]
    [InlineData("server_error", RustPlusErrorCode.ServerError)]
    [InlineData("banned", RustPlusErrorCode.Banned)]
    [InlineData("rate_limit", RustPlusErrorCode.RateLimit)]
    [InlineData("not_found", RustPlusErrorCode.NotFound)]
    [InlineData("wrong_type", RustPlusErrorCode.WrongType)]
    [InlineData("no_team", RustPlusErrorCode.NoTeam)]
    [InlineData("no_clan", RustPlusErrorCode.NoClan)]
    [InlineData("no_map", RustPlusErrorCode.NoMap)]
    [InlineData("access_denied", RustPlusErrorCode.AccessDenied)]
    [InlineData("message_not_sent", RustPlusErrorCode.MessageNotSent)]
    [InlineData("too_many_subscribers", RustPlusErrorCode.TooManySubscribers)]
    [InlineData("not_enabled", RustPlusErrorCode.NotEnabled)]
    [InlineData("no_player", RustPlusErrorCode.NoPlayer)]
    [InlineData("unknown-error", RustPlusErrorCode.Unknown)]
    [InlineData("some_future_identifier", RustPlusErrorCode.Unknown)]
    public void BuildAckOutput_MapsServerIdentifierToCode(string identifier, RustPlusErrorCode expected)
    {
        var response = ResponseHelper.BuildAckOutput(false, identifier);

        Assert.NotNull(response.Error);
        Assert.Equal(expected, response.Error!.Code);
        Assert.Equal(identifier, response.Error.Message); // the raw string stays available
    }

    [Fact]
    public void BuildGenericOutput_MapsServerIdentifierToCode()
    {
        var response = ResponseHelper.BuildGenericOutput<string>(false, default!, "not_found");

        Assert.Equal(RustPlusErrorCode.NotFound, response.Error!.Code);
    }

    [Fact]
    public void BuildAckOutput_Success_HasNoError()
    {
        Assert.Null(ResponseHelper.BuildAckOutput(true).Error);
    }
}
