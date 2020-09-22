// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Grpc
{
    /// <nodoc />
    public static class GrpcConstants
    {
        /// <nodoc />
        public static readonly int DefaultBufferSizeBytes = 8192;

        /// <nodoc />
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        /// <nodoc />
        public static readonly int DefaultGrpcPort = 7089;
    }
}
