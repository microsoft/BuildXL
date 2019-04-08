// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Announce changes in CAS content.
    /// </summary>
    public interface IContentChangeAnnouncer
    {
        /// <summary>
        ///     Inform subscribers content has been added.
        /// </summary>
        Task ContentAdded(ContentHashWithSize item);

        /// <summary>
        ///     Inform subscribers content has been evicted.
        /// </summary>
        Task ContentEvicted(ContentHashWithSize item);
    }
}
