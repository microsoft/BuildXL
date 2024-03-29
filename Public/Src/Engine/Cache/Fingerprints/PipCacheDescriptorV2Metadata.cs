﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// Protobuf-serialized part of a descriptor for a particular pip execution. This is the 'V2' format in which we have
    /// a two-phase lookup (weak and strong fingerprints), hence ObservedInputHashesByPath and ObservedDirectoryMembershipFingerprintsByPath
    /// have been removed.
    /// Furthermore, all output hashes (including standard error and standard output) are stored externally, since cache entries
    /// natively store hash-lists. PipCacheDescriptorV2Metadata is serialized and referenced by hash in the 'metadata' slot of the cache entry;
    /// together with the hash list, this forms a PipCacheDescriptorV2 (which is not a Bond type).
    /// </summary>
    public partial class PipCacheDescriptorV2Metadata : IPipFingerprintEntryData
    {
        /// <inheritdoc />
        public IEnumerable<ByteString> ListRelatedContent()
        {
            return StaticOutputHashes.Select(info => info.Info.Hash);
        }

        /// <inheritdoc />
        public PipFingerprintEntry ToEntry()
        {
            return PipFingerprintEntry.CreateFromData(PipFingerprintEntryKind.DescriptorV2, this.ToByteString());
        }
    }
}
