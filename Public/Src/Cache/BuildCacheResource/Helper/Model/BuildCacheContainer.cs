// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json.Serialization;

namespace BuildXL.Cache.BuildCacheResource.Model
{
    /// <nodoc/>
    public enum BuildCacheContainerType
    {
        /// <nodoc/>
        Content,
        /// <nodoc/>
        Metadata,
        /// <nodoc/>
        Checkpoint
    }

    /// <summary>
    /// A container under a cache resource shard
    /// </summary>
    public record BuildCacheContainer
    {
        /// <nodoc/>
        public required string Name { get; init; }

        /// <summary>
        /// A container can host content or metadata. If the GC service is involved,
        /// a 'checkpoint' container may also be used
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public required BuildCacheContainerType Type { get; init; }

        /// <nodoc/>
        public required string Signature { get; init; }
    }
}
