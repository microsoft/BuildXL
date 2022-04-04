// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Launcher.Server.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BuildXL.Launcher.Server
{
    public abstract class StartupBase
    {
        public StartupBase(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        protected abstract ServerMode Mode { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public virtual void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers()
                .ConfigureApplicationPartManager(
                apm =>
                {
                    apm.FeatureProviders.Clear();
                    apm.FeatureProviders.Add(new ConfiguredControllerFeatureProvider(this));
                });
        }

        protected enum ServerMode
        {
            /// <summary>
            /// Running as global deployment service used to provide launch manifests to launchers
            /// </summary>
            DeploymentService,

            /// <summary>
            /// Running as deployment proxy
            /// </summary>
            CacheService,
        }

        private class ConfiguredControllerFeatureProvider : ControllerFeatureProvider
        {
            private readonly StartupBase _startupBase;

            public ConfiguredControllerFeatureProvider(StartupBase startupBase)
            {
                _startupBase = startupBase;
            }

            public ServerMode Mode => _startupBase.Mode;

            protected override bool IsController(TypeInfo typeInfo)
            {
                if (!base.IsController(typeInfo))
                {
                    return false;
                }

                // Allow specific controllers depending on the mode
                if (typeInfo == typeof(DeploymentController))
                {
                    return Mode == ServerMode.DeploymentService;
                }
                else if (typeInfo == typeof(DeploymentProxyController) || typeInfo == typeof(ContentCacheController))
                {
                    return Mode == ServerMode.CacheService;
                }

                return true;
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                var sp = endpoints.ServiceProvider;
                var configuration = sp.GetService<DistributedCacheServiceConfiguration>();
                if (configuration != null && configuration.DistributedContentSettings.EnableAspNetCoreGrpc)
                {
                    var cacheServices = endpoints.ServiceProvider.GetRequiredService<ICacheServerServices>();
                    var endpointsWrapper = new GrpcEndpointCollectionWrapper(endpoints);
                    foreach (var endpoint in cacheServices.GrpcEndpoints)
                    {
                        endpoint.MapServices(endpointsWrapper);
                    }
                }

                endpoints.MapControllers();
            });

        }

        private record GrpcEndpointCollectionWrapper(IEndpointRouteBuilder Endpoints) : IGrpcServiceEndpointCollection
        {
            public void MapService<TService>() where TService : class
            {
                Endpoints.MapGrpcService<TService>();
            }
        }
    }
}
