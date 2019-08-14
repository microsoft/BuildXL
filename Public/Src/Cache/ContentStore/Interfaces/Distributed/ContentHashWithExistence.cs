// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Interfaces.Distributed
{
    /// <summary>
    /// Content hash with existence check in content tracker.
    /// </summary>
    public class ContentHashWithExistence
    {
        /// <summary>
        /// The content hash for the specified locations.
        /// </summary>
        public readonly ContentHash ContentHash;

        /// <summary>
        /// Whether the hash exists or not.
        /// </summary>
        public readonly bool Exists;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentHashWithExistence"/> class.
        /// </summary>
        public ContentHashWithExistence(ContentHash contentHash, bool exists)
        {
            ContentHash = contentHash;
            Exists = exists;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[ContentHash={ContentHash.ToShortString()} Exists={Exists}]";
        }
    }
}
