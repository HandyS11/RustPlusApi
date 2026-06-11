using RustPlusApi.Data;
using RustPlusApi.Data.Events;
using RustPlusApi.Utils;
using RustPlusContracts;
using System.Drawing;

// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf team-chat messages to model types.</summary>
public static class AppTeamChatToModel
{
    /// <summary>Maps an <see cref="AppTeamChat"/> to a <see cref="TeamChatInfo"/>.</summary>
    /// <param name="appTeamChat">The protobuf team chat response.</param>
    public static TeamChatInfo ToTeamChatInfo(this AppTeamChat appTeamChat)
    {
        return new TeamChatInfo
        {
            Messages = appTeamChat.Messages.ToTeamMessages()
        };
    }

    /// <summary>Maps a single <see cref="AppTeamMessage"/> to a <see cref="TeamMessage"/>.</summary>
    /// <param name="appTeamMessage">The protobuf team message.</param>
    public static TeamMessage ToTeamMessage(this AppTeamMessage appTeamMessage)
    {
        return new TeamMessage
        {
            SteamId = appTeamMessage.SteamId,
            Name = appTeamMessage.Name,
            Message = appTeamMessage.Message,
            Color = HtmlColorParser.FromHtml(appTeamMessage.Color),
            Time = DateTimeOffset.FromUnixTimeSeconds(appTeamMessage.Time).UtcDateTime,
        };
    }

    /// <summary>Maps a sequence of <see cref="AppTeamMessage"/> to <see cref="TeamMessage"/> instances.</summary>
    /// <param name="appTeamMessages">The protobuf team messages to map.</param>
    public static IEnumerable<TeamMessage> ToTeamMessages(this IEnumerable<AppTeamMessage> appTeamMessages)
    {
        return appTeamMessages.Select(ToTeamMessage);
    }

    /// <summary>Maps a single <see cref="AppTeamMessage"/> broadcast to a <see cref="TeamMessageEventArg"/>.</summary>
    /// <param name="appTeamMessage">The protobuf team message broadcast.</param>
    public static TeamMessageEventArg ToTeamMessageEvent(this AppTeamMessage appTeamMessage)
    {
        return new TeamMessageEventArg
        {
            SteamId = appTeamMessage.SteamId,
            Name = appTeamMessage.Name,
            Message = appTeamMessage.Message,
            Color = HtmlColorParser.FromHtml(appTeamMessage.Color),
            Time = DateTimeOffset.FromUnixTimeSeconds(appTeamMessage.Time).UtcDateTime,
        };
    }
}
