// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Service;
using CLAP;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     Run the service check verb.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Check if CAS service is running")]
        internal void ServiceRunning([DefaultValue(0), Description("Number of seconds to wait for running service")] int waitSeconds)
        {
            Initialize();

            _logger.Debug("Begin running service detection...");

            var waitMs = waitSeconds * 1000;
            var running = LocalContentServer.EnsureRunning(new Context(_logger), _scenario, waitMs);

            if (running)
            {
                _logger.Debug("Running service was detected");
            }
            else
            {
                throw new CacheException($"Service still not running after waiting {waitSeconds} seconds");
            }
        }
    }
}
