using System.Drawing;

using RustPlusApi.Data;
using RustPlusApi.Data.Events;

using RustPlusContracts;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

public static class AppTeamChatToModel
{
    public static TeamChatInfo ToTeamChatInfo(this AppTeamChat appTeamChat)
    {
        return new TeamChatInfo
        {
            Messages = appTeamChat.Messages.ToTeamMessages()
        };
    }

    public static TeamMessage ToTeamMessage(this AppTeamMessage appTeamMessage)
    {
        return new TeamMessage
        {
            SteamId = appTeamMessage.SteamId,
            Name = appTeamMessage.Name,
            Message = appTeamMessage.Message,
            Color = ColorTranslator.FromHtml(appTeamMessage.Color),
            Time = DateTimeOffset.FromUnixTimeSeconds(appTeamMessage.Time).UtcDateTime,
        };
    }

    public static IEnumerable<TeamMessage> ToTeamMessages(this IEnumerable<AppTeamMessage> appTeamMessages)
    {
        return appTeamMessages.Select(ToTeamMessage);
    }

    public static TeamMessageEventArg ToTeamMessageEvent(this AppTeamMessage appTeamMessage)
    {
        return new TeamMessageEventArg
        {
            SteamId = appTeamMessage.SteamId,
            Name = appTeamMessage.Name,
            Message = appTeamMessage.Message,
            Color = ColorTranslator.FromHtml(appTeamMessage.Color),
            Time = DateTimeOffset.FromUnixTimeSeconds(appTeamMessage.Time).UtcDateTime,
        };
    }
}
