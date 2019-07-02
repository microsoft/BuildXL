// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// A straightforward implementation of <see cref="IClientConfig"/> using public properties.
    /// </summary>
    public sealed class ClientConfig : IClientConfig
    {
        /// <nodoc />
        public const int DefaultMaxConnectRetries = 5;

        /// <nodoc />
        public const int DefaultConnectRetryDelayMillis = 100;

        /// <nodoc />
        public ClientConfig(int? numRetries = null, int? retryDelayMillis = null)
        {
            MaxConnectRetries = numRetries ?? DefaultMaxConnectRetries;
            ConnectRetryDelay = TimeSpan.FromMilliseconds(retryDelayMillis ?? DefaultConnectRetryDelayMillis);
        }

        /// <inheritdoc />
        public ILogger Logger { get; set; } = null;

        /// <inheritdoc />
        public TimeSpan ConnectRetryDelay { get; set; } = TimeSpan.FromMilliseconds(DefaultConnectRetryDelayMillis);

        /// <inheritdoc />
        public int MaxConnectRetries { get; set; } = DefaultMaxConnectRetries;

        /// <summary>
        /// Read and return an instance of this config from a binary reader.
        /// </summary>
        public static IClientConfig Deserialize(BinaryReader reader)
        {
            return new ClientConfig
            {
                ConnectRetryDelay = TimeSpan.FromTicks(reader.ReadInt64()),
                MaxConnectRetries = reader.ReadInt32(),
            };
        }

        /// <summary>
        /// Serialize an instance of IClientConfig to a binary writer.
        /// </summary>
        public static void Serialize(IClientConfig config, BinaryWriter writer)
        {
            writer.Write(config.ConnectRetryDelay.Ticks);
            writer.Write(config.MaxConnectRetries);
        }
    }
}
