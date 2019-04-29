// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;

namespace BuildXL.Cache.MemoizationStore.Vsts.Adapters
{
    /// <summary>
    /// A Data class for a Selector and ContentHashList.
    /// </summary>
    public readonly struct SelectorAndContentHashListWithCacheMetadata
    {
        /// <summary>
        /// The Selector.
        /// </summary>
        public readonly Selector Selector;

        /// <summary>
        /// The contenthashlist
        /// </summary>
        public readonly ContentHashListWithCacheMetadata ContentHashList;

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectorAndContentHashListWithCacheMetadata"/> struct.
        /// </summary>
        public SelectorAndContentHashListWithCacheMetadata(Selector selector, ContentHashListWithCacheMetadata contentHashList)
        {
            Selector = selector;
            ContentHashList = contentHashList;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            SelectorAndContentHashListWithCacheMetadata? objAsSelectorAndChl = obj as SelectorAndContentHashListWithCacheMetadata?;

            if (objAsSelectorAndChl != null)
            {
                return Equals(objAsSelectorAndChl.Value);
            }

            return base.Equals(obj);
        }

        /// <summary>
        /// Returns whether one of the structs is equal to another.
        /// </summary>
        public bool Equals(SelectorAndContentHashListWithCacheMetadata other)
        {
            return Selector.Equals(other.Selector) && Equals(ContentHashList, other.ContentHashList);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (Selector.GetHashCode() * 397) ^ (ContentHashList?.GetHashCode() ?? 0);
            }
        }

        /// <nodoc />
        public static bool operator ==(SelectorAndContentHashListWithCacheMetadata left, SelectorAndContentHashListWithCacheMetadata right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(SelectorAndContentHashListWithCacheMetadata left, SelectorAndContentHashListWithCacheMetadata right)
        {
            return !left.Equals(right);
        }
    }
}
