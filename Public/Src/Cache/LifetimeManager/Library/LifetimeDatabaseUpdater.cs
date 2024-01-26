// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Utilities.ParallelAlgorithms;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    public class LifetimeDatabaseUpdater
    {
        private static readonly Tracer Tracer = new(nameof(LifetimeDatabaseUpdater));

        private readonly Dictionary<BlobNamespaceId, IBlobCacheTopology> _topologies;
        private readonly Dictionary<BlobNamespaceId, RocksDbLifetimeDatabase.IAccessor> _accessors;
        private readonly IClock _clock;

        private readonly ActionBlockSlim<(LifetimeDatabaseCreator.ProcessFingerprintRequest, TaskCompletionSource<Result<LifetimeDatabaseCreator.ProcessContentHashListResult>>)> _fingerprintCreatedActionBlock;

        public LifetimeDatabaseUpdater(
            Dictionary<BlobNamespaceId, IBlobCacheTopology> topologies,
            Dictionary<BlobNamespaceId, RocksDbLifetimeDatabase.IAccessor> accessors,
            IClock clock,
            int fingerprintsDegreeOfParallelism)
        {
            _topologies = topologies;
            _accessors = accessors;
            _clock = clock;

            _fingerprintCreatedActionBlock = ActionBlockSlim.CreateWithAsyncAction<(LifetimeDatabaseCreator.ProcessFingerprintRequest, TaskCompletionSource<Result<LifetimeDatabaseCreator.ProcessContentHashListResult>>)>(
                new ActionBlockSlimConfiguration(DegreeOfParallelism: fingerprintsDegreeOfParallelism),
                async tpl =>
                {
                    var (request, tcs) = tpl;
                    try
                    {
                        var result = await LifetimeDatabaseCreator.DownloadAndProcessContentHashListAsync(
                            request.Context, request.Container, request.BlobName, request.BlobLength, request.Database, request.Topology, clock);

                        tcs.SetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetResult(new Result<LifetimeDatabaseCreator.ProcessContentHashListResult>(ex));
                    }
                });
        }

        public void ContentCreated(OperationContext context, BlobNamespaceId namespaceId, string blobName, long length)
        {
            if (!_accessors.TryGetValue(namespaceId, out var db))
            {
                Tracer.Diagnostic(context, $"Ignoring creation of content with name={blobName} because Namespace={namespaceId} isn't being tracked.");
                return;
            }

            if (!BlobUtilities.TryExtractBlobName(blobName, out var hashString))
            {
                return;
            }

            if (!ContentHash.TryParse(hashString, out var hash))
            {
                // If a random file is created, ignore it.
                Tracer.Warning(context, $"Failed to parse content hash from BlobName=[{blobName}]. Ignoring blob.");
                return;
            }

            db.AddContent(hash, length);
        }

        internal async Task<Result<LifetimeDatabaseCreator.ProcessContentHashListResult>> ContentHashListCreatedAsync(
            OperationContext context,
            BlobNamespaceId namespaceId,
            string blobName,
            long blobLength)
        {
            if (!_accessors.TryGetValue(namespaceId, out var db) ||
                !_topologies.TryGetValue(namespaceId, out var topology))
            {
                Tracer.Diagnostic(context, $"Ignoring creation of content hash list with path {blobName} because Namespace={namespaceId} isn't being tracked.");
                return LifetimeDatabaseCreator.ProcessContentHashListResult.Success;
            }

            StrongFingerprint strongFingerprint;
            try
            {
                strongFingerprint = AzureBlobStorageMetadataStore.ExtractStrongFingerprintFromPath(blobName);
            }
            catch (Exception e)
            {
                Tracer.Debug(context, e, $"Failed to parse strong fingerprint from BlobName=[{blobName}]. Ignoring blob.");
                return LifetimeDatabaseCreator.ProcessContentHashListResult.ContentHashListDoesNotExist;
            }

            var oldContentHashList = db.GetContentHashList(strongFingerprint, out _);
            if (oldContentHashList is not null)
            {
                // The CHL was updated. This can happen for various reasons, such as a non-deterministic fingerprint being replaced by a deterministic one,
                // or the build engine failing to match a target to a selector.
                // In any case, we need to make sure that we reflect the fact that the old CHL no longer truly exists.
                db.DeleteContentHashList(blobName, oldContentHashList.Hashes);
            }

            var (containerClient, _) = await topology.GetContainerClientAsync(context, BlobCacheShardingKey.FromWeakFingerprint(strongFingerprint.WeakFingerprint));

            var tcs = new TaskCompletionSource<Result<LifetimeDatabaseCreator.ProcessContentHashListResult>>();
            var request = new LifetimeDatabaseCreator.ProcessFingerprintRequest(context, containerClient, blobName, blobLength, db, topology);
            _fingerprintCreatedActionBlock.Post((request, tcs));
            var result = await tcs.Task;
            return result;
        }
    }
}
