// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Distributed;

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
