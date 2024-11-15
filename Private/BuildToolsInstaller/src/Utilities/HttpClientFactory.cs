// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace BuildToolsInstaller.Utilities
{
    internal class HttpClientFactory
    {
        private const string ResilienceClientName = "DefaultResilienceClient";
        private const string ResiliencePipelineName = "CustomResiliencePipeline";
        private static readonly ServiceProvider s_serviceProvider;
        private static readonly ServiceCollection s_services;

        static HttpClientFactory()
        {
            s_services = new ServiceCollection();

            // Add a resilience strategy for transient errors
            // We choose some arbitrary, conservative defaults for the timeouts,
            // considering the intended usage of the HttpClient for this tool.
            s_services.AddHttpClient(ResilienceClientName).AddResilienceHandler(
                ResiliencePipelineName,
                static builder =>
                {
                    // Overall timeout for the request: 2 minutes gives
                    // enough time to retry 3 times with a 30 second timeout per attempt,
                    // plus some slack for backoffs.
                    builder.AddTimeout(TimeSpan.FromMinutes(2));

                    // The retry strategy handles transient HTTP errors by default
                    builder.AddRetry(new HttpRetryStrategyOptions
                    {
                        BackoffType = DelayBackoffType.Exponential,
                        MaxRetryAttempts = 3,
                        UseJitter = true
                    });
                    // Timeout for each attempt
                    builder.AddTimeout(TimeSpan.FromSeconds(30));
                });

            s_serviceProvider = s_services.BuildServiceProvider();
        }

        /// <summary>
        /// Creates a HttpClient with a default resilience strategy
        /// </summary>
        /// <returns></returns>
        public static HttpClient Create()
        {
            var httpClientFactory = s_serviceProvider.GetRequiredService<IHttpClientFactory>();
            return httpClientFactory.CreateClient(ResilienceClientName);
        }
    }
}
