// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Well-known <see cref="ContentHash" /> constants. Some of these hashes have been picked arbitrarily to represent special
    /// file states, etc.
    /// </summary>
    /// <remarks>
    /// The arbitrary constants are similar in spirit to git's 'null ref' (all-zero hash). 2^160 is a fairly large space.
    /// </remarks>
    public static class WellKnownContentHashes
    {
        /// <summary>
        /// We hash the content of source files, but disregard 'untracked' source files outside of the source root.
        /// For these files we use a content hash of all zeros.
        /// </summary>
        /// <remarks>
        /// This hash is arbitrary (like git's null ref)
        /// TODO: policy around untracked files needs to change
        /// </remarks>
        public static readonly ContentHash UntrackedFile = ContentHashingUtilities.ZeroHash;

        /// <summary>
        /// File that doesn't exist on disk
        /// </summary>
        /// <remarks>
        /// This hash is arbitrary (like git's null ref)
        /// </remarks>
        public static readonly ContentHash AbsentFile = ContentHashingUtilities.CreateSpecialValue(1);

        /// <summary>
        /// Existent file that was probed, but its content hash is not known
        /// </summary>
        /// <remarks>
        /// This hash is arbitrary (like git's null ref)
        /// </remarks>
        public static readonly ContentHash ExistentFileProbe = ContentHashingUtilities.CreateSpecialValue(2);
    }

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
