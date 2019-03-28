// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// Descriptor for a cached pip graph.
    /// </summary>
    public partial class PackageDownloadDescriptor : IPipFingerprintEntryData
    {
        public PipFingerprintEntryKind Kind => PipFingerprintEntryKind.PackageDownload;

        /// <inheritdoc />
        public IEnumerable<BondContentHash> ListRelatedContent()
        {
            return Contents.Select(hashByName => hashByName.ContentHash);
        }

        public PipFingerprintEntry ToEntry()
        {
            return PipFingerprintEntry.CreateFromData(this);
        }

        public static PackageDownloadDescriptor Create(
            string friendlyName,
            List<StringKeyedHash> contents,
            string traceInfo)
        {
            var descriptor = new PackageDownloadDescriptor
            {
                Id = PipFingerprintEntry.CreateUniqueId(),
                FriendlyName = friendlyName,
                Contents = contents,
                TraceInfo = traceInfo,
            };

            return descriptor;
        }
    }
}
