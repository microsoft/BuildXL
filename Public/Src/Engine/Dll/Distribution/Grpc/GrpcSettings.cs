// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Configuration;
using Grpc.Core;

namespace BuildXL.Engine.Distribution.Grpc
{
    internal static class GrpcSettings
    {
        public const int MaxRetry = 3;

        /// <summary>
        /// Maximum time for a Grpc call (both orchestrator->worker and worker->orchestrator)
        /// </summary>
        /// <remarks>
        /// Default: 5 minutes
        /// </remarks>
        public static TimeSpan CallTimeout => EngineEnvironmentSettings.DistributionConnectTimeout;

        /// <summary>
        /// Maximum time to wait for the orchestrator to connect to a worker.
        /// </summary>
        /// <remarks>
        /// Default: 75 minutes
        /// </remarks>
        public static TimeSpan WorkerAttachTimeout => EngineEnvironmentSettings.WorkerAttachTimeout;

        public static void ParseHeader(Metadata header, out string sender, out DistributedInvocationId senderInvocationId, out string traceId)
        {
            sender = string.Empty;
            string relatedActivityId = string.Empty;
            string environment = string.Empty;

            traceId = string.Empty;

            foreach (var kvp in header)
            {
                if (kvp.Key == GrpcMetadata.TraceIdKey)
                {
                    traceId = new Guid(kvp.ValueBytes).ToString();
                }
                else if (kvp.Key == GrpcMetadata.RelatedActivityIdKey)
                {
                    relatedActivityId = kvp.Value;
                }
                else if (kvp.Key == GrpcMetadata.EnvironmentKey)
                {
                    environment = kvp.Value;
                }
                else if (kvp.Key == GrpcMetadata.SenderKey)
                {
                    sender = kvp.Value;
                }
            }

            senderInvocationId = new DistributedInvocationId(relatedActivityId, environment);
        }
    }
}