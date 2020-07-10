// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using StackExchange.Redis;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    internal static class RedisConfigurationExtensions
    {
        /// <summary>
        /// Gets an endpoint used for connecting to Redis instance.
        /// </summary>
        public static RedisEndpoint GetRedisEndpoint(this ConfigurationOptions options)
        {
            return new RedisEndpoint(options);
        }
    }

    internal readonly struct RedisEndpoint : IEquatable<RedisEndpoint>
    {
        private readonly string _endpoints;

        /// <nodoc />
        public RedisEndpoint(ConfigurationOptions options)
        {
            _endpoints = string.Join(", ", options.EndPoints);
        }

        /// <inheritdoc />
        public bool Equals(RedisEndpoint other) => _endpoints == other._endpoints;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is RedisEndpoint other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => (_endpoints != null ? _endpoints.GetHashCode() : 0);

        /// <inheritdoc />
        public override string ToString() => _endpoints;
    }
}
