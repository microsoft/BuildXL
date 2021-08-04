// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Utils;
using CLAP;
using Context = BuildXL.Cache.ContentStore.Interfaces.Tracing.Context;

namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     Run the RemoveFromTracker verb.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Remove hashes registered at the local location from the content tracker")]
        internal void RemoveFromTracker(
            [DefaultValue((uint)7089), Description("GRPC port")] uint grpcPort)
        {
            Initialize();

            // We need to initialize this here because there's no codepath otherwise.
            GrpcEnvironment.Initialize();

            var context = new Context(_logger);
            var retryPolicy = RetryPolicyFactory.GetLinearPolicy(ex => ex is ClientCanRetryException, (int)_retryCount, TimeSpan.FromSeconds(_retryIntervalSeconds));

            _logger.Debug("Begin repair handling...");

            // This action is synchronous to make sure the calling application doesn't exit before the method returns.
            using (var rpcClient = new GrpcRepairClient(grpcPort))
            {
                var removeFromTrackerResult = retryPolicy.ExecuteAsync(() => rpcClient.RemoveFromTrackerAsync(context), _cancellationToken).Result;
                if (!removeFromTrackerResult.Succeeded)
                {
                    throw new CacheException(removeFromTrackerResult.ErrorMessage);
                }
                else
                {
                    _logger.Debug($"Repair handling succeeded.");
                }

                var shutdownResult = rpcClient.ShutdownAsync(context).Result;
                if (!shutdownResult.Succeeded)
                {
                    throw new CacheException(shutdownResult.ErrorMessage);
                }
            }
        }
    }
}
