// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public GrpcCopyClientKey(string host, int grpcPort)
        {
            Host = host;
            GrpcPort = grpcPort;
        }

        /// <nodoc />
        public bool Equals(GrpcCopyClientKey other)
        {
            return string.Equals(Host, other.Host, StringComparison.InvariantCultureIgnoreCase)
                && GrpcPort == other.GrpcPort;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is GrpcCopyClientKey key && Equals(key);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Host, GrpcPort).GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"({Host}, {GrpcPort})";
        }
    }
}
