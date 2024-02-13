// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

namespace BuildXL.Cache.ContentStore.Grpc
{
    /// <summary>
    /// gRPC.NET server options.
    /// </summary>
    /// <remarks>
    /// This is essentially a copy of Grpc.AspNetCore.Server.GrpcServiceOptions.
    /// </remarks>
    public record GrpcDotNetServerOptions
    {
        public int? MaxSendMessageSize { get; init; } = int.MaxValue;

        public int? MaxReceiveMessageSize { get; init; } = int.MaxValue;

        public bool EnableDetailedErrors { get; init; } = false;

        public bool? IgnoreUnknownServices { get; init; }

        public string? ResponseCompressionAlgorithm { get; init; }

        public System.IO.Compression.CompressionLevel? ResponseCompressionLevel { get; init; }

        public static GrpcDotNetServerOptions Default { get; } = new GrpcDotNetServerOptions();
    }
}
