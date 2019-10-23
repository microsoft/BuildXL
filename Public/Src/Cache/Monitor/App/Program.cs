using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.Monitor.App.Rules;

namespace BuildXL.Cache.Monitor.App
{
    internal class Program
    {
        private static Task Main(string[] args)
        {
            var logger = new Logger(new ILog[] {
                new ConsoleLog(printSeverity: true),
            });
            
            var scheduler = new Scheduler(new SchedulerSettings(), logger, SystemClock.Instance);

            AddRules(scheduler);

            return scheduler.RunAsync();
        }

        private static void AddRules(Scheduler scheduler)
        {
            scheduler.Add(new PrintRule(), TimeSpan.FromSeconds(1));
        }
    }
}
