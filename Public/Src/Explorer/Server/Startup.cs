// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BuildXL.Explorer.Server
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
            services.AddMemoryCache();

            var builder = services.AddMvcCore(options =>
            {
                options.Filters.Add(new ExplorerExceptionFilter());
            });

            builder.AddJsonOptions(options =>
            {
#if DEBUG
                options.SerializerSettings.Formatting = Formatting.Indented;
                options.SerializerSettings.Converters.Add(new IsoDateTimeConverter());
                options.SerializerSettings.Converters.Add(new IsoTimeSpanConverter());
#endif
            });

            builder.AddAuthorization();

            // Order added affects options setup order

            // Default framework order
            builder.AddFormatterMappings();
            builder.AddViews();
            builder.AddDataAnnotations(); 
            builder.AddJsonFormatters();

            builder.AddCors();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }
            app.UseDeveloperExceptionPage();
            app.UseMvc();
        }
    }
}
