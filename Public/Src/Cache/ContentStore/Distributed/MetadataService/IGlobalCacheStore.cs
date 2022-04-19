// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Interface that represents a central store (currently backed by Redis).
    /// </summary>
    public interface IGlobalCacheStore : IContentMetadataStore, IStartupShutdownSlim
    {
        
    }

    public interface IContentMetadataStore : IMetadataStore, IStartupShutdownSlim
    {
        /// <summary>
        /// Gets the list of <see cref="ContentLocationEntry"/> for every hash specified by <paramref name="contentHashes"/> from a central store.
        /// </summary>
        /// <remarks>
        /// The resulting collection (in success case) will have the same size as <paramref name="contentHashes"/>.
        /// </remarks>
        Task<Result<IReadOnlyList<ContentLocationEntry>>> GetBulkAsync(OperationContext context, IReadOnlyList<ShortHash> contentHashes);

        /// <summary>
        /// Notifies a central store that content represented by <paramref name="contentHashes"/> is available on a current machine.
        /// </summary>
        /// <remarks>
        /// Using <see cref="ValueTask{BoolResult}"/> instead of normal tasks because <see cref="GlobalCacheService.RegisterContentLocationsAsync"/> can
        /// do the registration synchronously, and using <code>ValueTask</code> allows achieving allocation free implementation that is very useful
        /// because this method can be called a lot at start time of the service.
        /// </remarks>
        ValueTask<BoolResult> RegisterLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHashWithSize> contentHashes, bool touch);

        /// <summary>
        /// Puts a blob into the content location store.
        /// </summary>
        Task<PutBlobResult> PutBlobAsync(OperationContext context, ShortHash hash, byte[] blob);

        /// <summary>
        /// Gets a blob from the content location store.
        /// </summary>
        Task<GetBlobResult> GetBlobAsync(OperationContext context, ShortHash hash);

        /// <summary>
        /// Gets a value indicating whether the store supports storing and retrieving blobs.
        /// </summary>
        bool AreBlobsSupported { get; }
    }

    public interface IMetadataStore : IStartupShutdownSlim
    {
        /// <nodoc />
        Task<Result<bool>> CompareExchangeAsync(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            SerializedMetadataEntry replacement,
            string expectedReplacementToken);

        /// <nodoc />
        Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level);

        /// <nodoc />
        Task<Result<SerializedMetadataEntry>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint);
    }

    public interface IMetadataStoreWithIncorporation : IMetadataStore
    {
        Task<BoolResult> IncorporateStrongFingerprintsAsync(OperationContext context, IEnumerable<Task<StrongFingerprint>> strongFingerprints);
    }
}
