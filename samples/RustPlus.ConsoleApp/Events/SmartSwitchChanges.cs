using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Events;

public class SmartSwitchChanges(IRustPlus rustPlus)
{
    public void Setup()
    {
        // Subscribe to the SmartSwitchTriggered event
        // This event is triggered when a smart switch is activated or deactivated
        // IF and ONLY IF the smartSwitch state has been checked before (GetSmartSwitchInfo)
        rustPlus.OnSmartSwitchTriggered += (_, message) =>
        {
            DisplayUtilities.DisplayEvent("SmartSwitchChanges", message);
        };
    }
}