// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    internal struct GrpcCopyClientKey : IEquatable<GrpcCopyClientKey>
    {
        public GrpcCopyClientKey(string host, int grpcPort, bool useCompression)
        {
            Host = host;
            GrpcPort = grpcPort;
            UseCompression = useCompression;
        }

        public string Host;
        public int GrpcPort;
        public bool UseCompression;

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
            return BuildXL.Utilities.HashCodeHelper.Combine(Host.GetHashCode(), GrpcPort, UseCompression ? 1 : 0);
        }
    }
}
