// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     Pairing of a content hash list and corresponding determinism guarantee.
    /// </summary>
    public readonly struct ContentHashListWithDeterminism : IEquatable<ContentHashListWithDeterminism>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHashListWithDeterminism"/> struct
        /// </summary>
        public ContentHashListWithDeterminism(ContentHashList contentHashList, CacheDeterminism determinism)
        {
            ContentHashList = contentHashList;
            Determinism = determinism;
        }

        /// <summary>
        ///     Gets the content hash list member.
        /// </summary>
        public ContentHashList ContentHashList { get; }

        /// <summary>
        ///     Gets the cache determinism member.
        /// </summary>
        public CacheDeterminism Determinism { get; }

        /// <inheritdoc />
        public bool Equals(ContentHashListWithDeterminism other)
        {
            if (!ReferenceEquals(ContentHashList, other.ContentHashList))
            {
                if (ContentHashList == null || other.ContentHashList == null)
                {
                    return false;
                }

                if (!ContentHashList.Equals(other.ContentHashList))
                {
                    return false;
                }
            }

            return Determinism.Equals(other.Determinism);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (ContentHashList?.GetHashCode() ?? 0) ^ Determinism.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"ContentHashList=[{ContentHashList}], Determinism={Determinism}";
        }

        /// <nodoc />
        public static bool operator ==(ContentHashListWithDeterminism left, ContentHashListWithDeterminism right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ContentHashListWithDeterminism left, ContentHashListWithDeterminism right)
        {
            return !left.Equals(right);
        }
    }
}
