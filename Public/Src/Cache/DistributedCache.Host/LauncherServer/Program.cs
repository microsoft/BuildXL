using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace BuildXL.Launcher.Server
{
    public class DeploymentProgram
    {
        public static Task Main(string[] args)
        {
            return RunAsync(args, CancellationToken.None);
        }

        public static Task RunAsync(string[] args, CancellationToken token)
        {
            if (args.ElementAtOrDefault(0)?.Equals("cacheService", StringComparison.OrdinalIgnoreCase) == true)
            {
                args = args.Skip(1).ToArray();
                return CacheServiceStartup.RunWithCacheServiceAsync(args, token);
            }

            return CreateHostBuilder(args).Build().RunAsync(token);
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<DeploymentServiceStartup>();
                });
    }
}
