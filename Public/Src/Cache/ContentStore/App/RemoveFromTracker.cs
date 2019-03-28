// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using CLAP;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using Microsoft.Practices.TransientFaultHandling;

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

            var context = new Context(_logger);
            var retryPolicy = new RetryPolicy(
                new TransientErrorDetectionStrategy(),
                new FixedInterval("RetryInterval", (int)_retryCount, TimeSpan.FromSeconds(_retryIntervalSeconds), false));

            _logger.Debug("Begin repair handling...");

            // This action is synchronous to make sure the calling application doesn't exit before the method returns.
            using (var rpcClient = new GrpcRepairClient(grpcPort))
            {
                var removeFromTrackerResult = retryPolicy.ExecuteAsync(() => rpcClient.RemoveFromTrackerAsync(context)).Result;
                if (!removeFromTrackerResult.Succeeded)
                {
                    throw new CacheException(removeFromTrackerResult.ErrorMessage);
                }
                else
                {
                    _logger.Debug($"Repair handling succeeded. Removed {removeFromTrackerResult.Data} hashes from the content tracker.");
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
