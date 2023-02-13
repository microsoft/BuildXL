// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Authentication;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Microsoft.VisualStudio.Services.ArtifactServices.App.Shared.Cache;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common;
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
        private readonly IIpcLogger m_logger;
        private readonly DropConfig m_config;
        private readonly IDropServiceClient m_dropClient;
        private readonly CancellationTokenSource m_cancellationSource;
        private readonly Client m_bxlApiClient;
        private readonly NagleQueue<AddFileItem> m_nagleQueue;
        private readonly CounterCollection<DropClientCounter> m_counters;

        /// <summary>
        /// Won't be set for clients running on workers because workers don't create drops
        /// </summary>
        private DateTime? m_dropCreatedAtUtc;

        /// <summary>
        /// Won't be set for clients running on workers because workers don't finalize drops
        /// </summary>
        private DateTime? m_dropFinalizedAtUtc;

        private CancellationToken Token => m_cancellationSource.Token;

        private static IAppTraceSource Tracer => DropAppTraceSource.SingleInstance;

        private static CacheContextBase CacheContext => null; // not needed for anything but "get", which we don't do

        private readonly VssCredentialsFactory m_credentialFactory;

        private ArtifactHttpClientFactory GetFactory() =>
            new ArtifactHttpClientFactory(
                credentials: GetCredentials(),
                httpSendTimeout: m_config.HttpSendTimeout,
                tracer: Tracer,
                verifyConnectionCancellationToken: Token);

        /// <nodoc/>
        public Uri ServiceEndpoint => m_config.Service;

        /// <nodoc/>
        public string DropName => m_config.Name;

        /// <inheritdoc />
        public string DropUrl => ServiceEndpoint + "/_apis/drop/drops/" + DropName;

        /// <inheritdoc />
        public bool AttemptedFinalization { get; private set; }

        /// <nodoc />
        public VsoClient(IIpcLogger logger, Client bxlApiClient, DaemonConfig daemonConfig, DropConfig dropConfig)
        {
            Contract.Requires(daemonConfig != null);
            Contract.Requires(dropConfig != null);

            m_logger = logger;
            m_config = dropConfig;
            m_bxlApiClient = bxlApiClient;

            m_cancellationSource = new CancellationTokenSource();

            logger.Info("Using drop config: " + JsonConvert.SerializeObject(m_config));

            m_counters = new();

            string pat = !string.IsNullOrEmpty(dropConfig.PersonalAccessTokenEnv) ? Environment.GetEnvironmentVariable(dropConfig.PersonalAccessTokenEnv) : null;
            pat = string.IsNullOrWhiteSpace(pat) ? null : pat;
            m_credentialFactory = new VssCredentialsFactory(pat, new CredentialProviderHelper(m => m_logger.Verbose(m)), m => m_logger.Verbose(m));

            // instantiate drop client
            m_dropClient = new ReloadingDropServiceClient(
                logger: logger,
                clientConstructor: CreateDropServiceClient);

            m_nagleQueue = NagleQueue<AddFileItem>.Create(
                maxDegreeOfParallelism: m_config.MaxParallelUploads,
                batchSize: m_config.BatchSize,
                interval: m_config.NagleTime,
                processBatch: ProcessAddFilesAsync);
        }

        /// <summary>
        /// Takes a hash as string and registers its corresponding SHA-256 ContentHash using BuildXL Api.
        /// Should be called only when DropConfig.GenerateBuildManifest is true.
        /// Returns a hashset of failing RelativePaths.
        /// </summary>
        private async Task<HashSet<string>> RegisterFilesForBuildManifestAsync(BuildManifestEntry[] buildManifestEntries)
        {
            Contract.Requires(m_config.GenerateBuildManifest, "RegisterFileForBuildManifest API called even though Build Manifest Generation is Disabled in DropConfig");
            var bxlResult = await m_bxlApiClient.RegisterFilesForBuildManifest(DropDaemon.FullyQualifiedDropName(m_config), buildManifestEntries);
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
            using (m_counters.StartStopwatch(DropClientCounter.CreateTime))
            {
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

                m_dropCreatedAtUtc = DateTime.UtcNow;
                return result;
            }
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

            m_counters.IncrementCounter(DropClientCounter.NumberOfAddFileRequests);

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
            AttemptedFinalization = true;
            await m_nagleQueue.DisposeAsync();

            using (m_counters.StartStopwatch(DropClientCounter.FinalizeTime))
            {
                await m_dropClient.FinalizeAsync(DropName, token);
            }

            m_dropFinalizedAtUtc = DateTime.UtcNow;

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
        public IDictionary<string, long> GetStats(bool reportSizeInMegabytes)
        {
            const string StatsPrefix = "DropDaemon";

            var stats = m_counters.AsStatistics(StatsPrefix);
            if (reportSizeInMegabytes)
            {
                // Although we always track the size of associated / uploaded files in bytes, we used
                // to report these two metrics in megabytes. For backward compatibility, keep an option
                // to report size in megabytes.
                convertCounterToMb(stats, DropClientCounter.TotalAssociateSizeBytes, "TotalAssociateSizeMb");
                convertCounterToMb(stats, DropClientCounter.TotalUploadSizeBytes, "TotalUploadSizeMb");
            }

            return stats;

            static void convertCounterToMb(Dictionary<string, long> counters, DropClientCounter originalCounter, string newCounterName)
            {
                var oldKey = $"{StatsPrefix}.{originalCounter}";
                var newKey = $"{StatsPrefix}.{newCounterName}";
                counters.Add(newKey, counters[oldKey] >> 20);
                counters.Remove(oldKey);
            }
        }

        internal async Task<Possible<bool>> ReportDropTelemetryDataAsync(string daemonName)
        {
            if (!m_config.ReportTelemetry)
            {
                return true;
            }

            Contract.Requires(m_bxlApiClient != null);

            var dropInfo = new Dictionary<string, string>
            {
                { addPrefix("Endpoint"), ServiceEndpoint.ToString() },
                { addPrefix("DropName"), DropName},
            };
            
            if (m_dropCreatedAtUtc.HasValue)
            {
                dropInfo.Add(addPrefix("CreatedAtUtc"), m_dropCreatedAtUtc.Value.ToString("s", CultureInfo.InvariantCulture));
            }

            if (m_dropFinalizedAtUtc.HasValue)
            {
                dropInfo.Add(addPrefix("FinalizedAtUtc"), m_dropFinalizedAtUtc.Value.ToString("s", CultureInfo.InvariantCulture));
            }

            string serializedDropInfo = JsonConvert.SerializeObject(dropInfo, Formatting.Indented);
            string serializedDropStats = JsonConvert.SerializeObject(GetStats(reportSizeInMegabytes: false), Formatting.Indented);

            m_logger.Info("Reporting telemetry to BuildXL");
            m_logger.Info($"Info:{Environment.NewLine}{serializedDropInfo}");
            m_logger.Info($"Statistics:{Environment.NewLine}{serializedDropStats}");

            return await m_bxlApiClient.ReportDaemonTelemetry(daemonName, serializedDropStats, serializedDropInfo);
            
            static string addPrefix(string name) => $"DropDaemon.{name}";
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
            using (m_counters.StartStopwatch(DropClientCounter.AuthTime))
            {
                var client = new DropServiceClient(
                    ServiceEndpoint,
                    GetFactory(),
                    CacheContext,
                    new DropClientTelemetry(ServiceEndpoint, Tracer, enable: m_config.EnableTelemetry),
                    Tracer);

                return client;
            }
        }

        /// <summary>
        ///     Implements 'drop addfile' by first calling <see cref="IDropServiceClient.AssociateAsync"/>,
        ///     and then <see cref="IDropServiceClient.UploadAndAssociateAsync"/>.
        /// </summary>
        /// <remarks>
        ///     This method is called concurrently.
        /// </remarks>
        private async Task ProcessAddFilesAsync(List<AddFileItem> batch)
        {
            if (batch.Count == 0)
            {
                return;
            }

            m_counters.IncrementCounter(DropClientCounter.NumberOfBatches);
            if (batch.Count == m_config.BatchSize)
            {
                m_counters.IncrementCounter(DropClientCounter.NumberOfCompleteBatches);
            }
            else
            {
                m_counters.IncrementCounter(DropClientCounter.NumberOfIncompleteBatches);
            }

            FileBlobDescriptor[] blobsForAssociate = new FileBlobDescriptor[0];
            try
            {
                var dedupedBatch = SkipFilesWithTheSameDropPathAndContent(batch);
                var numSkipped = batch.Count - dedupedBatch.Length;
                m_logger.Info("Processing a batch of {0} drop files after skipping {1} files.", dedupedBatch.Length, numSkipped);

                Task<HashSet<string>> registerFilesForBuildManifestTask = null;
                // Register files for Build Manifest
                if (m_config.GenerateBuildManifest)
                {
                    BuildManifestEntry[] buildManifestEntries = dedupedBatch
                        .Where(dropItem => dropItem.BlobIdentifier != null && dropItem.FileId.HasValue)
                        .Select(dropItem => new BuildManifestEntry(dropItem.RelativeDropFilePath, dropItem.BlobIdentifier.ToContentHash(), dropItem.FullFilePath, dropItem.FileId.Value))
                        .ToArray();

                    if (buildManifestEntries.Length > 0) // dropItem.BlobIdentifier = null for files generated in the DropDaemon
                    {
                        registerFilesForBuildManifestTask = Task.Run(() => RegisterFilesForBuildManifestAsync(buildManifestEntries));
                    }
                }

                // compute blobs for associate
                using (m_counters.StartStopwatch(DropClientCounter.TotalComputeFileBlobDescriptorForAssociate))
                {
                    blobsForAssociate = await Task.WhenAll(dedupedBatch.Select(item => item.FileBlobDescriptorForAssociateAsync(m_config.EnableChunkDedup, Token)));
                }

                // run 'Associate' on all items from the batch; the result will indicate which files were associated and which need to be uploaded
                AssociationsStatus associateStatus = await AssociateAsync(blobsForAssociate);
                IReadOnlyList<AddFileItem> itemsLeftToUpload = await SetResultForAssociatedNonMissingItemsAsync(dedupedBatch, associateStatus, m_config.EnableChunkDedup, Token);

                // compute blobs for upload
                FileBlobDescriptor[] blobsForUpload;
                using (m_counters.StartStopwatch(DropClientCounter.TotalComputeFileBlobDescriptorForUpload))
                {
                    blobsForUpload = await Task.WhenAll(itemsLeftToUpload.Select(item => item.FileBlobDescriptorForUploadAsync(m_config.EnableChunkDedup, Token)));
                }

                // run 'UploadAndAssociate' for the missing files.
                await UploadAndAssociateAsync(associateStatus, blobsForUpload);
                SetResultForUploadedMissingItems(itemsLeftToUpload);
                m_counters.AddToCounter(DropClientCounter.TotalUploadSizeBytes, blobsForUpload.Sum(b => b.FileSize ?? 0));

                using (m_counters.StartStopwatch(DropClientCounter.TotalBuildManifestRegistrationDuration))
                {
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
                            m_counters.IncrementCounter(DropClientCounter.TotalBuildManifestRegistrationFailures);
                            m_logger.Info($"Build Manifest File registration failed for file at RelativePath '{file.RelativeDropFilePath}' with VSO '{file.BlobIdentifier.AlgorithmResultString}'.");
                        }
                    }
                }

                m_logger.Info("Done processing AddFile batch.");
            }
            catch (Exception e)
            {
                m_logger.Verbose($"Failed ProcessAddFilesAsync (batch size:{batch.Count}, blobsForAssociate size:{blobsForAssociate.Length}){Environment.NewLine}"
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

        private AddFileItem[] SkipFilesWithTheSameDropPathAndContent(List<AddFileItem> batch)
        {
            var dedupedItems = new Dictionary<string, AddFileItem>(capacity: batch.Count, comparer: StringComparer.OrdinalIgnoreCase);
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

            if (batch.Count != numSkipped + numFailed + dedupedItems.Count)
            {
                Contract.Assert(false, $"batch_count ({batch.Count}) != num_skipped ({numSkipped}) + num_failed({numFailed}) + num_returned ({dedupedItems.Count})");
            }

            return dedupedItems.Values.ToArray();
        }

        private async Task<AssociationsStatus> AssociateAsync(FileBlobDescriptor[] blobsForAssociate)
        {
            m_logger.Info("Running associate on {0} files.", blobsForAssociate.Length);

            Tuple<IEnumerable<BlobIdentifier>, AssociationsStatus> associateResult;
            using (m_counters.StartStopwatch(DropClientCounter.TotalAssociateTime))
            {
                // m_dropClient.AssociateAsync does some internal batching. For each batch, it will create AssociationsStatus.
                // The very first created AssociationsStatus is stored and later returned as Item2 in the tuple.
                // Elements from all AssociationsStatus.Missing are added to the same IEnumerable<BlobIdentifier> and returned
                // as Item2 in the tuple.
                // If the method creates more than one batch (i.e., more than one AssociationsStatus is created), the returned
                // associateResult.Item1 will not match associateResult.Item2.Missing.
                associateResult = await m_dropClient.AssociateAsync(
                    DropName,
                    blobsForAssociate.ToList(),
                    abortIfAlreadyExists: false,
                    cancellationToken: Token).ConfigureAwait(false);
            }

            var result = associateResult.Item2;
            var missingBlobIdsCount = associateResult.Item1.Count();
            var associationsStatusMissingBlobsCount = associateResult.Item2.Missing.Count();
            m_counters.AddToCounter(DropClientCounter.NumberOfFilesAssociated, blobsForAssociate.Length - missingBlobIdsCount);

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

            using (m_counters.StartStopwatch(DropClientCounter.TotalUploadTime))
            {
                await m_dropClient.UploadAndAssociateAsync(
                    DropName,
                    blobsForUpload.ToList(),
                    abortIfAlreadyExists: false,
                    firstAssociationStatus: associateStatus,
                    cancellationToken: Token).ConfigureAwait(false);
            }

            m_counters.AddToCounter(DropClientCounter.NumberOfFilesUploaded, numMissing);
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

            m_counters.AddToCounter(DropClientCounter.TotalAssociateSizeBytes, totalSizeOfAssociatedFiles);

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

            internal FileArtifact? FileId => m_dropItem.Artifact;

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
            return m_credentialFactory.GetOrCreateVssCredentialsAsync(m_config.Service, useAad: true, PatType.VstsDropReadWrite)
                .GetAwaiter()
                .GetResult();
        }

        private enum DropClientCounter
        {
            /// <summary>
            /// Time taken to authenticate
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            AuthTime,

            /// <summary>
            /// Time taken to complete 'drop create'
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            CreateTime,

            /// <summary>
            /// Time taken to complete 'drop finalize'
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            FinalizeTime,

            /// <summary>
            /// Total number of 'addfile' requests
            /// </summary>
            NumberOfAddFileRequests,

            /// <summary>
            /// Number of 'addfile' processing batches
            /// </summary>
            NumberOfBatches,

            NumberOfCompleteBatches,

            NumberOfIncompleteBatches,

            /// <summary>
            /// Number of files 'associated' (found in the drop remote store, so didn't need to be uploaded to drop)
            /// </summary>
            NumberOfFilesAssociated,

            /// <summary>
            /// Total size of all associated files in bytes (note that these files were not actually uploaded, i.e., they were not transfered across the network)
            /// </summary>
            TotalAssociateSizeBytes,

            /// <summary>
            /// Total time taken to complete all 'Associate' calls to ArtifactServices drop
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            TotalAssociateTime,

            /// <summary>
            /// Number of files 'uploaded' (were not already found in the drop remote store)
            /// </summary>
            NumberOfFilesUploaded,

            /// <summary>
            /// Total size of all uploaded files in bytes (note that this is not the same as the size of the drop, since it doesn't include files that were only associated).
            /// </summary>
            TotalUploadSizeBytes,

            /// <summary>
            /// Total time taken to complete all 'UploadAndAssociate' calls to ArtifactServices drop
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            TotalUploadTime,

            /// <summary>
            /// Total time spent in Build Manifest Registrations during 'UploadAndAssociate' calls
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            TotalBuildManifestRegistrationDuration,

            /// <summary>
            /// Total number of failures encountered in Build Manifest Registrations during 'UploadAndAssociate' calls
            /// </summary>
            TotalBuildManifestRegistrationFailures,

            /// <summary>
            /// Total time taken to compute FileBlobDescriptors for 'Associate' calls
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            TotalComputeFileBlobDescriptorForAssociate,

            /// <summary>
            /// Total time taken to compute FileBlobDescriptors for 'UploadAndAssociate' calls
            /// </summary>
            [CounterType(CounterType.Stopwatch)]
            TotalComputeFileBlobDescriptorForUpload
        }
    }
}