// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Storage.Fingerprints
{
    /// <summary>
    /// A fingerprint is a <see cref="Fingerprint" /> of a dependency graph node.
    /// Multiple fingerprint types exist with different semantics, and are not mutually comparable.
    /// </summary>
    public interface IFingerprint
    {
        /// <summary>
        /// Returns the underlying hash represented by this fingerprint.
        /// </summary>
        Fingerprint Hash { get; }
    }
}
