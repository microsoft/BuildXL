// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;

namespace BuildXL.Storage.Fingerprints
{
    /// <summary>
    /// Methods for handling constants from WellKnownContentHashes. 
    /// </summary>
    public static class WellKnownContentHashUtilities
    {
        /// <summary>
        /// Checks whether <paramref name="hash"/> is an AbsentFile hash. Note: this method does a hash algorithm agnostic check 
        /// </summary>
        /// <remarks>
        /// ContentHash contains not only the hash itself, but also the algorithm used to create the hash.
        /// Before comparing any hash with WellKnownContentHashes.AbsentFile, one needs to ensure that
        /// ContentHashingUtilities.HashInfo has been initialized (i.e., the algorithm has been selected).
        /// In some specific cases, it might not be possible. This method covers the scenario where we
        /// want to check whether a hash is an AbsentFile hash without worrying about the hashing algorithm.
        /// </remarks>
        public static bool IsAbsentFileHash(in ContentHash hash) => hash.IsSpecialValue(1);
    }
}
