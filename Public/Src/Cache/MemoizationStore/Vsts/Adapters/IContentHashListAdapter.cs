// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Vsts.Adapters
{
    /// <summary>
    /// An adapter for talking to the VSTS Cache Service for contenthashlists.
    /// </summary>
    public interface IContentHashListAdapter
    {
        /// <summary>
        /// Gets the selectors from the VSTS Service asynchronously.
        /// </summary>
        Task<ObjectResult<IEnumerable<SelectorAndContentHashListWithCacheMetadata>>> GetSelectorsAsync(
            Context context,
            string cacheNamespace,
            Fingerprint weakFingerprint,
            int maxSelectorsToFetch);

        /// <summary>
        /// Gets a single content hashlist from the service asynchronously.
        /// </summary>
        Task<ObjectResult<ContentHashListWithCacheMetadata>> GetContentHashListAsync(Context context, string cacheNamespace, StrongFingerprint strongFingerprint);

        /// <summary>
        /// Adds a single content hashlist from the service asynchronously.
        /// </summary>
        Task<ObjectResult<ContentHashListWithCacheMetadata>> AddContentHashListAsync(Context context, string cacheNamespace, StrongFingerprint strongFingerprint, ContentHashListWithCacheMetadata valueToAdd);

        /// <summary>
        /// Incorporates and extends lifetimes of a set of strong fingeprints.
        /// </summary>
        Task IncorporateStrongFingerprints(Context context, string cacheNamespace, IncorporateStrongFingerprintsRequest incorporateStrongFingerprintsRequest);
    }
}
