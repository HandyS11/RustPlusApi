namespace RustPlus.ConsoleApp.Utils;

/// <summary>A single selectable menu entry: a label and the action to run when chosen.</summary>
/// <param name="Label">Display text shown in the menu.</param>
/// <param name="Action">Async delegate invoked when the entry is selected.</param>
internal sealed record MenuItem(string Label, Func<Task> Action);

/// <summary>
/// Renders a numbered menu and dispatches the chosen <see cref="MenuItem"/>. Sub-menus nest by
/// having an item's action call <see cref="RunAsync"/> again. Option "0" returns to the caller.
/// </summary>
internal static class Menu
{
    public static async Task RunAsync(string title, params MenuItem[] items)
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine($"{title}:");
            Console.WriteLine("0. Back");
            for (var i = 0; i < items.Length; i++)
            {
                Console.WriteLine($"{i + 1}. {items[i].Label}");
            }

            Console.Write("\nPlease enter your choice: ");
            var choice = Console.ReadLine();

            if (choice == "0")
            {
                return;
            }

            if (int.TryParse(choice, out var n) && n >= 1 && n <= items.Length)
            {
                try
                {
                    await items[n - 1].Action();
                }
                catch (Exception ex)
                {
                    // Keep the menu alive if a feature throws (e.g. a transient network fault)
                    // rather than tearing down the whole session.
                    Console.WriteLine($"Something went wrong: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("Invalid choice, please try again.");
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(intercept: true);
        }
    }
}
