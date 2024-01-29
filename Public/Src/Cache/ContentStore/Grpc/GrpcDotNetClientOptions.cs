// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

#nullable enable

namespace BuildXL.Cache.ContentStore.Grpc
{
    /// <summary>
    /// Configuration options used by Grpc.Net clients.
    /// </summary>
    public record GrpcDotNetClientOptions
    {
        /// <summary>
        /// If not null, specifies min verbosity level from grpc layer that will be sent to the cache telemetry.
        /// </summary>
        /// <remarks>
        /// This value should match the values from Microsoft.Extensions.Logging.LogLevel enum.
        /// </remarks>
        public int? MinLogLevelVerbosity { get; init; }

        public static GrpcDotNetClientOptions Default { get; } = new GrpcDotNetClientOptions() { MinLogLevelVerbosity = GrpcConstants.DefaultGrpcDotNetMinLogLevel }; // Using error logging severity by default.
    }

}
