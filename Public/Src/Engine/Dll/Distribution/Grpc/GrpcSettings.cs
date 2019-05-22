// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities.Configuration;
using Grpc.Core;

namespace BuildXL.Engine.Distribution.Grpc
{
    internal static class GrpcSettings
    {
        public const string TraceIdKey = "traceid-bin";
        public const string BuildIdKey = "buildid";
        public const string SenderKey = "sender";

        public const int MaxRetry = 4;

        /// <summary>
        /// Maximum time for a Grpc call (both master->worker and worker->master)
        /// </summary>
        /// <remarks>
        /// Default: 5 minutes
        /// </remarks>
        public static TimeSpan CallTimeout => EngineEnvironmentSettings.DistributionConnectTimeout;

        /// <summary>
        /// Maximum time to wait for the master to connect to a worker.
        /// </summary>
        /// <remarks>
        /// Default: 60 minutes
        /// </remarks>
        public static TimeSpan InactiveTimeout => EngineEnvironmentSettings.DistributionInactiveTimeout;

        /// <summary>
        /// The number of threads to be created for Grpc.
        /// </summary>
        /// <remarks>
        /// Default: 70
        /// </remarks>
        public static int ThreadPoolSize => EngineEnvironmentSettings.GrpcThreadPoolSize;

        /// <summary>
        /// Maximum time to wait for the master to connect to a worker.
        /// </summary>
        /// <remarks>
        /// Default: false
        /// </remarks>
        public static bool HandlerInliningEnabled => EngineEnvironmentSettings.GrpcHandlerInliningEnabled;

        public static void ParseHeader(Metadata header, out string sender, out string senderBuildId, out string traceId)
        {
            sender = string.Empty;
            senderBuildId = string.Empty;
            traceId = string.Empty;

            foreach (var kvp in header)
            {
                if (kvp.Key == GrpcSettings.TraceIdKey)
                {
                    traceId = new Guid(kvp.ValueBytes).ToString();
                }
                else if (kvp.Key == GrpcSettings.BuildIdKey)
                {
                    senderBuildId = kvp.Value;
                }
                else if (kvp.Key == GrpcSettings.SenderKey)
                {
                    sender = kvp.Value;
                }
            }
        }
    }
}