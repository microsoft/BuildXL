// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Engine.Distribution.Grpc
{
    internal static class GrpcConstants
    {
        public const string TraceIdKey = "traceid-bin";
        public const string BuildIdKey = "buildid";
        public const int MaxRetry = 3;

        /// <summary>
        /// Maximum time for a Grpc call (both master->worker and worker->master)
        /// </summary>
        /// <remarks>
        /// Default: 3 minutes
        /// </remarks>
        public static TimeSpan CallTimeout => EngineEnvironmentSettings.DistributionConnectTimeout;

        public static TimeSpan InactiveTimeout => EngineEnvironmentSettings.DistributionInactiveTimeout;
    }
}