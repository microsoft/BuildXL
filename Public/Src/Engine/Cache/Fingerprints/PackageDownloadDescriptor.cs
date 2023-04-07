// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// Descriptor for a cached pip graph.
    /// </summary>
    public partial class PackageDownloadDescriptor : IPipFingerprintEntryData
    {
        /// <inheritdoc />
        public IEnumerable<ByteString> ListRelatedContent()
        {
            return Contents.Select(hashByName => hashByName.ContentHash);
        }

        /// <inheritdoc />
        public PipFingerprintEntry ToEntry()
        {
            return PipFingerprintEntry.CreateFromData(PipFingerprintEntryKind.PackageDownload, this.ToByteString());
        }

        /// <nodoc /
        public static PackageDownloadDescriptor Create(
            string friendlyName,
            List<StringKeyedHash> contents,
            string traceInfo)
        {
            var descriptor = new PackageDownloadDescriptor
            {
                Id = PipFingerprintEntry.CreateUniqueId(),
                FriendlyName = friendlyName,
                TraceInfo = traceInfo,
            };
            descriptor.Contents.Add(contents);

            return descriptor;
        }
    }
}
