using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Events;

public class TeamChatChanges(IRustPlus rustPlus)
{
    public void Setup()
    {
        // Subscribe to the TeamChatMessageReceived event
        // This event is triggered when a team chat message is received
        // IF and ONLY IF the team chat has been enabled before (EnableTeamChat)
        rustPlus.OnTeamChatReceived += (_, message) =>
        {
            DisplayUtilities.DisplayEvent("TeamChatChanges", message);
        };
    }
}