// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
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
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Authentication;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.Symbol.App.Core;
using Microsoft.VisualStudio.Services.Symbol.App.Core.Telemetry;
using Microsoft.VisualStudio.Services.Symbol.App.Core.Tracing;
using Microsoft.VisualStudio.Services.Symbol.Common;
using Microsoft.VisualStudio.Services.Symbol.WebApi;
using Newtonsoft.Json;
using Tool.ServicePipDaemon;
using static BuildXL.Utilities.FormattableStringEx;

namespace Tool.SymbolDaemon
{
    /// <summary>
    /// Client for Artifact Services symbols endpoint.
    /// </summary>
    public sealed class VsoSymbolClient : ISymbolClient
    {
        private static IAppTraceSource Tracer => SymbolAppTraceSource.SingleInstance;

        private readonly Client m_apiClient;

        private readonly IIpcLogger m_logger;
        private readonly SymbolConfig m_config;
        private readonly ISymbolServiceClient m_symbolClient;
        private readonly CancellationTokenSource m_cancellationSource;
        private readonly DebugEntryCreateBehavior m_debugEntryCreateBehavior;

        private CancellationToken CancellationToken => m_cancellationSource.Token;
        private string m_requestId;
        private IDomainId m_domainId;
        private readonly SemaphoreSlim m_requestIdAcquisitionMutex = TaskUtilities.CreateMutex();
        private readonly NagleQueue<BatchedSymbolFile> m_nagleQueue;
        private readonly ActionQueue m_fileUploadQueue;
        private int m_batchCount;

        private readonly VssCredentialsFactory m_credentialFactory;

        private ArtifactHttpClientFactory GetFactory() =>
            new ArtifactHttpClientFactory(
                credentials: GetCredentials(),
                httpSendTimeout: m_config.HttpSendTimeout,
                tracer: Tracer,
                verifyConnectionCancellationToken: CancellationToken);

        private Uri ServiceEndpoint => m_config.Service;

        internal string RequestName => m_config.Name;

        private string RequestId
        {
            get
            {
                Contract.Requires(!string.IsNullOrEmpty(m_requestId));
                return m_requestId;
            }
        }

        private IDomainId DomainId
        {
            get
            {
                Contract.Requires(m_domainId != null);
                return m_domainId;
            }
        }

        private readonly CounterCollection<SymbolClientCounter> m_counters;

        /// <nodoc />
        public VsoSymbolClient(IIpcLogger logger, SymbolConfig config, Client apiClient)
        {
            m_logger = logger;
            m_apiClient = apiClient;
            m_config = config;
            m_debugEntryCreateBehavior = config.DebugEntryCreateBehavior;
            m_cancellationSource = new CancellationTokenSource();

            m_counters = new CounterCollection<SymbolClientCounter>();

            m_logger.Info(I($"[{nameof(VsoSymbolClient)}] Using symbol config: {JsonConvert.SerializeObject(m_config)}"));

            m_credentialFactory = new VssCredentialsFactory(pat: null, new CredentialProviderHelper(m => m_logger.Verbose(m)), m => m_logger.Verbose(m));

            m_symbolClient = new ReloadingSymbolClient(
                logger: logger,
                clientConstructor: CreateSymbolServiceClient);

            m_nagleQueue = NagleQueue<BatchedSymbolFile>.Create(
                maxDegreeOfParallelism: m_config.MaxParallelUploads,
                batchSize: m_config.BatchSize,
                interval: m_config.NagleTime,
                processBatch: ProcessBatchedFilesAsync);

            m_fileUploadQueue = new ActionQueue(m_config.MaxParallelUploads);
        }

        private ISymbolServiceClient CreateSymbolServiceClient()
        {
            using (m_counters.StartStopwatch(SymbolClientCounter.AuthDuration))
            {
                var client = new SymbolServiceClient(
                    ServiceEndpoint,
                    GetFactory(),
                    Tracer,
                    new SymbolServiceClientTelemetry(Tracer, ServiceEndpoint, enable: m_config.EnableTelemetry));

                return client;
            }
        }

        /// <summary>
        /// Queries the endpoint for the RequestId. 
        /// This method should be called only after the request has been created, otherwise, it will throw an exception.
        /// </summary>
        /// <remarks>
        /// On workers, m_requestId / m_domainId won't be initialized, so we need to query the server for the right values.
        /// </remarks>
        private async Task EnsureRequestIdAndDomainIdAreInitalizedAsync()
        {
            // Check whether the field is initialized, so we are not wastefully acquire the semaphore.
            if (string.IsNullOrEmpty(m_requestId))
            {
                using (await m_requestIdAcquisitionMutex.AcquireAsync())
                {
                    // check whether we still need to query the server
                    if (string.IsNullOrEmpty(m_requestId) || m_domainId == null)
                    {
                        using (m_counters.StartStopwatch(SymbolClientCounter.GetRequestIdDuration))
                        {
                            var result = await m_symbolClient.GetRequestByNameAsync(RequestName, CancellationToken);
                            m_requestId = result.Id;
                            m_domainId = result.DomainId;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Creates a symbol request.
        /// </summary>
        public async Task<Request> CreateAsync(CancellationToken token)
        {
            if (!m_config.DomainId.HasValue)
            {
                m_logger.Verbose("DomainId is not specified. Creating symbol publishing request using DefaultDomainId.");
            }

            IDomainId domainId = m_config.DomainId.HasValue
                ? new ByteDomainId(m_config.DomainId.Value)
                : WellKnownDomainIds.DefaultDomainId;

            Request result;
            using (m_counters.StartStopwatch(SymbolClientCounter.CreateDuration))
            {
                result = await m_symbolClient.CreateRequestAsync(domainId, RequestName, m_config.EnableChunkDedup, token);
            }

            m_requestId = result.Id;
            m_domainId = result.DomainId;

            // info about a request in a human-readable form
            var requestDetails = $"Symbol request has been created:{Environment.NewLine}"
                + $"ID: {result.Id}{Environment.NewLine}"
                + $"Name: {result.Name}{Environment.NewLine}"
                + $"Content list: '{result.Url}/DebugEntries'";

            // Send the message to the main log.
            Analysis.IgnoreResult(await m_apiClient.LogMessage(requestDetails));

            m_logger.Verbose(requestDetails);

            return result;
        }

        /// <inheritdoc />
        public Task<Request> CreateAsync() => CreateAsync(CancellationToken);

        /// <inheritdoc />
        public async Task<AddDebugEntryResult> AddFileAsync(SymbolFile symbolFile)
        {
            Contract.Requires(symbolFile.IsIndexed, "File has not been indexed.");

            m_counters.IncrementCounter(SymbolClientCounter.NumAddFileRequests);
            if (symbolFile.DebugEntries.Count == 0)
            {
                // If there are no debug entries, ask bxl to log a message and return early.
                Analysis.IgnoreResult(await m_apiClient.LogMessage(I($"File '{symbolFile.FullFilePath}' does not contain symbols and will not be added to '{RequestName}'."), isWarning: false));
                m_counters.IncrementCounter(SymbolClientCounter.NumFilesWithoutDebugEntries);

                return AddDebugEntryResult.NoSymbolData;
            }

            m_logger.Verbose($"Queued file '{symbolFile}'");
            var batchedFile = new BatchedSymbolFile(symbolFile);
            m_nagleQueue.Enqueue(batchedFile);
            return await batchedFile.ResultTaskSource.Task;
        }

        private async Task ProcessBatchedFilesAsync(BatchedSymbolFile[] batch)
        {
            var batchNumber = Interlocked.Increment(ref m_batchCount);

            try
            {
                m_logger.Info($"Started processing batch #{batchNumber} ({batch.Length} files).");

                await EnsureRequestIdAndDomainIdAreInitalizedAsync();

                var debugEntriesToAssociate = batch.SelectMany(
                    file => file.File.DebugEntries.Select(entry => CreateDebugEntry(entry, DomainId)))
                    .ToList();


                var resultOfAssociateCall = await AssociateAsync(debugEntriesToAssociate);
                var entriesWithMissingBlobs = resultOfAssociateCall.Where(e => e.Status == DebugEntryStatus.BlobMissing).ToList();
                var missingBlobsToFilesMap = SetResultForAssociatedFiles(batch, entriesWithMissingBlobs);

                // materialize all files that we need to upload
                var materializedFiles = await TaskUtilities.SafeWhenAll(missingBlobsToFilesMap.Values.Select(static file => file.File.EnsureMaterializedAsync()));

                await UploadAndAssociateAsync(entriesWithMissingBlobs, missingBlobsToFilesMap);

                m_counters.AddToCounter(SymbolClientCounter.NumFilesUploaded, missingBlobsToFilesMap.Count);
                m_counters.AddToCounter(SymbolClientCounter.TotalUploadSize, materializedFiles.Sum(f => f.Length));

                missingBlobsToFilesMap.Values.ForEach(static file => file.ResultTaskSource.TrySetResult(AddDebugEntryResult.UploadedAndAssociated));

                // double-check that all files were processed
                Contract.Assert(batch.All(f => f.ResultTaskSource.Task.IsCompleted));

                m_logger.Info($"Finished processing batch #{batchNumber}.");
            }
            catch (Exception e)
            {
                m_logger.Verbose($"Failed ProcessBatchedFilesAsync (batch #{batchNumber}, size:{batch.Length}){Environment.NewLine}"
                   + string.Join(
                       Environment.NewLine,
                       batch.Select(item => $"'{item.File.FullFilePath}', Hash:'{item.File.Hash}', DebugEntries.Count: {item.File.DebugEntries.Count}, Task.IsCompleted:{item.ResultTaskSource.Task.IsCompleted}")));

                batch.ForEach(f => f.ResultTaskSource.TrySetException(e));
            }
        }

        private async Task<List<DebugEntry>> AssociateAsync(List<DebugEntry> debugEntriesToAssociate)
        {
            List<DebugEntry> result;
            using (m_counters.StartStopwatch(SymbolClientCounter.TotalAssociateTime))
            {
                try
                {
                    result = await m_symbolClient.CreateRequestDebugEntriesAsync(
                        RequestId,
                        debugEntriesToAssociate,
                        // First, we create debug entries with ThrowIfExists behavior not to silence the collision errors.
                        DebugEntryCreateBehavior.ThrowIfExists,
                        CancellationToken);
                }
                catch (DebugEntryExistsException e)
                {
                    if (m_debugEntryCreateBehavior == DebugEntryCreateBehavior.ThrowIfExists)
                    {
                        // The daemon is configured to throw on a collision.
                        throw;
                    }

                    string message = "A collision has occurred while processing a batch. "
                        + $"SymbolDaemon will retry creating debug entries with {m_debugEntryCreateBehavior} behavior. "
                        + $"{Environment.NewLine}{e.Message}";

                    // Log a message in SymbolDaemon log file
                    m_logger.Verbose(message);

                    result = await m_symbolClient.CreateRequestDebugEntriesAsync(
                        RequestId,
                        debugEntriesToAssociate,
                        m_debugEntryCreateBehavior,
                        CancellationToken);
                }
            }

            return result;
        }

        private Dictionary<BlobIdentifier, BatchedSymbolFile> SetResultForAssociatedFiles(BatchedSymbolFile[] batch, List<DebugEntry> entriesWithMissingBlobs)
        {
            // A single file might contain multiple DebugEntries, however, all them will share the same BlobIdentifier.
            // Because of that, we only need to check the BlobIdentifier of the first entry for each file.
            // This method is also constructing blobId -> file map, that is later used when uploading files.

            var missingBlobIds = new HashSet<BlobIdentifier>(entriesWithMissingBlobs.Select(e => e.BlobIdentifier));
            var blobIdToFileMap = new Dictionary<BlobIdentifier, BatchedSymbolFile>();
            foreach (var file in batch)
            {
                // DebugEntries list is always non-empty at this point
                if (missingBlobIds.Contains(file.File.DebugEntries[0].BlobIdentifier))
                {
                    // It is possible that a batch contains multiple files with the same content (same blobId). In such a case, 
                    // DebugEntries might or might not be the same. Since Upload call is isolated from the Associate call,
                    // and the only thing needed for upload is content's location, add the first file to the map and mark
                    // other files as Associated (the entries from those files will still be a part of the second Associate call). 
                    if (!blobIdToFileMap.ContainsKey(file.File.DebugEntries[0].BlobIdentifier))
                    {
                        blobIdToFileMap.Add(file.File.DebugEntries[0].BlobIdentifier, file);
                    }
                    else
                    {
                        file.ResultTaskSource.TrySetResult(AddDebugEntryResult.Associated);
                        m_counters.IncrementCounter(SymbolClientCounter.NumFilesAssociated);
                        m_counters.AddToCounter(SymbolClientCounter.TotalAssociateSize, file.File.FileLength);
                    }
                }
                else
                {
                    file.ResultTaskSource.TrySetResult(AddDebugEntryResult.Associated);
                    m_counters.IncrementCounter(SymbolClientCounter.NumFilesAssociated);
                }
            }

            return blobIdToFileMap;
        }

        private async Task UploadAndAssociateAsync(List<DebugEntry> entriesWithMissingBlobs, Dictionary<BlobIdentifier, BatchedSymbolFile> missingBlobsToFilesMap)
        {
            if (entriesWithMissingBlobs.Count == 0)
            {
                return;
            }

            // We need to upload each missing file only once, however, to upload a file, we need data from DebugEntry returned
            // by the service endpoint during the associate call. EntriesWithMissingBlobs might contain entries that are linked
            // to the same file, so we need dedup that list first.
            await m_fileUploadQueue.ForEachAsync(
                entriesWithMissingBlobs.Distinct(DebugEntryBlobIdComparer.Instance),
                async (entry, index) =>
                {
                    using (m_counters.StartStopwatch(SymbolClientCounter.TotalUploadTime))
                    {
                        var batchedFile = missingBlobsToFilesMap[entry.BlobIdentifier];
                        var uploadResult = await m_symbolClient.UploadFileAsync(
                            entry.DomainId,
                            // uploading to the location set by the symbol service
                            entry.BlobUri,
                            RequestId,
                            batchedFile.File.FullFilePath,
                            entry.BlobIdentifier,
                            CancellationToken);
                        batchedFile.SetBlobIdentifier(uploadResult);
                    }
                });

            // need to update entries before calling associate the second time
            entriesWithMissingBlobs.ForEach(entry => missingBlobsToFilesMap[entry.BlobIdentifier].BlobIdentifier.UpdateDebugEntryBlobReference(entry));

            using (m_counters.StartStopwatch(SymbolClientCounter.TotalAssociateAfterUploadTime))
            {
                entriesWithMissingBlobs = await m_symbolClient.CreateRequestDebugEntriesAsync(
                    RequestId,
                    entriesWithMissingBlobs,
                    m_debugEntryCreateBehavior,
                    CancellationToken);
            }

            Contract.Assert(entriesWithMissingBlobs.All(e => e.Status != DebugEntryStatus.BlobMissing), "Entries with non-success code are present.");
        }

        /// <summary>
        /// Finalizes a symbol request.
        /// </summary>        
        public async Task<Request> FinalizeAsync(CancellationToken token)
        {
            await m_nagleQueue.DisposeAsync();

            using (m_counters.StartStopwatch(SymbolClientCounter.FinalizeDuration))
            {
                var result = await m_symbolClient.FinalizeRequestAsync(
                    RequestId,
                    ComputeExpirationDate(m_config.Retention),
                    // isUpdateOperation == true => request will be marked as 'Sealed', 
                    // i.e., no more DebugEntries could be added to it 
                    isUpdateOperation: false,
                    token);

                return result;
            }
        }

        /// <inheritdoc />
        public Task<Request> FinalizeAsync() => FinalizeAsync(CancellationToken);

        private DateTime ComputeExpirationDate(TimeSpan retention)
        {
            return DateTime.UtcNow.Add(retention);
        }

        /// <nodoc />
        public void Dispose()
        {
            m_nagleQueue.Dispose();
            m_symbolClient.Dispose();
        }

        private static DebugEntry CreateDebugEntry(IDebugEntryData data, IDomainId domainId)
        {
            return new DebugEntry()
            {
                BlobIdentifier = data.BlobIdentifier,
                ClientKey = data.ClientKey,
                InformationLevel = data.InformationLevel,
                DomainId = domainId,
            };
        }

        /// <inheritdoc />
        public IDictionary<string, long> GetStats()
        {
            return m_counters.AsStatistics("SymbolDaemon");
        }

        internal async Task<Possible<bool>> ReportSymbolTelemetryDataAsync(string daemonName)
        {
            if (!m_config.ReportTelemetry)
            {
                return true;
            }

            Contract.Requires(m_apiClient != null);

            var telemetry = new Dictionary<string, string>
            {
                { addPrefix("Endpoint"), ServiceEndpoint.ToString() },
                { addPrefix("RequestName"), RequestName },
                { addPrefix("RequestId"), RequestId }
            };

            string serializedSymbolInfo = JsonConvert.SerializeObject(telemetry, Formatting.Indented);
            string serializedSymbolStats = JsonConvert.SerializeObject(GetStats(), Formatting.Indented);

            m_logger.Info("Reporting telemetry to BuildXL");
            m_logger.Info($"Info:{Environment.NewLine}{serializedSymbolInfo}");
            m_logger.Info($"Statistics:{Environment.NewLine}{serializedSymbolStats}");

            return await m_apiClient.ReportDaemonTelemetry(daemonName, serializedSymbolStats, serializedSymbolInfo);

            static string addPrefix(string name) => $"SymbolDaemon.{name}";
        }

        private enum SymbolClientCounter
        {
            [CounterType(CounterType.Stopwatch)]
            AuthDuration,

            [CounterType(CounterType.Stopwatch)]
            GetRequestIdDuration,

            [CounterType(CounterType.Stopwatch)]
            CreateDuration,

            [CounterType(CounterType.Stopwatch)]
            FinalizeDuration,

            [CounterType(CounterType.Stopwatch)]
            TotalAssociateTime,

            [CounterType(CounterType.Stopwatch)]
            TotalAssociateAfterUploadTime,

            [CounterType(CounterType.Stopwatch)]
            TotalUploadTime,

            NumAddFileRequests,

            NumFilesWithoutDebugEntries,

            NumFilesAssociated,

            NumFilesUploaded,

            TotalAssociateSize,

            TotalUploadSize,
        }

        /// <summary>
        /// Try to acquire credentials using the Azure Artifacts Helper first, if that fails then fallback to VsoCredentialHelper
        /// </summary>
        /// <returns>Credentials for PAT that was acquired.</returns>
        private VssCredentials GetCredentials()
        {
            return m_credentialFactory.GetOrCreateVssCredentialsAsync(m_config.Service, useAad: true, PatType.SymbolsReadWrite)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// A wrapper of <see cref="SymbolFile"/> for batched processing.
        /// </summary>
        private sealed class BatchedSymbolFile
        {
            private readonly object m_lock = new object();

            public SymbolFile File { get; }
            public TaskSourceSlim<AddDebugEntryResult> ResultTaskSource { get; }
            public SymbolBlobIdentifier BlobIdentifier { get; private set; }

            public BatchedSymbolFile(SymbolFile file)
            {
                File = file;
                ResultTaskSource = TaskSourceSlim.Create<AddDebugEntryResult>();
            }

            public void SetBlobIdentifier(SymbolBlobIdentifier blobIdentifier)
            {
                lock (m_lock)
                {
                    BlobIdentifier = blobIdentifier;
                }
            }
        }

        private sealed class DebugEntryBlobIdComparer : IEqualityComparer<DebugEntry>
        {
            public static DebugEntryBlobIdComparer Instance = new DebugEntryBlobIdComparer();

            private DebugEntryBlobIdComparer()
            {
            }

            public bool Equals(DebugEntry x, DebugEntry y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }
                if (ReferenceEquals(x, null))
                {
                    return false;
                }
                if (ReferenceEquals(y, null))
                {
                    return false;
                }

                return Equals(x.BlobIdentifier, y.BlobIdentifier);
            }

            public int GetHashCode(DebugEntry obj)
            {
                return obj.BlobIdentifier?.GetHashCode() ?? 0;
            }
        }
    }
}