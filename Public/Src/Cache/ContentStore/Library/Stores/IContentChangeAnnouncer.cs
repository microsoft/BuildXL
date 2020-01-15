// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
