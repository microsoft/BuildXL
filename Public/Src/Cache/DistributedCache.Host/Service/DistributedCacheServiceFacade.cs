// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.Host.Service.Internal;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Top level entry point for creating and running a distributed cache service
    /// </summary>
    public class DistributedCacheServiceFacade
    {
        /// <summary>
        /// Creates and runs a distributed cache service
        /// </summary>
        /// <exception cref="CacheException">thrown when cache startup fails</exception>
        public static async Task RunAsync(DistributedCacheServiceArguments arguments)
        {
            // Switching to another thread.
            await Task.Yield();

            var host = arguments.Host;
            var logger = arguments.Logger;

            var factory = new ContentServerFactory(arguments);

            await host.OnStartingServiceAsync();

            using (var server = factory.Create())
            {
                var context = new Context(logger);

                try
                {
                    var startupResult = await server.StartupAsync(context);
                    if (!startupResult)
                    {
                        throw new CacheException(startupResult.ToString());
                    }

                    host.OnStartedService();

                    logger.Info("Service started");

                    await arguments.Cancellation.WaitForCancellationAsync();

                    logger.Always("Exit event set");
                }
                finally
                {
                    var timeoutInMinutes = arguments?.Configuration?.DistributedContentSettings?.MaxShutdownDurationInMinutes ?? 30;
                    BoolResult result = await ShutdownWithTimeout(context, server, TimeSpan.FromMinutes(timeoutInMinutes));
                    if (!result)
                    {
                        logger.Warning("Failed to shutdown local content server: {0}", result);
                    }

                    host.OnTeardownCompleted();
                }
            }
        }

        private static async Task<BoolResult> ShutdownWithTimeout(Context context, LocalContentServer server, TimeSpan timeout)
        {
            var shutdownTask = server.ShutdownAsync(context);
            if (await Task.WhenAny(shutdownTask, Task.Delay(timeout)) != shutdownTask)
            {
                return new BoolResult($"Server shutdown didn't finished after '{timeout}'.");
            }

            // shutdownTask is done already. Just getting the result out of it.
            return await shutdownTask;
        }
    }
}
