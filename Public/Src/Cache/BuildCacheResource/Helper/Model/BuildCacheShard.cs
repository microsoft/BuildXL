// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace BuildXL.Cache.BuildCacheResource.Model
{
    /// <summary>
    /// A blob storage account (shard) backing a cache resource
    /// </summary>
    public record BuildCacheShard
    {
        private readonly IReadOnlyCollection<BuildCacheContainer>? _containers;

        /// <nodoc/>
        [JsonPropertyName("StorageUrl")]
        public required Uri StorageUri { get; init; }

        /// <summary>
        /// The set of active containers under this storage account
        /// </summary>
        /// <remarks>
        /// This is represented as a list in the corresponding JSON to accommodate for future changes in the number of containers/respective roles, but
        /// the current assumption is that there will always be 3 containers: one content, one metadata and one checkpoint.
        /// </remarks>
        public required IReadOnlyCollection<BuildCacheContainer> Containers
        {
            get => _containers!;
            init
            {
                if (value.Count != 3)
                {
                    throw new ArgumentException($"Expected to have three containers but found '{value.Count}': {string.Join(",", value.Select(container => container.Name))}.");
                }

                var invalidContainers = value.GroupBy(shard => shard.Type).FirstOrDefault(group => group.Count() != 1);

                if (invalidContainers != null)
                {
                    throw new ArgumentException($"A shard must contain three containers of the types: content, metadata and checkpoint. " +
                        $"However, containers '${string.Join(",", invalidContainers.Select(container => container.Name))}' have all type '{invalidContainers.Key}'");
                }

                _containers = value;

                MetadataContainer = _containers.Single(container => container.Type == BuildCacheContainerType.Metadata);
                ContentContainer = _containers.Single(container => container.Type == BuildCacheContainerType.Content);
                CheckpointContainer = _containers.Single(container => container.Type == BuildCacheContainerType.Checkpoint);
            }
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        /// <nodoc/>
        public BuildCacheContainer MetadataContainer { get; private set; }

        /// <nodoc/>
        public BuildCacheContainer ContentContainer { get; private set; }

        /// <nodoc/>
        public BuildCacheContainer CheckpointContainer { get; private set; }

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    }
}
