namespace BuildXL.Cache.Monitor.App
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            using var monitor = new Monitor(new Monitor.Configuration());
            monitor.Run().Wait();
        }
    }
}
