// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <nodoc />
    public partial class PipGraphInputDescriptor : IPipFingerprintEntryData
    {
        /// <inheritdoc />
        public PipFingerprintEntryKind Kind => PipFingerprintEntryKind.GraphInputDescriptor;

        /// <inheritdoc />
        public IEnumerable<BondContentHash> ListRelatedContent()
        {
            yield break;
        }

        /// <inheritdoc />
        public PipFingerprintEntry ToEntry()
        {
            return PipFingerprintEntry.CreateFromData(this);
        }
    }
}
