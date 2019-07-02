// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Key to lookup <see cref="GrpcCopyClient"/> in <see cref="GrpcCopyClientCache"/>.
    /// </summary>
    public readonly struct GrpcCopyClientKey : IEquatable<GrpcCopyClientKey>
    {
        /// <nodoc />
        public string Host { get; }

        /// <nodoc />
        public int GrpcPort { get; }

        /// <nodoc />
        public bool UseCompression { get; }

        /// <nodoc />
        public GrpcCopyClientKey(string host, int grpcPort, bool useCompression)
        {
            Host = host;
            GrpcPort = grpcPort;
            UseCompression = useCompression;
        }

        /// <nodoc />
        public bool Equals(GrpcCopyClientKey other)
        {
            return string.Equals(Host, other.Host, StringComparison.InvariantCultureIgnoreCase)
                && GrpcPort == other.GrpcPort
                && UseCompression == other.UseCompression;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is GrpcCopyClientKey key && Equals(key);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Host, GrpcPort, UseCompression).GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"({Host}, {GrpcPort}, {(UseCompression ? "compressed" : "uncompressed")})";
        }
    }
}
