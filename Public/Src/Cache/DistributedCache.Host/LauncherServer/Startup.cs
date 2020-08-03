using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = BuildXL.Cache.ContentStore.Interfaces.Logging.ILogger;

namespace BuildXL.Launcher.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();

            var deploymentService = new DeploymentService(
                deploymentRoot: new AbsolutePath(Configuration["DeploymentRoot"]),
                secretsProvider: new KeyVaultSecretsProvider(Configuration["KeyVaultUri"]),
                clock: SystemClock.Instance,
                uploadConcurrency: Environment.ProcessorCount);

            var consoleLog = new ConsoleLog(useShortLayout: false, printSeverity: true);
            var logger = new Logger(consoleLog);

            services.AddSingleton(deploymentService);
            services.AddSingleton<ILogger>(logger);

            // TODO: Dispose loggers? Does service collection already handle this?
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            //app.UseHttpsRedirection();

            app.UseRouting();

            //app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
