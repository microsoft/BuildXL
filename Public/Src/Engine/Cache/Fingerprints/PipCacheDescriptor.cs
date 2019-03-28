// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// Descriptor for a particular pip execution. This is the 'V1' format which is being phased out.
    /// </summary>
    public partial class PipCacheDescriptor : IPipFingerprintEntryData
    {
        /// <nodoc />
        public PipFingerprintEntryKind Kind => PipFingerprintEntryKind.DescriptorV1;

        /// <inheritdoc />
        public IEnumerable<BondContentHash> ListRelatedContent()
        {
            return OutputHashesByPath.Select(hashByName => hashByName.ContentHash);
        }

        /// <nodoc />
        public PipFingerprintEntry ToEntry()
        {
            return PipFingerprintEntry.CreateFromData(this);
        }
    }
}
