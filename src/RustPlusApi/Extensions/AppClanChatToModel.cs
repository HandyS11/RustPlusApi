using RustPlusApi.Data.Clans;
using RustPlusApi.Data.Events;

using RustPlusContracts;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

public static class AppClanChatToModel
{
    public static ClanChatInfo ToClanChatInfo(this AppClanChat appClanChat)
    {
        return new ClanChatInfo
        {
            Messages = appClanChat.Messages.ToClanMessages()
        };
    }

    public static ClanMessage ToClanMessage(this AppClanMessage appClanMessage)
    {
        return new ClanMessage
        {
            SteamId = appClanMessage.SteamId,
            Name = appClanMessage.Name,
            Message = appClanMessage.Message,
            Time = DateTimeOffset.FromUnixTimeSeconds(appClanMessage.Time).UtcDateTime
        };
    }

    public static IEnumerable<ClanMessage> ToClanMessages(this IEnumerable<AppClanMessage> appClanMessages)
    {
        return appClanMessages.Select(ToClanMessage);
    }

    public static ClanMessageEventArg ToClanMessageEvent(this AppNewClanMessage appNewClanMessage)
    {
        return new ClanMessageEventArg
        {
            ClanId = appNewClanMessage.ClanId,
            SteamId = appNewClanMessage.Message.SteamId,
            Name = appNewClanMessage.Message.Name,
            Message = appNewClanMessage.Message.Message,
            Time = DateTimeOffset.FromUnixTimeSeconds(appNewClanMessage.Message.Time).UtcDateTime
        };
    }
}
