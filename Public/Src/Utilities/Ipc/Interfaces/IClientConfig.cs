// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Linq;

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// Common <see cref="IClient"/> configuration parameters.
    /// </summary>
    public interface IClientConfig
    {
        /// <summary>
        /// Logger to use.  May be null to indicate no logging.
        /// </summary>
        ILogger Logger { get; }

        /// <summary>
        /// Maximum number of retries to establish a connection.
        /// </summary>
        int MaxConnectRetries { get; }

        /// <summary>
        /// Delay between two consecutive retries to establish a connection.
        /// </summary>
        TimeSpan ConnectRetryDelay { get; }
    }

    /// <summary>
    /// Extension methods for <see cref="IClient"/>.
    /// </summary>
    public static class ClientConfigExtensions
    {
        /// <summary>
        /// Return a JSON representation of a given config instance.
        /// </summary>
        public static string ToJson(this IClientConfig config)
        {
            var properties = string.Join(", ", new[]
            {
                Tuple.Create(nameof(IClientConfig.MaxConnectRetries), config.MaxConnectRetries.ToString(CultureInfo.InvariantCulture)),
                Tuple.Create(nameof(IClientConfig.ConnectRetryDelay), ((int)config.ConnectRetryDelay.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)),
            }.Select(t => '"' + t.Item1 + "\": " + t.Item2));
            return "{" + properties + "}";
        }
    }
}
