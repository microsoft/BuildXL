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
        public const int DefaultGrpcPort = 7089;

        /// <nodoc />
        public const int DefaultEncryptedGrpcPort = 7090;

        /// <nodoc />
        public const int DefaultEphemeralGrpcPort = 7091;

        /// <nodoc />
        public const int DefaultEphemeralLeaderGrpcPort = 7092;

        /// <nodoc />
        public const int DefaultEphemeralEncryptedGrpcPort = 7093;

        /// <nodoc />
        public const int DefaultEphemeralLeaderEncryptedGrpcPort = 7094;

        /// <nodoc />
        public const string LocalHost = "localhost";
    }
}
