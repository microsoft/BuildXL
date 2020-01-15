// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
