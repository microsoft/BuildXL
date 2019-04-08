// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Modes of the FingerprintStore. 
    /// </summary>
    public enum FingerprintStoreMode : byte
    {
        /// <summary>
        /// Invalid.
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// Use default read and write patterns when populating the FingerprintStore.
        /// </summary>
        Default = 1,

        /// <summary>
        /// Experimental mode for spin drives.
        /// 
        /// Write new entries to disk irregardless of existing entries in the store.
        /// This reduces random reads for increased writes in sequential blocks.
        /// </summary>
        /// <remarks>
        /// An alternative scheme is to just delete the existing store altogether, but that risks data loss of entries unused in a particular build.
        /// </remarks>
        IgnoreExistingEntries = 2,

        /// <summary>
        /// Only store fingerprints computed at execution time. 
        /// This will reduce reads/writes on strong fingerprint cache misses by skipping storing any fingerprints from cache lookup time, which are useful for strong fingerprint analysis.
        /// </summary>
        ExecutionFingerprintsOnly = 3,
    }
}
