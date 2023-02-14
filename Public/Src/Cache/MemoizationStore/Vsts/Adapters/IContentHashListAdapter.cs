// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;

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
        Task<Result<IEnumerable<SelectorAndContentHashListWithCacheMetadata>>> GetSelectorsAsync(
            OperationContext context,
            string cacheNamespace,
            Fingerprint weakFingerprint,
            int maxSelectorsToFetch);

        /// <summary>
        /// Gets a single content hashlist from the service asynchronously.
        /// </summary>
        Task<Result<ContentHashListWithCacheMetadata>> GetContentHashListAsync(
            OperationContext context,
            string cacheNamespace,
            StrongFingerprint strongFingerprint);

        /// <summary>
        /// Adds a single content hashlist from the service asynchronously.
        /// </summary>
        Task<Result<ContentHashListWithCacheMetadata>> AddContentHashListAsync(
            OperationContext context,
            string cacheNamespace,
            StrongFingerprint strongFingerprint,
            ContentHashListWithCacheMetadata valueToAdd,
            bool forceUpdate);

        /// <summary>
        /// Incorporates and extends lifetimes of a set of strong fingeprints.
        /// </summary>
        Task IncorporateStrongFingerprints(OperationContext context, string cacheNamespace, IncorporateStrongFingerprintsRequest incorporateStrongFingerprintsRequest);
    }
}
