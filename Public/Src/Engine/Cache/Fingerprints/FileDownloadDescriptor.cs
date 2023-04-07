// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Google.Protobuf;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// Descriptor for a cached pip graph.
    /// </summary>
    public partial class FileDownloadDescriptor : IPipFingerprintEntryData
    {
        /// <inheritdoc />
        public IEnumerable<ByteString> ListRelatedContent()
        {
            yield return Content;
        }

        /// <inheritdoc />
        public PipFingerprintEntry ToEntry()
        {
            return PipFingerprintEntry.CreateFromData(PipFingerprintEntryKind.FileDownload, this.ToByteString());
        }

        public static FileDownloadDescriptor Create(
           ByteString content,
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
