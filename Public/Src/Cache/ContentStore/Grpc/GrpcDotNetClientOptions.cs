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
        /// <nodoc />
        public static GrpcDotNetClientOptions Default = new GrpcDotNetClientOptions();
    }
}
