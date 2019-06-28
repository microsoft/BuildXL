// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.Tasks;
using Microsoft.VisualStudio.Services.ArtifactServices.App.Shared.Cache;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Authentication;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.Drop.App.Core;
using Microsoft.VisualStudio.Services.Drop.App.Core.Telemetry;
using Microsoft.VisualStudio.Services.Drop.App.Core.Tracing;
using Microsoft.VisualStudio.Services.Drop.WebApi;
using Microsoft.VisualStudio.Services.ItemStore.Common;
using Newtonsoft.Json;
using static BuildXL.Utilities.FormattableStringEx;

namespace Tool.DropDaemon
{
    /// <summary>
    ///     Responsible for communicating with a drop service endpoint.
    /// </summary>
    public sealed class VsoClient : IDropClient
    {
        #region Private Nested Types

        /// <summary>
        ///     Private nested class for keeping statistics.
        /// </summary>
        private sealed class DropStatistics
        {
            internal long NumCompleteBatches = 0;
            internal long NumIncompleteBatches = 0;
            internal long NumBatches = 0;
            internal long NumAddFileRequests = 0;
            internal long NumFilesAssociated = 0;
            internal long NumFilesUploaded = 0;
            internal long AuthTimeMs = 0;
            internal long CreateTimeMs = 0;
            internal long TotalAssociateTimeMs = 0;
            internal long TotalUploadTimeMs = 0;
            internal long TotalComputeFileBlobDescriptorForAssociateMs = 0;
            internal long TotalComputeFileBlobDescriptorForUploadMs = 0;
            internal long FinalizeTimeMs = 0;
            internal long TotalUploadSizeMb = 0;

            internal IDictionary<string, long> ToDictionary()
            {
                var dict = new Dictionary<string, long>();
                AddStat(dict, Statistics.AuthTimeMs, ref AuthTimeMs);
                AddStat(dict, Statistics.CreateTimeMs, ref CreateTimeMs);
                AddStat(dict, Statistics.NumberOfAddFileRequests, ref NumAddFileRequests);
                AddStat(dict, Statistics.NumberOfBatches, ref NumBatches);
                AddStat(dict, "NumCompleteBatches", ref NumCompleteBatches);
                AddStat(dict, "NumIncompleteBatches", ref NumIncompleteBatches);
                AddStat(dict, Statistics.NumberOfFilesAssociated, ref NumFilesAssociated);
                AddStat(dict, Statistics.NumberOfFilesUploaded, ref NumFilesUploaded);
                AddStat(dict, Statistics.TotalAssociateTimeMs, ref TotalAssociateTimeMs);
                AddStat(dict, Statistics.TotalUploadTimeMs, ref TotalUploadTimeMs);
                AddStat(dict, Statistics.TotalComputeFileBlobDescriptorForAssociateMs, ref TotalComputeFileBlobDescriptorForAssociateMs);
                AddStat(dict, Statistics.TotalComputeFileBlobDescriptorForUploadMs, ref TotalComputeFileBlobDescriptorForUploadMs);
                AddStat(dict, Statistics.FinalizeTimeMs, ref FinalizeTimeMs);
                AddStat(dict, Statistics.TotalUploadSizeMb, ref TotalUploadSizeMb);
                return dict;
            }

            private static void AddStat(IDictionary<string, long> stats, string key, ref long value)
            {
                stats[I($"DropDaemon.{key}")] = Volatile.Read(ref value);
            }
        }
        #endregion

        private readonly ILogger m_logger;
        private readonly DropConfig m_config;
        private readonly IDropServiceClient m_dropClient;
        private readonly CancellationTokenSource m_cancellationSource;
        private readonly Timer m_batchTimer;

        private readonly BatchBlock<AddFileItem> m_batchBlock;
        private readonly BufferBlock<AddFileItem[]> m_bufferBlock;
        private readonly ActionBlock<AddFileItem[]> m_actionBlock;

        private long m_lastTimeProcessAddFileRanInTicks = DateTime.UtcNow.Ticks;

        private CancellationToken Token => m_cancellationSource.Token;

        private static IAppTraceSource Tracer => DropAppTraceSource.SingleInstance;

        private static CacheContext CacheContext => null; // not needed for anything but "get", which we don't do

        private VssCredentials GetCredentials() =>
            new VsoCredentialHelper(m => m_logger.Verbose(m))
                .GetCredentials(m_config.Service, true, null);

        private ArtifactHttpClientFactory GetFactory() =>
            new ArtifactHttpClientFactory(
                credentials: GetCredentials(),
                httpSendTimeout: m_config.HttpSendTimeout,
                tracer: Tracer,
                verifyConnectionCancellationToken: Token);

        private DropStatistics Stats { get; }

        /// <nodoc/>
        public Uri ServiceEndpoint => m_config.Service;

        /// <nodoc/>
        public string DropName => m_config.Name;

        /// <inheritdoc />
        public string DropUrl => ServiceEndpoint + "/_apis/drop/drops/" + DropName;

        /// <nodoc />
        public VsoClient(ILogger logger, DropConfig dropConfig)
        {
            Contract.Requires(dropConfig != null);

            m_logger = logger;
            m_config = dropConfig;
            m_cancellationSource = new CancellationTokenSource();

            logger.Info("Using drop config: " + JsonConvert.SerializeObject(m_config));

            Stats = new DropStatistics();

            // instantiate drop client
            m_dropClient = new ReloadingDropServiceClient(
                logger: logger,
                clientConstructor: CreateDropServiceClient);

            // create dataflow blocks
            var groupingOptions = new GroupingDataflowBlockOptions { Greedy = true };

            var actionOptions = new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = m_config.MaxParallelUploads };
            var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

            m_batchBlock = new BatchBlock<AddFileItem>(m_config.BatchSize, groupingOptions);
            m_bufferBlock = new BufferBlock<AddFileItem[]>(); // per http://blog.stephencleary.com/2012/11/async-producerconsumer-queue-using.html, good to have buffer when throttling
            m_actionBlock = new ActionBlock<AddFileItem[]>(ProcessAddFilesAsync, actionOptions);
            m_batchBlock.LinkTo(m_bufferBlock, linkOptions);
            m_bufferBlock.LinkTo(m_actionBlock, linkOptions);

            // create and set up timer for triggering the batch block
            TimeSpan timerInterval = m_config.NagleTime;
            m_batchTimer = new Timer(FlushBatchBlock, null, timerInterval, timerInterval);
        }

        /// <summary>
        ///     Invokes <see cref="IDropServiceClient.CreateAsync"/>.
        /// </summary>
        public async Task<DropItem> CreateAsync(CancellationToken token)
        {
            var startTime = DateTime.UtcNow;

            var result = await m_dropClient.CreateAsync(
                DropName,
                isAppendOnly: true,
                expirationDate: DateTime.UtcNow.Add(m_config.Retention),
                chunkDedup: m_config.EnableChunkDedup,
                cancellationToken: token);

            Interlocked.Add(ref Stats.CreateTimeMs, ElapsedMillis(startTime));
            return result;
        }

        /// <summary>
        ///     <see cref="CreateAsync(CancellationToken)"/>
        /// </summary>
        public Task<DropItem> CreateAsync() => CreateAsync(Token);

        /// <summary>
        ///     Calculates the hash for the given file (if not given) and queues it up
        ///     to be batch-processed later (<see cref="ProcessAddFilesAsync"/>).
        /// </summary>
        public async Task<AddFileResult> AddFileAsync(IDropItem dropItem)
        {
            Contract.Requires(dropItem != null);

            m_logger.Verbose("Queued file '{0}'", dropItem);

            Interlocked.Increment(ref Stats.NumAddFileRequests);

            var addFileItem = new AddFileItem(dropItem);
            await m_batchBlock.SendAsync(addFileItem);
            return await addFileItem.TaskSource.Task;
        }

        /// <summary>
        ///     Invokes <see cref="IDropServiceClient.FinalizeAsync"/>.
        /// </summary>
        public async Task<FinalizeResult> FinalizeAsync(CancellationToken token)
        {
            m_batchBlock.Complete();
            await m_actionBlock.Completion;

            var startTime = DateTime.UtcNow;
            await m_dropClient.FinalizeAsync(DropName, token);
            Interlocked.Add(ref Stats.FinalizeTimeMs, ElapsedMillis(startTime));

            return new FinalizeResult();
        }

        /// <summary>
        ///     <see cref="FinalizeAsync(CancellationToken)"/>.
        /// </summary>
        public Task<FinalizeResult> FinalizeAsync() => FinalizeAsync(Token);

        /// <summary>
        ///     Returns statistics including:
        ///       - 'drop create' time
        ///       - number of 'addfile' requests
        ///       - number of 'addfile' processing batches
        ///       - number of files associated
        ///       - number of files uploaded
        ///       - total time spent running "Associate"
        ///       - total time spent running "UploadAndAssociate"
        ///       - total time spent computing light FileBlobDescriptor (for "Associate")
        ///       - total time spent computing full FileBlobDescriptor (for "UploadAndAssociate")
        ///       - 'drop finalize' time
        /// </summary>
        public IDictionary<string, long> GetStats()
        {
            return Stats.ToDictionary();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_dropClient.Dispose();
            m_cancellationSource.Dispose();
            m_batchTimer.Dispose();
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose client", Justification = "Caller is responsible for disposing it")]
        private IDropServiceClient CreateDropServiceClient()
        {
            var startTime = DateTime.UtcNow;
            var client = new DropServiceClient(
                ServiceEndpoint,
                GetFactory(),
                CacheContext,
                new DropClientTelemetry(ServiceEndpoint, Tracer, enable: m_config.EnableTelemetry),
                Tracer);
            Interlocked.Add(ref Stats.AuthTimeMs, ElapsedMillis(startTime));
            return client;
        }

        /// <summary>
        ///     Triggered by <see cref="m_batchTimer"/>.
        /// </summary>
        private void FlushBatchBlock(object state)
        {
            var elapsedTicks = ElapsedTicksSinceLastProcessedBatch();
            if (elapsedTicks > m_config.NagleTime.Ticks)
            {
                m_batchBlock.TriggerBatch();
            }
        }

        /// <summary>
        ///     Implements 'drop addfile' by first calling <see cref="IDropServiceClient.AssociateAsync"/>,
        ///     and then <see cref="IDropServiceClient.UploadAndAssociateAsync"/>.
        /// </summary>
        /// <remarks>
        ///     This method is called concurrently.
        /// </remarks>
        private async Task ProcessAddFilesAsync(AddFileItem[] batch)
        {
            Interlocked.Exchange(ref m_lastTimeProcessAddFileRanInTicks, DateTime.UtcNow.Ticks);

            int batchLength = batch.Length;
            if (batchLength == 0)
            {
                return;
            }

            Interlocked.Increment(ref Stats.NumBatches);
            if (batchLength == m_config.BatchSize)
            {
                Interlocked.Increment(ref Stats.NumCompleteBatches);
            }
            else
            {
                Interlocked.Increment(ref Stats.NumIncompleteBatches);
            }


            m_logger.Info("Processing a batch of {0} drop files.", batchLength);
            try
            {
                // compute blobs for associate
                var startTime = DateTime.UtcNow;
                FileBlobDescriptor[] blobsForAssociate = await Task.WhenAll(batch.Select(item => item.FileBlobDescriptorForAssociateAsync(m_config.EnableChunkDedup, Token)));
                Interlocked.Add(ref Stats.TotalComputeFileBlobDescriptorForAssociateMs, ElapsedMillis(startTime));

                // run 'Associate' on all items from the batch; the result will indicate which files were associated and which need to be uploaded
                AssociationsStatus associateStatus = await AssociateAsync(blobsForAssociate);
                IReadOnlyList<AddFileItem> itemsLeftToUpload = await SetResultForAssociatedNonMissingItems(batch, associateStatus, m_config.EnableChunkDedup, Token);

                // compute blobs for upload
                startTime = DateTime.UtcNow;
                FileBlobDescriptor[] blobsForUpload = await Task.WhenAll(itemsLeftToUpload.Select(item => item.FileBlobDescriptorForUploadAsync(m_config.EnableChunkDedup, Token)));
                Interlocked.Add(ref Stats.TotalComputeFileBlobDescriptorForUploadMs, ElapsedMillis(startTime));

                // run 'UploadAndAssociate' for the missing files.
                await UploadAndAssociateAsync(associateStatus, blobsForUpload);
                SetResultForUploadedMissingItems(itemsLeftToUpload);
                Interlocked.Add(ref Stats.TotalUploadSizeMb, blobsForUpload.Sum(b => b.FileSize ?? 0) >> 20);

                m_logger.Info("Done processing AddFile batch.");
            }
            catch (Exception e)
            {
                foreach (AddFileItem item in batch)
                {
                    if (!item.TaskSource.Task.IsCompleted)
                    {
                        item.TaskSource.SetException(e);
                    }
                }
            }
        }

        private async Task<AssociationsStatus> AssociateAsync(FileBlobDescriptor[] blobsForAssociate)
        {
            m_logger.Info("Running associate on {0} files.", blobsForAssociate.Length);

            var startTime = DateTime.UtcNow;

            Tuple<IEnumerable<BlobIdentifier>, AssociationsStatus> associateResult = await m_dropClient.AssociateAsync(
                DropName,
                blobsForAssociate.ToList(),
                abortIfAlreadyExists: false,
                cancellationToken: Token).ConfigureAwait(false);

            Interlocked.Add(ref Stats.TotalAssociateTimeMs, ElapsedMillis(startTime));
            Interlocked.Add(ref Stats.NumFilesAssociated, blobsForAssociate.Length - associateResult.Item2.Missing.Count());
            return associateResult.Item2;
        }

        private async Task UploadAndAssociateAsync(AssociationsStatus associateStatus, FileBlobDescriptor[] blobsForUpload)
        {
            int numMissing = associateStatus.Missing.Count();
            m_logger.Info("Uploading {0} missing files.", numMissing);

#if DEBUG
            // check that missing hashes reported in associateStatus match those provided in blobsForUpload
            var providedHashes = new HashSet<BlobIdentifier>(blobsForUpload.Select(fb => fb.BlobIdentifier));
            var notFoundMissingHashes = associateStatus.Missing.Where(b => !providedHashes.Contains(b)).ToList();
            if (notFoundMissingHashes.Any())
            {
                throw new DropDaemonException("This many hashes not found in blobs to upload: " + notFoundMissingHashes.Count());
            }
#endif

            var startTime = DateTime.UtcNow;

            await m_dropClient.UploadAndAssociateAsync(
                DropName,
                blobsForUpload.ToList(),
                abortIfAlreadyExists: false,
                firstAssociationStatus: associateStatus,
                cancellationToken: Token).ConfigureAwait(false);

            Interlocked.Add(ref Stats.TotalUploadTimeMs, ElapsedMillis(startTime));
            Interlocked.Add(ref Stats.NumFilesUploaded, numMissing);
        }

        private static long ElapsedMillis(DateTime startTime)
        {
            return (long)DateTime.UtcNow.Subtract(startTime).TotalMilliseconds;
        }

        private static void SetResultForUploadedMissingItems(IReadOnlyList<AddFileItem> uploadedItems)
        {
            foreach (AddFileItem item in uploadedItems)
            {
                item.TaskSource.SetResult(AddFileResult.UploadedAndAssociated);
            }
        }

        /// <summary>
        ///     Sets completion results for <see cref="AddFileItem"/>s that were successfully associated
        ///     (as indicated by <paramref name="associateStatus"/>), and returns <see cref="AddFileItem"/>s
        ///     for those that are missing (<see cref="AssociationsStatus.Missing"/>).
        /// </summary>
        private static async Task<IReadOnlyList<AddFileItem>> SetResultForAssociatedNonMissingItems(AddFileItem[] batch, AssociationsStatus associateStatus, bool chunkDedup, CancellationToken cancellationToken)
        {
            var missingItems = new List<AddFileItem>();
            var missingBlobIds = associateStatus.Missing;
            foreach (AddFileItem item in batch)
            {
                var itemFileBlobDescriptor = await item.FileBlobDescriptorForAssociateAsync(chunkDedup, cancellationToken);
                if (!missingBlobIds.Contains(itemFileBlobDescriptor.BlobIdentifier))
                {
                    item.TaskSource.SetResult(AddFileResult.Associated);
                }
                else
                {
                    missingItems.Add(item);
                }
            }

            return missingItems;
        }

        private long ElapsedTicksSinceLastProcessedBatch()
        {
            return DateTime.UtcNow.Ticks - Interlocked.Read(ref m_lastTimeProcessAddFileRanInTicks);
        }

        /// <summary>
        ///     Dataflow descriptor for the 'addfile' job.
        /// </summary>
        internal sealed class AddFileItem
        {
            private readonly IDropItem m_dropItem;

            private FileBlobDescriptor m_fileBlobDescriptorForUpload;
            private FileBlobDescriptor m_fileBlobDescriptorForAssociate;

            internal TaskSourceSlim<AddFileResult> TaskSource { get; }

            internal string FullFilePath => m_dropItem.FullFilePath;

            internal AddFileItem(IDropItem item)
            {
                TaskSource = TaskSourceSlim.Create<AddFileResult>();
                m_dropItem = item;
                m_fileBlobDescriptorForUpload = null;
                m_fileBlobDescriptorForAssociate = null;
            }

            /// <summary>
            ///     Returns a <see cref="FileBlobDescriptor"/> to be used for the 'drop associate' operation.
            ///
            ///     The only relevant fields for the 'associate' operation are <see cref="DropFile.RelativePath"/>
            ///     and <see cref="DropFile.BlobIdentifier"/>; hence, the returned descriptor does not necessarily contain
            ///     valid values for <see cref="DropFile.FileSize"/> and <see cref="FileBlobDescriptor.AbsolutePath"/>.
            ///
            ///     After the first time it is computed, the result is memoized and returned in all subsequent calls.
            /// </summary>
            internal async Task<FileBlobDescriptor> FileBlobDescriptorForAssociateAsync(bool chunkDedup, CancellationToken cancellationToken)
            {
                if (m_fileBlobDescriptorForAssociate == null)
                {
                    m_fileBlobDescriptorForAssociate = await ComputeFileBlobDescriptorForAssociateAsync(chunkDedup, cancellationToken);
                }

                return m_fileBlobDescriptorForAssociate;
            }

            /// <summary>
            ///     Returns a <see cref="FileBlobDescriptor"/> to be used for the 'drop upload-and-associate' operation.
            ///
            ///     The returned descriptor contains valid values for all the following fields (which are all needed
            ///     to upload a file to drop): <see cref="DropFile.BlobIdentifier"/>, <see cref="DropFile.RelativePath"/>,
            ///     <see cref="DropFile.FileSize"/>, and <see cref="FileBlobDescriptor.AbsolutePath"/>.
            ///
            ///     After the first time it is computed, the result is memoized and returned in all subsequent calls.
            /// </summary>
            internal async Task<FileBlobDescriptor> FileBlobDescriptorForUploadAsync(bool chunkDedup, CancellationToken cancellationToken)
            {
                if (m_fileBlobDescriptorForUpload == null)
                {
                    m_fileBlobDescriptorForUpload = await ComputeFileBlobDescriptorForUploadAsync(chunkDedup, cancellationToken);

                    // check that if a descriptor for 'associate' was previously returned, its blob identifier is the same
                    if (m_fileBlobDescriptorForAssociate != null &&
                        !m_fileBlobDescriptorForAssociate.BlobIdentifier.Equals(m_fileBlobDescriptorForUpload.BlobIdentifier))
                    {
                        throw new DropDaemonException(I($"Blob identifier for file '{m_fileBlobDescriptorForUpload.AbsolutePath}' returned for 'UploadAndAssociate' ({m_fileBlobDescriptorForUpload.BlobIdentifier}) is different from the one returned for 'Associate' ({m_fileBlobDescriptorForAssociate.BlobIdentifier})."));
                    }
                }

                return m_fileBlobDescriptorForUpload;
            }

            private async Task<FileBlobDescriptor> ComputeFileBlobDescriptorForUploadAsync(bool chunkDedup, CancellationToken cancellationToken)
            {
                FileInfo fileInfo = await m_dropItem.EnsureMaterialized();
                if (m_dropItem.BlobIdentifier != null)
                {
#if DEBUG
                    // in debug builds: recompute blob identifier from file and assert it's the same as the provided one
                    await DropItemForFile.ComputeAndDoubleCheckBlobIdentifierAsync(m_dropItem.BlobIdentifier, fileInfo.FullName, fileInfo.Length, chunkDedup, phase: "CalculateFullFileBlobDescriptor", cancellationToken: cancellationToken);
#endif

                    // avoid recomputing blob identifier from file
                    var dropFile = new DropFile(m_dropItem.RelativeDropPath, fileInfo.Length, m_dropItem.BlobIdentifier);
                    return new FileBlobDescriptor(dropFile, fileInfo.FullName);
                }
                else
                {
                    return await DropItemForFile.ComputeFileDescriptorFromFileAsync(fileInfo.FullName, chunkDedup, m_dropItem.RelativeDropPath, cancellationToken);
                }
            }

            private async Task<FileBlobDescriptor> ComputeFileBlobDescriptorForAssociateAsync(bool chunkDedup, CancellationToken cancellationToken)
            {
                if (m_dropItem.BlobIdentifier != null)
                {
                    // if blob identifier is provided by IDropItem we don't have to compute full
                    // file blob descriptor (from actual file content) in order to execute "associate"
                    return new FileBlobDescriptor(
                        new DropFile(
                            relativePath: m_dropItem.RelativeDropPath,
                            fileSize: m_dropItem.FileLength,
                            blobId: m_dropItem.BlobIdentifier),
                        m_dropItem.FullFilePath);
                }
                else
                {
                    // no blob identifier is provided by IDropItem ==> calculate and use full FileBlobDescriptor
                    return await FileBlobDescriptorForUploadAsync(chunkDedup, cancellationToken);
                }
            }
        }
    }
}
