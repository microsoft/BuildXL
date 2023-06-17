// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

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
        Task ContentAdded(Context context, ContentHashWithSize contentHashWithSize);

        /// <summary>
        ///     Inform subscribers content has been evicted.
        /// </summary>
        Task ContentEvicted(Context context, ContentHashWithSize contentHashWithSize);
    }
}
