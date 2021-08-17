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
    public interface IGlobalCacheStore : IContentMetadataStore, IClusterManagementStore, IStartupShutdownSlim
    {
        
    }

    public interface IContentMetadataStore : IStartupShutdownSlim
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
        Task<BoolResult> RegisterLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHashWithSize> contentHashes, bool touch);

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

        #region Memoization Operations

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

        #endregion Memoization Operations
    }

    public interface IClusterManagementStore : IStartupShutdownSlim
    {
        /// <summary>
        /// Notifies the store that the specified machine is alive
        /// </summary>
        Task<Result<HeartbeatMachineResponse>> HeartbeatAsync(OperationContext context, HeartbeatMachineRequest request);

        /// <summary>
        /// Gets updates to the cluster state based on the provided <see cref="GetClusterUpdatesRequest.MaxMachineId"/>
        /// </summary>
        Task<Result<GetClusterUpdatesResponse>> GetClusterUpdatesAsync(OperationContext context, GetClusterUpdatesRequest request);
    }
}
