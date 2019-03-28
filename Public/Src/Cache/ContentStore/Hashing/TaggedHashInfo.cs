// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Simple description of a hash that is tagged with its algorithm ID.
    /// </summary>
    public abstract class TaggedHashInfo : HashInfo
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TaggedHashInfo" /> class.
        /// </summary>
        protected TaggedHashInfo(HashType hashType, int length)
            : base(hashType, length)
        {
        }

        /// <summary>
        ///     Gets algorithm ID of hash type.
        /// </summary>
        public byte AlgorithmId => AlgorithmIdLookup.Find(HashType);
    }
}
