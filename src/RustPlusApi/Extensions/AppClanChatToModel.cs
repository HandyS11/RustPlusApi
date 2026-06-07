using RustPlusApi.Data.Clans;
using RustPlusApi.Data.Events;

using RustPlusContracts;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf clan-chat messages to clan model types.</summary>
public static class AppClanChatToModel
{
    /// <summary>Maps an <see cref="AppClanChat"/> to a <see cref="ClanChatInfo"/>.</summary>
    /// <param name="appClanChat">The protobuf clan chat response.</param>
    public static ClanChatInfo ToClanChatInfo(this AppClanChat appClanChat)
    {
        return new ClanChatInfo
        {
            Messages = appClanChat.Messages.ToClanMessages()
        };
    }

    /// <summary>Maps a single <see cref="AppClanMessage"/> to a <see cref="ClanMessage"/>.</summary>
    /// <param name="appClanMessage">The protobuf clan message.</param>
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

    /// <summary>Maps a sequence of <see cref="AppClanMessage"/> to <see cref="ClanMessage"/> instances.</summary>
    /// <param name="appClanMessages">The protobuf clan messages to map.</param>
    public static IEnumerable<ClanMessage> ToClanMessages(this IEnumerable<AppClanMessage> appClanMessages)
    {
        return appClanMessages.Select(ToClanMessage);
    }

    /// <summary>Maps an <see cref="AppNewClanMessage"/> broadcast to a <see cref="ClanMessageEventArg"/>.</summary>
    /// <param name="appNewClanMessage">The protobuf new clan message broadcast.</param>
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
