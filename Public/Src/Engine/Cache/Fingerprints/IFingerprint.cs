// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Engine.Cache.Fingerprints
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
