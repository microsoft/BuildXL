// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Hashing;

// ReSharper disable All
namespace BuildXL.Cache.ContentStore.Interfaces.Distributed
{
    /// <summary>
    /// A wrapper for content hashes and their respective locations.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ContentHashAndLocations
    {
        /// <summary>
        /// Locations where the content hash will be found. A null list means the only location is the local machine and a populated list holds remote locations.
        /// </summary>
        public IReadOnlyList<MachineLocation> Locations { get; }

        /// <summary>
        /// The content hash for the specified locations.
        /// </summary>
        public ContentHash ContentHash { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentHashAndLocations"/> struct.
        /// </summary>
        public ContentHashAndLocations(ContentHash contentHash, IReadOnlyList<MachineLocation> locations = null)
        {
            ContentHash = contentHash;
            Locations = locations;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[ContentHash={ContentHash} LocationCount={Locations?.Count}]";
        }
    }
}
