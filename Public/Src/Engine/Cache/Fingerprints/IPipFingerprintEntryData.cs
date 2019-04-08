// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <nodoc />
    public interface IPipFingerprintEntryData
    {
        /// <nodoc />
        PipFingerprintEntryKind Kind { get; }

        /// <nodoc />
        ulong Id { get; }

        /// <summary>
        /// Lists CAS content hashes related to the entry. For an index, these would be hashes of path sets.
        /// For a descriptor, these would be hashes of outputs.
        /// </summary>
        IEnumerable<BondContentHash> ListRelatedContent();

        /// <summary>
        /// Wraps this instance in a <see cref="PipFingerprintEntry"/> tagged with the appropriate kind.
        /// </summary>
        /// <remarks>
        /// We dispatch <see cref="ToEntry"/> through an interface vtable and force each entry data type to
        /// reimplement it (as <c>return PipFingerprintEntry.CreateFromData(this);</c>) - this instantiates
        /// <see cref="PipFingerprintEntry.CreateFromData{T}"/> for each concrete type, as required by Bond's
        /// <see cref="Bond.Serialize.To{W,T}(W,T)"/>. Alternatively, we would have a single method that branched
        /// based on <see cref="Kind"/>.
        /// </remarks>
        PipFingerprintEntry ToEntry();
    }
}
