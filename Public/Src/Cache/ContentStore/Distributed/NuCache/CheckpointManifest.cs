// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities.Core;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Checkpoint state obtained from the central store.
    /// </summary>
    public record CheckpointManifest
    {
        /// <nodoc />
        public List<CheckpointManifestContentEntry> ContentByPath { get; init; } = new();

        public CheckpointManifestContentEntry? TryGetValue(string relativePath)
        {
            return ContentByPath.FirstOrDefault(entry => entry.RelativePath.Equals(relativePath, StringComparison.InvariantCultureIgnoreCase));
        }

        public void Add(CheckpointManifestContentEntry entry)
        {
            ContentByPath.Add(entry);
        }
    }

    public record CheckpointManifestContentEntry(ShortHash Hash, string RelativePath, string StorageId, long Size)
    {
#if NET_FRAMEWORK
        /// <summary>
        /// This parameterless constructor is required for System.Text.Json deserialization only in .NET Framework
        /// </summary>
        public CheckpointManifestContentEntry()
            : this(default, string.Empty, string.Empty, 0)
        {

        }
#endif
    }
}
