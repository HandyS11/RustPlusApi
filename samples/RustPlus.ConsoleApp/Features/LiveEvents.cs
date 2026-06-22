using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Data.Events;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class LiveEvents(IRustPlus rustPlus)
{
    public Task RunAsync()
    {
        void OnSmartDevice(object? _, SmartDeviceEventArg e) =>
            DisplayUtilities.DisplayEvent("SmartDeviceTriggered", e);

        void OnStorageMonitor(object? _, StorageMonitorEventArg e) =>
            DisplayUtilities.DisplayEvent("StorageMonitorTriggered", e);

        void OnTeamChat(object? _, TeamMessageEventArg e) => DisplayUtilities.DisplayEvent("TeamChatReceived", e);
        void OnClanChat(object? _, ClanMessageEventArg e) => DisplayUtilities.DisplayEvent("ClanChatReceived", e);
        void OnClanChanged(object? _, ClanChangedEventArg e) => DisplayUtilities.DisplayEvent("ClanChanged", e);

        rustPlus.OnSmartDeviceTriggered += OnSmartDevice;
        rustPlus.OnStorageMonitorTriggered += OnStorageMonitor;
        rustPlus.OnTeamChatReceived += OnTeamChat;
        rustPlus.OnClanChatReceived += OnClanChat;
        rustPlus.OnClanChanged += OnClanChanged;

        Console.Clear();
        Console.WriteLine("Listening for live events (smart switch, storage monitor, team & clan chat,");
        Console.WriteLine("clan changes). Press any key to stop...\n");
        Console.ReadKey(intercept: true);

        rustPlus.OnSmartDeviceTriggered -= OnSmartDevice;
        rustPlus.OnStorageMonitorTriggered -= OnStorageMonitor;
        rustPlus.OnTeamChatReceived -= OnTeamChat;
        rustPlus.OnClanChatReceived -= OnClanChat;
        rustPlus.OnClanChanged -= OnClanChanged;

        return Task.CompletedTask;
    }
}
