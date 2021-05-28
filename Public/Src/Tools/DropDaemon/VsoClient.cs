// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
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
using Tool.ServicePipDaemon;
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
            internal long TotalAssociateSizeBytes = 0;
            internal long TotalUploadSizeBytes = 0;
            internal long TotalBuildManifestRegistrationDurationMs = 0;
            internal long TotalBuildManifestRegistrationFailures = 0;

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
                // we track the total size of files in bytes, but log it in megabytes
                AddStat(dict, Statistics.TotalAssociateSizeMb, ref TotalAssociateSizeBytes, size => size >> 20);
                AddStat(dict, Statistics.TotalUploadSizeMb, ref TotalUploadSizeBytes, size => size >> 20);
                AddStat(dict, Statistics.TotalBuildManifestRegistrationDurationMs, ref TotalBuildManifestRegistrationDurationMs);
                AddStat(dict, Statistics.TotalBuildManifestRegistrationFailures, ref TotalBuildManifestRegistrationFailures);
                return dict;
            }

            private static void AddStat(IDictionary<string, long> stats, string key, ref long value, Func<long, long> converter = null)
            {
                stats[I($"DropDaemon.{key}")] = converter != null ? converter(Volatile.Read(ref value)) : Volatile.Read(ref value);
            }
        }
        #endregion

        private readonly IIpcLogger m_logger;
        private readonly DropConfig m_config;
        private readonly IDropServiceClient m_dropClient;
        private readonly CancellationTokenSource m_cancellationSource;
        private readonly DropDaemon m_dropDaemon;

        private readonly NagleQueue<AddFileItem> m_nagleQueue;

        private CancellationToken Token => m_cancellationSource.Token;

        private static IAppTraceSource Tracer => DropAppTraceSource.SingleInstance;

        private static CacheContext CacheContext => null; // not needed for anything but "get", which we don't do

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
        public VsoClient(IIpcLogger logger, DropDaemon dropDaemon)
        {
            Contract.Requires(dropDaemon?.DropConfig != null);

            m_logger = logger;
            m_dropDaemon = dropDaemon;
            m_config = dropDaemon.DropConfig;
            m_cancellationSource = new CancellationTokenSource();

            logger.Info("Using drop config: " + JsonConvert.SerializeObject(m_config));

            Stats = new DropStatistics();

            // instantiate drop client
            m_dropClient = new ReloadingDropServiceClient(
                logger: logger,
                clientConstructor: CreateDropServiceClient);

            m_nagleQueue = NagleQueue<AddFileItem>.Create(
                maxDegreeOfParallelism: m_config.MaxParallelUploads,
                batchSize: m_config.BatchSize,
                interval: m_config.NagleTime,
                processBatch: ProcessAddFilesAsync);

            if (m_config.ArtifactLogName != null)
            {
                DropAppTraceSource.SingleInstance.SetSourceLevel(System.Diagnostics.SourceLevels.Verbose);
                Tracer.AddFileTraceListener(Path.Combine(m_config.LogDir, m_config.ArtifactLogName));
            }
        }

        /// <summary>
        /// Takes a hash as string and registers its corresponding SHA-256 ContentHash using BuildXL Api.
        /// Should be called only when DropConfig.GenerateBuildManifest is true.
        /// Returns a hashset of failing RelativePaths.
        /// </summary>
        private async Task<HashSet<string>> RegisterFilesForBuildManifestAsync(BuildManifestEntry[] buildManifestEntries)
        {
            await Task.Yield();
            Contract.Requires(m_dropDaemon.DropConfig.GenerateBuildManifest, "RegisterFileForBuildManifest API called even though Build Manifest Generation is Disabled in DropConfig");
            var bxlResult = await m_dropDaemon.ApiClient.RegisterFilesForBuildManifest(m_dropDaemon.DropName, buildManifestEntries);
            if (!bxlResult.Succeeded)
            {
                m_logger.Verbose($"ApiClient.RegisterFileForBuildManifest unsuccessful. Failure: {bxlResult.Failure.DescribeIncludingInnerFailures()}");
                return new HashSet<string>(buildManifestEntries.Select(bme => bme.RelativePath));
            }

            if (bxlResult.Result.Length > 0)
            {
                m_logger.Verbose($"ApiClient.RegisterFileForBuildManifest found {bxlResult.Result.Length} file hashing failures.");
                return new HashSet<string>(bxlResult.Result.Select(bme => bme.RelativePath));
            }

            return new HashSet<string>();
        }

        /// <summary>
        ///     Invokes <see cref="IDropServiceClient.CreateAsync(string, bool, DateTime?, bool, CancellationToken)"/>.
        /// </summary>
        public async Task<DropItem> CreateAsync(CancellationToken token)
        {
            var startTime = DateTime.UtcNow;
            if (!m_config.DomainId.HasValue)
            {
                m_logger.Verbose("Domain ID is not specified. Creating drop using a default domain id.");
            }

            IDomainId domainId = m_config.DomainId.HasValue
                ? new ByteDomainId(m_config.DomainId.Value)
                : WellKnownDomainIds.DefaultDomainId;

            var result = await m_dropClient.CreateAsync(
                domainId,
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
            m_nagleQueue.Enqueue(addFileItem);

            var manifestResult = await addFileItem.BuildManifestTaskSource.Task;
            var dropResult = await addFileItem.DropResultTaskSource.Task;

            return manifestResult == RegisterFileForBuildManifestResult.Failed
                ? AddFileResult.RegisterFileForBuildManifestFailure
                : dropResult;
        }

        /// <summary>
        ///     Invokes <see cref="IDropServiceClient.FinalizeAsync"/>.
        /// </summary>
        public async Task<FinalizeResult> FinalizeAsync(CancellationToken token)
        {
            await m_nagleQueue.DisposeAsync();

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
            m_nagleQueue.Dispose();
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
        ///     Implements 'drop addfile' by first calling <see cref="IDropServiceClient.AssociateAsync"/>,
        ///     and then <see cref="IDropServiceClient.UploadAndAssociateAsync"/>.
        /// </summary>
        /// <remarks>
        ///     This method is called concurrently.
        /// </remarks>
        private async Task ProcessAddFilesAsync(AddFileItem[] batch)
        {
            if (batch.Length == 0)
            {
                return;
            }

            Interlocked.Increment(ref Stats.NumBatches);
            if (batch.Length == m_config.BatchSize)
            {
                Interlocked.Increment(ref Stats.NumCompleteBatches);
            }
            else
            {
                Interlocked.Increment(ref Stats.NumIncompleteBatches);
            }

            FileBlobDescriptor[] blobsForAssociate = new FileBlobDescriptor[0];
            try
            {
                var dedupedBatch = SkipFilesWithTheSameDropPathAndContent(batch);
                var numSkipped = batch.Length - dedupedBatch.Length;
                m_logger.Info("Processing a batch of {0} drop files after skipping {1} files.", dedupedBatch.Length, numSkipped);

                Task<HashSet<string>> registerFilesForBuildManifestTask = null;
                // Register files for Build Manifest
                if (m_dropDaemon.DropConfig.GenerateBuildManifest)
                {
                    BuildManifestEntry[] buildManifestEntries = dedupedBatch
                        .Where(dropItem => dropItem.BlobIdentifier != null)
                        .Select(dropItem => new BuildManifestEntry(dropItem.RelativeDropFilePath, dropItem.BlobIdentifier.ToContentHash(), dropItem.FullFilePath))
                        .ToArray();

                    if (buildManifestEntries.Length > 0) // dropItem.BlobIdentifier = null for files generated in the DropDaemon
                    {
                        registerFilesForBuildManifestTask = Task.Run(() => RegisterFilesForBuildManifestAsync(buildManifestEntries));
                    }
                }

                // compute blobs for associate
                var startTime = DateTime.UtcNow;
                blobsForAssociate = await Task.WhenAll(dedupedBatch.Select(item => item.FileBlobDescriptorForAssociateAsync(m_config.EnableChunkDedup, Token)));
                Interlocked.Add(ref Stats.TotalComputeFileBlobDescriptorForAssociateMs, ElapsedMillis(startTime));

                // run 'Associate' on all items from the batch; the result will indicate which files were associated and which need to be uploaded
                AssociationsStatus associateStatus = await AssociateAsync(blobsForAssociate);
                IReadOnlyList<AddFileItem> itemsLeftToUpload = await SetResultForAssociatedNonMissingItemsAsync(dedupedBatch, associateStatus, m_config.EnableChunkDedup, Token);

                // compute blobs for upload
                startTime = DateTime.UtcNow;
                FileBlobDescriptor[] blobsForUpload = await Task.WhenAll(itemsLeftToUpload.Select(item => item.FileBlobDescriptorForUploadAsync(m_config.EnableChunkDedup, Token)));
                Interlocked.Add(ref Stats.TotalComputeFileBlobDescriptorForUploadMs, ElapsedMillis(startTime));

                // run 'UploadAndAssociate' for the missing files.
                await UploadAndAssociateAsync(associateStatus, blobsForUpload);
                SetResultForUploadedMissingItems(itemsLeftToUpload);
                Interlocked.Add(ref Stats.TotalUploadSizeBytes, blobsForUpload.Sum(b => b.FileSize ?? 0));

                startTime = DateTime.UtcNow;

                foreach (var file in dedupedBatch)
                {
                    RegisterFileForBuildManifestResult result = registerFilesForBuildManifestTask == null
                        ? RegisterFileForBuildManifestResult.Skipped
                        : (await registerFilesForBuildManifestTask).Contains(file.RelativeDropFilePath)
                            ? RegisterFileForBuildManifestResult.Failed
                            : RegisterFileForBuildManifestResult.Registered;
                    file.BuildManifestTaskSource.TrySetResult(result);

                    if (result == RegisterFileForBuildManifestResult.Failed)
                    {
                        Interlocked.Increment(ref Stats.TotalBuildManifestRegistrationFailures);
                        m_logger.Info($"Build Manifest File registration failed for file at RelativePath '{file.RelativeDropFilePath}' with VSO '{file.BlobIdentifier.AlgorithmResultString}'.");
                    }
                }

                Interlocked.Add(ref Stats.TotalBuildManifestRegistrationDurationMs, ElapsedMillis(startTime));
                m_logger.Info("Done processing AddFile batch.");
            }
            catch (Exception e)
            {
                m_logger.Verbose($"Failed ProcessAddFilesAsync (batch size:{batch.Length}, blobsForAssociate size:{blobsForAssociate.Length}){Environment.NewLine}"
                    + string.Join(
                        Environment.NewLine,
                        batch.Select(item => $"'{item.FullFilePath}', '{item.RelativeDropFilePath}', BlobId:'{item.BlobIdentifier?.ToString() ?? ""}', Task.IsCompleted:{item.DropResultTaskSource.Task.IsCompleted}")));

                foreach (AddFileItem item in batch)
                {
                    if (!item.DropResultTaskSource.Task.IsCompleted)
                    {
                        item.DropResultTaskSource.SetException(e);
                    }

                    // No exceptions are thrown by RegisterFilesForBuildManifestAsync
                    item.BuildManifestTaskSource.TrySetResult(RegisterFileForBuildManifestResult.Skipped);
                }
            }
        }

        private AddFileItem[] SkipFilesWithTheSameDropPathAndContent(AddFileItem[] batch)
        {
            var dedupedItems = new Dictionary<string, AddFileItem>(capacity: batch.Length, comparer: StringComparer.OrdinalIgnoreCase);
            var numSkipped = 0;
            var numFailed = 0;
            foreach (var item in batch)
            {
                if (dedupedItems.TryGetValue(item.RelativeDropFilePath, out var existingItem))
                {
                    // Only skip an item if the content is known, it's the same content, and it's being uploaded to the same place
                    if (existingItem.BlobIdentifier != null && existingItem.BlobIdentifier == item.BlobIdentifier)
                    {
                        // the item won't be returned for further processing, so we need to mark its task complete
                        item.DropResultTaskSource.SetResult(AddFileResult.SkippedAsDuplicate);
                        item.BuildManifestTaskSource.SetResult(RegisterFileForBuildManifestResult.Skipped);
                        ++numSkipped;
                    }
                    else
                    {
                        item.DropResultTaskSource.SetException(new Exception($"An item with a drop path '{item.RelativeDropFilePath}' is already present -- existing content: '{existingItem?.BlobIdentifier?.ToContentHash()}', new content: '{item?.BlobIdentifier?.ToContentHash()}'"));
                        item.BuildManifestTaskSource.SetResult(RegisterFileForBuildManifestResult.Skipped);
                        ++numFailed;
                    }
                }
                else
                {
                    dedupedItems.Add(item.RelativeDropFilePath, item);
                }
            }

            if (batch.Length != numSkipped + numFailed + dedupedItems.Count)
            {
                Contract.Assert(false, $"batch_count ({batch.Length}) != num_skipped ({numSkipped}) + num_failed({numFailed}) + num_returned ({dedupedItems.Count})");
            }

            return dedupedItems.Values.ToArray();
        }

        private async Task<AssociationsStatus> AssociateAsync(FileBlobDescriptor[] blobsForAssociate)
        {
            m_logger.Info("Running associate on {0} files.", blobsForAssociate.Length);

            var startTime = DateTime.UtcNow;

            // m_dropClient.AssociateAsync does some internal batching. For each batch, it will create AssociationsStatus.
            // The very first created AssociationsStatus is stored and later returned as Item2 in the tuple.
            // Elements from all AssociationsStatus.Missing are added to the same IEnumerable<BlobIdentifier> and returned
            // as Item2 in the tuple.
            // If the method creates more than one batch (i.e., more than one AssociationsStatus is created), the returned
            // associateResult.Item1 will not match associateResult.Item2.Missing.
            Tuple<IEnumerable<BlobIdentifier>, AssociationsStatus> associateResult = await m_dropClient.AssociateAsync(
                DropName,
                blobsForAssociate.ToList(),
                abortIfAlreadyExists: false,
                cancellationToken: Token).ConfigureAwait(false);

            var result = associateResult.Item2;

            Interlocked.Add(ref Stats.TotalAssociateTimeMs, ElapsedMillis(startTime));

            var missingBlobIdsCount = associateResult.Item1.Count();
            var associationsStatusMissingBlobsCount = associateResult.Item2.Missing.Count();
            Interlocked.Add(ref Stats.NumFilesAssociated, blobsForAssociate.Length - missingBlobIdsCount);

            if (missingBlobIdsCount != associationsStatusMissingBlobsCount)
            {
                m_logger.Verbose("Mismatch in the number of missing files during Associate call -- missingBlobIdsCount={0}, associationsStatusMissingBlobsCount={1}",
                    missingBlobIdsCount,
                    associationsStatusMissingBlobsCount);

                if (missingBlobIdsCount < associationsStatusMissingBlobsCount)
                {
                    // This is an unexpected scenario. If there is a mismatch, count(associateResult.Item1) must be > count(associateResult.Item2.Missing).
                    Contract.Assert(false, "Unexpected mismatch in the number of missing files.");
                }

                // fix AssociationsStatus so it contains all the missing files. 
                result.Missing = associateResult.Item1;
            }

            return result;
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
                throw new DaemonException("This many hashes not found in blobs to upload: " + notFoundMissingHashes.Count());
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
                item.DropResultTaskSource.SetResult(AddFileResult.UploadedAndAssociated);
            }
        }

        /// <summary>
        ///     Sets completion results for <see cref="AddFileItem"/>s that were successfully associated
        ///     (as indicated by <paramref name="associateStatus"/>), and returns <see cref="AddFileItem"/>s
        ///     for those that are missing (<see cref="AssociationsStatus.Missing"/>).
        /// </summary>
        private async Task<IReadOnlyList<AddFileItem>> SetResultForAssociatedNonMissingItemsAsync(AddFileItem[] batch, AssociationsStatus associateStatus, bool chunkDedup, CancellationToken cancellationToken)
        {
            var missingItems = new List<AddFileItem>();
            var missingBlobIds = new HashSet<BlobIdentifier>(associateStatus.Missing);
            long totalSizeOfAssociatedFiles = 0;
            foreach (AddFileItem item in batch)
            {
                var itemFileBlobDescriptor = await item.FileBlobDescriptorForAssociateAsync(chunkDedup, cancellationToken);
                if (!missingBlobIds.Contains(itemFileBlobDescriptor.BlobIdentifier))
                {
                    item.DropResultTaskSource.SetResult(AddFileResult.Associated);
                    totalSizeOfAssociatedFiles += item.PrecomputedFileLength;
                }
                else
                {
                    missingItems.Add(item);
                }
            }

            Interlocked.Add(ref Stats.TotalAssociateSizeBytes, totalSizeOfAssociatedFiles);
            return missingItems;
        }

        /// <summary>
        ///     Dataflow descriptor for the 'addfile' job.
        /// </summary>
        internal sealed class AddFileItem
        {
            private readonly IDropItem m_dropItem;

            private FileBlobDescriptor m_fileBlobDescriptorForUpload;
            private FileBlobDescriptor m_fileBlobDescriptorForAssociate;

            internal TaskSourceSlim<AddFileResult> DropResultTaskSource { get; }

            internal TaskSourceSlim<RegisterFileForBuildManifestResult> BuildManifestTaskSource { get; }

            internal string FullFilePath => m_dropItem.FullFilePath;

            internal string RelativeDropFilePath => m_dropItem.RelativeDropPath;

            internal BlobIdentifier BlobIdentifier => m_dropItem.BlobIdentifier;

            /// <summary>
            /// Optional pre-computed file length. This field is set only for files that have known length
            /// and that originated from BuildXL. In all other cases the field is set to 0.
            /// </summary>
            internal long PrecomputedFileLength => m_dropItem.FileLength;

            internal AddFileItem(IDropItem item)
            {
                DropResultTaskSource = TaskSourceSlim.Create<AddFileResult>();
                m_dropItem = item;
                m_fileBlobDescriptorForUpload = null;
                m_fileBlobDescriptorForAssociate = null;
                BuildManifestTaskSource = TaskSourceSlim.Create<RegisterFileForBuildManifestResult>();
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
                        throw new DaemonException(I($"Blob identifier for file '{m_fileBlobDescriptorForUpload.AbsolutePath}' returned for 'UploadAndAssociate' ({m_fileBlobDescriptorForUpload.BlobIdentifier}) is different from the one returned for 'Associate' ({m_fileBlobDescriptorForAssociate.BlobIdentifier})."));
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
                        absolutePath: null); // If we pass it in, the client will actually try to use the file, and we cannot be sure that it has been materialized.
                }
                else
                {
                    // no blob identifier is provided by IDropItem ==> calculate and use full FileBlobDescriptor
                    return await FileBlobDescriptorForUploadAsync(chunkDedup, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Try to acquire credentials using the Azure Artifacts Helper first, if that fails then fallback to VsoCredentialHelper
        /// </summary>
        /// <returns>Credentials for PAT that was acquired.</returns>
        private VssCredentials GetCredentials()
        {
            Action<string> loggerAction = m => m_logger.Verbose(m);
            var adoCredentialHelper = new AzureArtifactsCredentialHelper(loggerAction);
            var credentialHelperResult = adoCredentialHelper.AcquirePat(m_config.Service, PatType.VstsDropReadWrite).Result;

            if (credentialHelperResult.Result == AzureArtifactsCredentialHelperResultType.Success)
            {
                return new VsoCredentialHelper(loggerAction).GetPATCredentials(credentialHelperResult.Pat);
            }

            return new VsoCredentialHelper(loggerAction).GetCredentials(serviceUri: m_config.Service, useAad: true, existingAadTokenCacheBytes: null, pat: null, promptBehavior: PromptBehavior.Never);
        }
    }
}
