// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Result of <see cref="RedisContentLocationStore.GetRedisInfoAsync"/>.
    /// </summary>
    internal class RedisInfoStats
    {
        /// <nodoc />
        public IReadOnlyList<(string serverId, RedisInfo info)> Info { get; }

        /// <nodoc />
        public RedisInfoStats(List<(string serverId, RedisInfo info)> info)
        {
            Info = info;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Join(Environment.NewLine, Info.Select(i => $"Server Id: {i.serverId}, Info: {i.info}"));
        }

        /// <summary>
        /// Prints the first result with only key information.
        /// </summary>
        public string ToDisplayString()
        {
            if (Info.Count == 0)
            {
                return "<Empty>";
            }

            var info = Info[0].info;
            return string.Join(
                ", ",
                $"Keys count: {info.KeyCount}",
                $"Expirable keys: {info.ExpirableKeyCount}",
                $"CPU: {info.UsedCpuAveragePersentage}%",
                $"Used memory: {info.UsedMemoryHuman}",
                $"Used memory RSS: {info.UsedMemoryRssHuman}"
            );
        }
    }
}
