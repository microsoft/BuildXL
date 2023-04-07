// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Google.Protobuf;

namespace BuildXL.Engine.Cache.Fingerprints
{
    ///<nodoc />
    public partial class PipGraphInputDescriptor : IPipFingerprintEntryData
    {
        /// <inheritdoc />
        public IEnumerable<ByteString> ListRelatedContent()
        {
            yield break;
        }

        /// <inheritdoc />
        public PipFingerprintEntry ToEntry()
        {
            return PipFingerprintEntry.CreateFromData(PipFingerprintEntryKind.GraphInputDescriptor, this.ToByteString());
        }
    }
}
