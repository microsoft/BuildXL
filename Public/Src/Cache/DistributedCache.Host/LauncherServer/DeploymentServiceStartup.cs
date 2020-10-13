using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Launcher.Server.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = BuildXL.Cache.ContentStore.Interfaces.Logging.ILogger;

namespace BuildXL.Launcher.Server
{
    public class DeploymentServiceStartup : StartupBase
    {
        public DeploymentServiceStartup(IConfiguration configuration)
            : base(configuration)
        {
        }

        protected override ServerMode Mode => ServerMode.DeploymentService;

        // This method gets called by the runtime. Use this method to add services to the container.
        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            var deploymentService = new DeploymentService(
                deploymentRoot: new AbsolutePath(Configuration["DeploymentRoot"]),
                secretsProviderFactory: keyVaultUri => new KeyVaultSecretsProvider(keyVaultUri),
                clock: SystemClock.Instance,
                uploadConcurrency: Environment.ProcessorCount);

            var consoleLog = new ConsoleLog(useShortLayout: false, printSeverity: true);
            var logger = new Logger(consoleLog);

            services.AddSingleton(deploymentService);
            services.AddSingleton<ILogger>(logger);

            // TODO: Dispose loggers? Does service collection already handle this?
        }
    }
}
