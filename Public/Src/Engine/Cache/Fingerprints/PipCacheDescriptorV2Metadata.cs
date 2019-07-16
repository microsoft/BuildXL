// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// Bond-serialized part of a descriptor for a particular pip execution. This is the 'V2' format in which we have
    /// a two-phase lookup (weak and strong fingerprints), hence ObservedInputHashesByPath and ObservedDirectoryMembershipFingerprintsByPath
    /// have been removed.
    /// Furthermore, all output hashes (including standard error and standard output) are stored externally, since cache entries
    /// natively store hash-lists. PipCacheDescriptorV2Metadata is serialized and referenced by hash in the 'metadata' slot of the cache entry;
    /// together with the hash list, this forms a PipCacheDescriptorV2 (which is not a Bond type).
    /// </summary>
    public partial class PipCacheDescriptorV2Metadata : IPipFingerprintEntryData
    {
        /// <nodoc />
        public PipFingerprintEntryKind Kind => PipFingerprintEntryKind.DescriptorV2;

        /// <summary>
        /// A small 32-bit bloom filter for determining a lower bound on how many machines have replicated the output content of this metadata entry
        /// </summary>
        public uint OutputContentReplicasMiniBloomFilter;

        /// <nodoc />
        public IEnumerable<BondContentHash> ListRelatedContent()
        {
            return StaticOutputHashes.Select(info => info.Info.Hash);
        }

        /// <nodoc />
        public PipFingerprintEntry ToEntry()
        {
            return PipFingerprintEntry.CreateFromData(this);
        }
    }
}
