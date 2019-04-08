// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// Descriptor for a cached pip graph.
    /// </summary>
    public partial class FileDownloadDescriptor : IPipFingerprintEntryData
    {
        public PipFingerprintEntryKind Kind => PipFingerprintEntryKind.FileDownload;

        /// <inheritdoc />
        public IEnumerable<BondContentHash> ListRelatedContent()
        {
            yield return Content;
        }

        public PipFingerprintEntry ToEntry()
        {
            return PipFingerprintEntry.CreateFromData(this);
        }

        public static FileDownloadDescriptor Create(
            BondContentHash content,
            string url,
            string traceInfo)
        {
            var descriptor = new FileDownloadDescriptor
            {
                Id = PipFingerprintEntry.CreateUniqueId(),
                Content = content,
                Url = url,
                TraceInfo = traceInfo,
            };

            return descriptor;
        }
    }
}
