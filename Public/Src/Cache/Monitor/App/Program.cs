using System.Diagnostics;

namespace BuildXL.Cache.Monitor.App
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Debugger.Launch();
            using var monitor = new Monitor(new Monitor.Configuration());
            monitor.Run().Wait();
        }
    }
}
