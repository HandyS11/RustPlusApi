using RustPlusApi;

namespace RustPlusApi.NetFrameworkSmoke
{
    /// <summary>Exercises the public surface against the netstandard2.0 build from a net48 host.</summary>
    /// <remarks>This is compiled (not run) in CI to guard the multi-target reach.</remarks>
    internal static class Program
    {
        private static void Main()
        {
            using (var rustPlus = new RustPlus("127.0.0.1", 28083, 76561198000000000UL, 123456789))
            {
                System.Console.WriteLine("Constructed RustPlus: connected=" + rustPlus.IsConnected());
            }
        }
    }
}
