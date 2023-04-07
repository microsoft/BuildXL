// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Google.Protobuf;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <nodoc />
    public interface IPipFingerprintEntryData
    {
        /// <nodoc />
        ulong Id { get; }

        /// <summary>
        /// Lists CAS content hashes related to the entry. For an index, these would be hashes of path sets.
        /// For a descriptor, these would be hashes of outputs.
        /// </summary>
        IEnumerable<ByteString> ListRelatedContent();

        /// <summary>
        /// Wraps this instance in a <see cref="PipFingerprintEntry"/> tagged with the appropriate kind.
        /// </summary>
        /// <remarks>
        /// We dispatch <see cref="ToEntry"/> through an interface vtable and force each entry data type to
        /// reimplement it (as <c>return PipFingerprintEntry.CreateFromData(this);</c>) - this instantiates
        /// <see cref="PipFingerprintEntry.CreateFromData"/> for each concrete type 
        /// Alternatively, we would have a single method that branched based on <see cref="PipFingerprintEntryKind"/>.
        /// </remarks>
        PipFingerprintEntry ToEntry();
    }
}
