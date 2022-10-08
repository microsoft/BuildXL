// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.ExternalApi.Commands;
using BuildXL.Ipc.Interfaces;
using BuildXL.Native.IO;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Cache;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// IPC server providing an implementation for the BuildXL External API <see cref="BuildXL.Ipc.ExternalApi.Client"/>.
    /// </summary>
    public sealed class ApiServer : IIpcOperationExecutor, IDisposable
    {
        private const int GetRequestedHashFromLocalFileRetryMultiplierMs = 200; // Worst-case delay = 12.4 sec. Math.Pow(2, retryAttempt) * GetRequestedHashFromLocalFileRetryMultiplierMs
        private const int GetRequestedHashFromLocalFileRetryLimit = 6;          // Starts from 0, retry multiplier is applied upto (GetRequestedHashFromLocalFileRetryLimit - 1)

        private readonly FileContentManager m_fileContentManager;
        private readonly PipTwoPhaseCache m_pipTwoPhaseCache;
        private readonly IServer m_server;
        private readonly PipExecutionContext m_context;
        private readonly Tracing.IExecutionLogTarget m_executionLog;
        private readonly Tracing.BuildManifestGenerator m_buildManifestGenerator;
        private readonly ServiceManager m_serviceManger;
        private readonly ConcurrentBigMap<ContentHash, IReadOnlyList<ContentHash>> m_inMemoryBuildManifestStore;
        private readonly ConcurrentBigMap<string, long> m_receivedStatistics;
        private readonly bool m_verifyFileContentOnRequestedHashComputation;
        private LoggingContext m_loggingContext;
        // Build manifest requires HistoricMetadataCache. If it's not available, we need to log a warning on the
        // first build manifest API call. 
        private int m_historicMetadataCacheCheckComplete = 0;

        private ObjectPool<List<HashType>> m_hashTypePoolForHashComputation;

        /// <summary>
        /// Counters for all ApiServer related statistics.
        /// </summary>
        public static readonly CounterCollection<ApiServerCounters> Counters = new CounterCollection<ApiServerCounters>();

        /// <summary>
        /// Counters for all Build Manifest related statistics within ApiServer.
        /// </summary>
        public static readonly CounterCollection<BuildManifestCounters> ManifestCounters = new CounterCollection<BuildManifestCounters>();

        /// <summary>
        /// Counters for recomputing content hash related statistics within ApiServer.
        /// </summary>
        private readonly CounterCollection<RecomputeContentHashCounters> m_computingContentHashCounters = new CounterCollection<RecomputeContentHashCounters>();

        /// <nodoc />
        public ApiServer(
            IIpcProvider ipcProvider,
            string ipcMonikerId,
            FileContentManager fileContentManager,
            PipExecutionContext context,
            IServerConfig config,
            PipTwoPhaseCache pipTwoPhaseCache,
            Tracing.IExecutionLogTarget executionLog,
            Tracing.BuildManifestGenerator buildManifestGenerator,
            ServiceManager serviceManger,
            bool verifyFileContentOnBuildManifestHashComputation)
        {
            Contract.Requires(ipcMonikerId != null);
            Contract.Requires(fileContentManager != null);
            Contract.Requires(context != null);
            Contract.Requires(config != null);
            Contract.Requires(pipTwoPhaseCache != null);
            Contract.Requires(executionLog != null);

            m_fileContentManager = fileContentManager;
            m_server = ipcProvider.GetServer(ipcProvider.LoadAndRenderMoniker(ipcMonikerId), config);
            m_context = context;
            m_executionLog = executionLog;
            m_buildManifestGenerator = buildManifestGenerator;
            m_serviceManger = serviceManger;
            m_pipTwoPhaseCache = pipTwoPhaseCache;
            m_inMemoryBuildManifestStore = new ConcurrentBigMap<ContentHash, IReadOnlyList<ContentHash>>();
            m_receivedStatistics = new ConcurrentBigMap<string, long>();
            m_verifyFileContentOnRequestedHashComputation = verifyFileContentOnBuildManifestHashComputation;
            m_hashTypePoolForHashComputation = Pools.CreateListPool<HashType>();
        }

        /// <summary>
        /// Starts the server. <seealso cref="IServer.Start"/>
        /// </summary>
        public void Start(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);

            m_loggingContext = loggingContext;
            m_server.Start(this);
        }

        /// <summary>
        /// Stops the server and waits until it is stopped.
        /// <seealso cref="IStoppable.RequestStop"/>, <seealso cref="IStoppable.Completion"/>.
        /// </summary>
        public Task Stop()
        {
            m_server.RequestStop();
            return m_server.Completion;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_server.Dispose();
        }

        /// <summary>
        /// Logs ApiServer's counters as well as the stats reported by server's clients
        /// </summary>
        public void LogStats()
        {
            Counters.LogAsStatistics("ApiServer", m_loggingContext);
            ManifestCounters.LogAsStatistics("ApiServer.BuildManifest", m_loggingContext);
            m_computingContentHashCounters.LogAsStatistics("ApiServer.ReComputingContentHash", m_loggingContext);
            Logger.Log.BulkStatistic(m_loggingContext, m_receivedStatistics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
        }

        private void StoreBuildManifestHashes(ContentHash hash, IReadOnlyList<ContentHash> manifestHashes)
        {
            using (ManifestCounters.StartStopwatch(BuildManifestCounters.InternalHashToHashCacheWriteDuration))
            {
                foreach (var manifestHash in manifestHashes)
                {
                    ManifestCounters.IncrementCounter(BuildManifestCounters.InternalHashToHashCacheWriteCount);
                    m_pipTwoPhaseCache.TryStoreRemappedContentHash(hash, manifestHash);
                }
            }
        }

        // TODO : Use an object pool for all these lists that are now flying around?
        private bool TryGetBuildManifestHashesAsync(ContentHash identifyingHash, out IReadOnlyList<ContentHash> manifestHashes)
        {
            var manifestHashesMutable = new List<ContentHash>();
            if (Interlocked.CompareExchange(ref m_historicMetadataCacheCheckComplete, 1, 0) == 0)
            {
                // It's the first time this API is called. We need to check that the historic metadata cache is available.
                // The cache is vital to the perf of build manifest, so we need to emit a warning if it cannot be used.
                // CompareExchange ensures that we do this check at most one time.
                var hmc = m_pipTwoPhaseCache as HistoricMetadataCache;
                // Need to make sure the loading task is complete before checking whether the cache is valid.
                hmc?.StartLoading(waitForCompletion: true);
                if (hmc == null || !hmc.Valid)
                {
                    Tracing.Logger.Log.ApiServerReceivedWarningMessage(m_loggingContext, "Build manifest requires historic metadata cache; however, it is not available in this build. This will negatively affect build performance.");
                }
            }

            using (ManifestCounters.StartStopwatch(BuildManifestCounters.InternalHashToHashCacheReadDuration))
            {
                foreach (var hashType in ContentHashingUtilities.BuildManifestHashTypes)
                {
                    ManifestCounters.IncrementCounter(BuildManifestCounters.InternalHashToHashCacheReadCount);
                    var buildManifestHash = m_pipTwoPhaseCache.TryGetMappedContentHash(identifyingHash, hashType);
                    if (!buildManifestHash.IsValid)
                    {
                        // This means that we will recompute all the hashes if any single one of them is missing.
                        // Because we always store/retrieve all the hashes simultaneously, either all will be present
                        // or none, and the TTL of the entries will be in sync.
                        // If in the future this behavior changes, we probably want to optimize this.
                        manifestHashes = null;
                        return false;
                    }
                    manifestHashesMutable.Add(buildManifestHash);
                }

                manifestHashes = manifestHashesMutable;
                return true;
            }
        }

        private async Task<Possible<IReadOnlyList<ContentHash>>> TryGetRequestedHashFromLocalFileAsync(string fullFilePath, ContentHash hash, IList<HashType> requestedTypes, int retryAttempt = 0)
        {
            Contract.Assert(requestedTypes.Count > 0, "Must request at least one hash type");
            var result = new List<ContentHash>();
            if (retryAttempt >= GetRequestedHashFromLocalFileRetryLimit)
            {
                string message = $"GetRequestedHashFromLocalFileRetryLimit exceeded at path '{fullFilePath}'";
                Tracing.Logger.Log.ApiServerForwardedIpcServerMessage(m_loggingContext, "BuildManifest", message);
                return new Failure<string>(message);
            }

            if (retryAttempt > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, retryAttempt) * GetRequestedHashFromLocalFileRetryMultiplierMs));
            }

            if (!File.Exists(fullFilePath))
            {
                Tracing.Logger.Log.ApiServerForwardedIpcServerMessage(m_loggingContext, "RequestedHashComputation", $"Local file not found at path '{fullFilePath}' while computing BuildManifest Hash. Trying other methods to obtain hash.");
                return new Failure<string>($"File doesn't exist: '{fullFilePath}'");
            }

            var hashers = new HashingStream[requestedTypes.Count - 1];
            HashingStream validationStream = null;
            try
            {
                using var fs = FileUtilities.CreateFileStream(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.Read, FileOptions.SequentialScan).AssertHasLength();

                // If enabled, create a hashing stream for content validation. Using HashType.Unknown uses the default hashtype
                validationStream = m_verifyFileContentOnRequestedHashComputation ? ContentHashingUtilities.GetContentHasher(HashType.Unknown).CreateReadHashingStream(fs) : null;

                // Create a series of nested ReadHashingStream so we compute all the hashes in parallel
                StreamWithLength outerStream = validationStream?.AssertHasLength() ?? fs;
                for (var i = 0; i < hashers.Length; i++)
                {
                    // Hashers has size (requestedTypes.Count - 1)
                    // requestedTypes[0] will be used to hash+consume the resulting outerStream (see below)
                    var hashType = requestedTypes[i + 1];
                    hashers[i] = ContentHashingUtilities.GetContentHasher(hashType).CreateReadHashingStream(outerStream);
                    outerStream = hashers[i].AssertHasLength();
                }

                // Hashing the outermost stream will cause all the nested hashers to also do their processing
                var firstManifestHash = await ContentHashingUtilities.HashContentStreamAsync(outerStream, requestedTypes[0]);
                result.Add(firstManifestHash);

                for (int i = 0; i < hashers.Length; i++)
                {
                    result.Add(hashers[i].GetContentHash());
                }

                if (m_verifyFileContentOnRequestedHashComputation)
                {
                    var actualHash = validationStream.GetContentHash();
                    if (hash != actualHash)
                    {
                        return new Failure<string>($"Unexpected file content during requested hash computation. Path: '{fullFilePath}', expected hash '{hash}', actual hash '{actualHash}'.");
                    }
                }
            }
            catch (Exception ex) when (ex is BuildXLException || ex is IOException)
            {
                Tracing.Logger.Log.ApiServerForwardedIpcServerMessage(m_loggingContext, "RequestedHashComputation",
                    $"Local file found at path '{fullFilePath}' but threw exception while computing requested Hash. Retry attempt {retryAttempt} out of {GetRequestedHashFromLocalFileRetryLimit}. Exception: {ex}");
                return await TryGetRequestedHashFromLocalFileAsync(fullFilePath, hash, requestedTypes, retryAttempt + 1);
            }
            finally
            {
                validationStream?.Dispose();
                for (var i = 0; i < hashers.Length; i++)
                {
                    hashers[i]?.Dispose();
                }
            }

            return result;
        }

        /// <summary>
        /// Compute the hashes for file stored in Cache. Required for Build Manifest generation.
        /// </summary>
        private async Task<Possible<IReadOnlyList<ContentHash>>> ComputeBuildManifestHashFromCacheAsync(BuildManifestEntry buildManifestEntry, IList<HashType> requestedTypes)
        {
            // Ensure that the file is materialized.
            MaterializeFileCommand materializeCommand = new MaterializeFileCommand(buildManifestEntry.Artifact, buildManifestEntry.FullFilePath);
            IIpcResult materializeResult = await ExecuteMaterializeFileAsync(materializeCommand);
            if (!materializeResult.Succeeded)
            {
                return new Failure<string>($"Unable to materialize file: '{buildManifestEntry.FullFilePath}' with hash: '{buildManifestEntry.Hash.Serialize()}'. Failure: {materializeResult.Payload}");
            }

            return await TryGetRequestedHashFromLocalFileAsync(buildManifestEntry.FullFilePath, buildManifestEntry.Hash, requestedTypes);
        }

        async Task<IIpcResult> IIpcOperationExecutor.ExecuteAsync(int id, IIpcOperation op)
        {
            Contract.Requires(op != null);

            Tracing.Logger.Log.ApiServerOperationReceived(m_loggingContext, op.Payload);
            var maybeIpcResult = await TryDeserialize(op.Payload)
                .ThenAsync(cmd => TryExecuteCommandAsync(cmd));

            return maybeIpcResult.Succeeded
                ? maybeIpcResult.Result
                : new IpcResult(IpcResultStatus.ExecutionError, maybeIpcResult.Failure.Describe());
        }

        /// <summary>
        /// Generic ExecuteCommand.  Pattern matches <paramref name="cmd"/> and delegates
        /// to a specific Execute* method based on the commands type.
        /// </summary>
        private async Task<Possible<IIpcResult>> TryExecuteCommandAsync(Command cmd)
        {
            Contract.Requires(cmd != null);

            var materializeFileCmd = cmd as MaterializeFileCommand;
            if (materializeFileCmd != null)
            {
                using (Counters.StartStopwatch(ApiServerCounters.MaterializeFileCallsDuration))
                {
                    var result = await ExecuteCommandWithStats(ExecuteMaterializeFileAsync, materializeFileCmd, ApiServerCounters.MaterializeFileCalls);
                    return new Possible<IIpcResult>(result);
                }

            }

            var registerBuildManifestHashesCmd = cmd as RegisterFilesForBuildManifestCommand;
            if (registerBuildManifestHashesCmd != null)
            {
                using (ManifestCounters.StartStopwatch(BuildManifestCounters.RegisterHashesDuration))
                {
                    var result = await ExecuteCommandWithStats(ExecuteRecordBuildManifestHashesAsync, registerBuildManifestHashesCmd, BuildManifestCounters.BatchedRegisterHashesCalls);
                    return new Possible<IIpcResult>(result);
                }
            }

            var generateBuildManifestDataCmd = cmd as GenerateBuildManifestFileListCommand;
            if (generateBuildManifestDataCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteGenerateBuildManifestFileListAsync, generateBuildManifestDataCmd, BuildManifestCounters.TotalGenerateBuildManifestFileListCalls);
                return new Possible<IIpcResult>(result);
            }

            var reportStatisticsCmd = cmd as ReportStatisticsCommand;
            if (reportStatisticsCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteReportStatistics, reportStatisticsCmd, ApiServerCounters.TotalReportStatisticsCalls);
                return new Possible<IIpcResult>(result);
            }

            var getSealedDirectoryFilesCmd = cmd as GetSealedDirectoryContentCommand;
            if (getSealedDirectoryFilesCmd != null)
            {
                using (Counters.StartStopwatch(ApiServerCounters.GetSealedDirectoryContentDuration))
                {
                    var result = await ExecuteCommandWithStats(ExecuteGetSealedDirectoryContentAsync, getSealedDirectoryFilesCmd, ApiServerCounters.TotalGetSealedDirectoryContentCalls);
                    return new Possible<IIpcResult>(result);
                }
            }

            var logMessageCmd = cmd as LogMessageCommand;
            if (logMessageCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteLogMessage, logMessageCmd, ApiServerCounters.TotalLogMessageCalls);
                return new Possible<IIpcResult>(result);
            }

            var reportServicePipIsReady = cmd as ReportServicePipIsReadyCommand;
            if (reportServicePipIsReady != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteReportServicePipIsReadyAsync, reportServicePipIsReady, ApiServerCounters.TotalServicePipIsReadyCalls);
                return new Possible<IIpcResult>(result);
            }

            var rehashCommand = cmd as RecomputeContentHashCommand;
            if (rehashCommand != null)
            {
                var result = await ExecuteRecomputeContentHashAsync(rehashCommand);
                m_computingContentHashCounters.IncrementCounter(RecomputeContentHashCounters.TotalNumberOfRecomputingHashCalls);
                return new Possible<IIpcResult>(result);
            }

            var reportDaemonTelemetryCmd = cmd as ReportDaemonTelemetryCommand;
            if (reportDaemonTelemetryCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteReportDaemonTelemetry, reportDaemonTelemetryCmd, ApiServerCounters.TotalReportDaemonTelemetryCalls);
                return new Possible<IIpcResult>(result);
            }

            var errorMessage = "Unimplemented command: " + cmd.GetType().FullName;
            Contract.Assert(false, errorMessage);
            return new Failure<string>(errorMessage);
        }

        /// <summary>
        /// Executes <see cref="MaterializeFileCommand"/>.  First check that <see cref="MaterializeFileCommand.File"/>
        /// and <see cref="MaterializeFileCommand.FullFilePath"/> match, then delegates to <see cref="FileContentManager.TryMaterializeFileAsync(FileArtifact)"/>.
        /// If provided <see cref="MaterializeFileCommand.File"/> is not valid, no checks are done, and the call is delegated
        /// to <see cref="FileContentManager.TryMaterializeSealedFileAsync(AbsolutePath)"/>
        /// </summary>
        private async Task<IIpcResult> ExecuteMaterializeFileAsync(MaterializeFileCommand cmd)
        {
            Contract.Requires(cmd != null);

            // If the FileArtifact was provided, for extra safety, check that provided file path and file id match
            AbsolutePath filePath;
            bool isValidPath = AbsolutePath.TryCreate(m_context.PathTable, cmd.FullFilePath, out filePath);
            if (cmd.File.IsValid && (!isValidPath || !cmd.File.Path.Equals(filePath)))
            {
                return new IpcResult(
                    IpcResultStatus.ExecutionError,
                    "file path ids differ; file = " + cmd.File.Path.ToString(m_context.PathTable) + ", file path = " + cmd.FullFilePath);
            }
            // If only path was provided, check that it's a valid path.
            else if (!cmd.File.IsValid && !filePath.IsValid)
            {
                return new IpcResult(
                   IpcResultStatus.ExecutionError,
                   $"failed to create AbsolutePath from '{cmd.FullFilePath}'");
            }

            var result = cmd.File.IsValid
                ? await m_fileContentManager.TryMaterializeFileAsync(cmd.File)
                // If file artifact is unknown, try materializing using only the file path.
                // This method has lower chance of success, since it depends on FileContentManager's
                // ability to infer FileArtifact associated with this path.
                : await m_fileContentManager.TryMaterializeSealedFileAsync(filePath);
            bool succeeded = result == ArtifactMaterializationResult.Succeeded;
            string absoluteFilePath = cmd.File.Path.ToString(m_context.PathTable);

            // if file materialization failed, log an error here immediately, so that this errors gets picked up as the root cause 
            // (i.e., the "ErrorBucket") instead of whatever fallout ends up happening (e.g., IPC pip fails)
            if (!succeeded)
            {
                // For sealed files, materialization might not have succeeded because a path is not known to BXL.
                // In such a case, do not log an error, and let the caller deal with the failure.
                if (cmd.File.IsValid)
                {
                    Tracing.Logger.Log.ErrorApiServerMaterializeFileFailed(m_loggingContext, absoluteFilePath, cmd.File.IsValid, result.ToString());
                }
            }
            else
            {
                Tracing.Logger.Log.ApiServerMaterializeFileSucceeded(m_loggingContext, absoluteFilePath);
            }

            return IpcResult.Success(cmd.RenderResult(succeeded));
        }

        private Task<IIpcResult> ExecuteGenerateBuildManifestFileListAsync(GenerateBuildManifestFileListCommand cmd)
            => Task.FromResult(ExecuteGenerateBuildManifestFileList(cmd));

        /// <summary>
        /// Executes <see cref="GenerateBuildManifestFileListCommand"/>. Generates a list of file hashes required for BuildManifest.json file
        /// for given <see cref="GenerateBuildManifestFileListCommand.DropName"/>.
        /// </summary>
        private IIpcResult ExecuteGenerateBuildManifestFileList(GenerateBuildManifestFileListCommand cmd)
        {
            Contract.Requires(cmd != null);
            Contract.Requires(m_buildManifestGenerator != null, "Build Manifest data can only be generated on orchestrator");

            GenerateBuildManifestFileListResult result;

            if (!m_buildManifestGenerator.TryGenerateBuildManifestFileList(cmd.DropName, out string error, out var buildManifestFileList))
            {
                result = GenerateBuildManifestFileListResult.CreateForFailure(GenerateBuildManifestFileListResult.OperationStatus.UserError, error);
            }
            else
            {
                result = GenerateBuildManifestFileListResult.CreateForSuccess(buildManifestFileList);
            }

            // We always return a 'success' here because a call to the API Server was successful.
            // Whether the file list was generated is a part of the result that we return.
            return IpcResult.Success(cmd.RenderResult(result));
        }

        /// <summary>
        /// Executes <see cref="RegisterFilesForBuildManifestCommand"/>. Checks if Cache contains build manifest hashes for given <see cref="BuildManifestEntry"/>.
        /// Else checks if local files exist and computes their ContentHash.
        /// Returns an empty array on success. Any failing BuildManifestEntries are returned for logging.
        /// If build manifest hashes are not available, the files are materialized using <see cref="ExecuteMaterializeFileAsync"/>, the build manifest hashes are computed and stored into cache.
        /// </summary>
        private async Task<IIpcResult> ExecuteRecordBuildManifestHashesAsync(RegisterFilesForBuildManifestCommand cmd)
        {
            Contract.Requires(cmd != null);

            var tasks = cmd.BuildManifestEntries
                .Select(buildManifestEntry => ExecuteRecordBuildManifestHashWithXlgAsync(cmd.DropName, buildManifestEntry))
                .ToArray();

            var result = await TaskUtilities.SafeWhenAll(tasks);

            if (result.Any(value => !value.IsValid))
            {
                BuildManifestEntry[] failures = result.Where(value => !value.IsValid)
                    .Select(value => new BuildManifestEntry(value.RelativePath, value.AzureArtifactsHash, "Invalid", FileArtifact.Invalid)) // FullFilePath is unused by the caller
                    .ToArray();

                return IpcResult.Success(cmd.RenderResult(failures));
            }
            else
            {
                m_executionLog.RecordFileForBuildManifest(new Tracing.RecordFileForBuildManifestEventData(result.ToList()));

                return IpcResult.Success(cmd.RenderResult(Array.Empty<BuildManifestEntry>()));
            }
        }

        /// <summary>
        /// Returns an invalid <see cref="Tracing.BuildManifestEntry"/> when file read or hash computation fails. 
        /// Else returns a valid <see cref="Tracing.BuildManifestEntry"/> on success.
        /// </summary>
        private async Task<Tracing.BuildManifestEntry> ExecuteRecordBuildManifestHashWithXlgAsync(string dropName, BuildManifestEntry buildManifestEntry)
        {
            await Task.Yield(); // Yield to ensure hashing happens asynchronously

            // (1) Attempt hash read from in-memory store
            if (m_inMemoryBuildManifestStore.TryGetValue(buildManifestEntry.Hash, out var buildManifestHash))
            {
                return new Tracing.BuildManifestEntry(dropName, buildManifestEntry.RelativePath, buildManifestEntry.Hash, buildManifestHash);
            }

            // (2) Attempt hash read from cache
            if (TryGetBuildManifestHashesAsync(buildManifestEntry.Hash, out var buildManifestHashes))
            {
                m_inMemoryBuildManifestStore.TryAdd(buildManifestEntry.Hash, buildManifestHashes);
                return new Tracing.BuildManifestEntry(dropName, buildManifestEntry.RelativePath, buildManifestEntry.Hash, buildManifestHashes);
            }

            // (3) Attempt to compute hash for locally existing file (Materializes non-existing files)
            using (ManifestCounters.StartStopwatch(BuildManifestCounters.InternalComputeHashLocallyDuration))
            {
                ManifestCounters.IncrementCounter(BuildManifestCounters.InternalComputeHashLocallyCount);
                var computeHashResult = await ComputeBuildManifestHashFromCacheAsync(buildManifestEntry, requestedTypes: ContentHashingUtilities.BuildManifestHashTypes);
                if (computeHashResult.Succeeded)
                {
                    m_inMemoryBuildManifestStore.TryAdd(buildManifestEntry.Hash, computeHashResult.Result);
                    StoreBuildManifestHashes(buildManifestEntry.Hash, computeHashResult.Result);
                    return new Tracing.BuildManifestEntry(dropName, buildManifestEntry.RelativePath, buildManifestEntry.Hash, computeHashResult.Result);
                }

                Tracing.Logger.Log.ErrorApiServerGetBuildManifestHashFromLocalFileFailed(m_loggingContext, buildManifestEntry.Hash.Serialize(), computeHashResult.Failure.DescribeIncludingInnerFailures());
            }

            ManifestCounters.IncrementCounter(BuildManifestCounters.TotalHashFileFailures);
            return new Tracing.BuildManifestEntry(dropName, buildManifestEntry.RelativePath, buildManifestEntry.Hash, new[] { new ContentHash(HashType.Unknown) });
        }

        /// <summary>
        /// Executes <see cref="ReportStatisticsCommand"/>.
        /// </summary>
        private Task<IIpcResult> ExecuteReportStatistics(ReportStatisticsCommand cmd)
        {
            Contract.Requires(cmd != null);

            Tracing.Logger.Log.ApiServerReportStatisticsExecuted(m_loggingContext, cmd.Stats.Count);
            foreach (var statistic in cmd.Stats)
            {
                // we aggregate the stats based on their name
                m_receivedStatistics.AddOrUpdate(
                    statistic.Key,
                    statistic.Value,
                    static (key, value) => value,
                    static (key, newValue, oldValue) => newValue + oldValue);
            }

            return Task.FromResult(IpcResult.Success(cmd.RenderResult(true)));
        }

        private async Task<IIpcResult> ExecuteGetSealedDirectoryContentAsync(GetSealedDirectoryContentCommand cmd)
        {
            Contract.Requires(cmd != null);

            // for extra safety, check that provided directory path and directory id match
            AbsolutePath dirPath;
            bool isValidPath = AbsolutePath.TryCreate(m_context.PathTable, cmd.FullDirectoryPath, out dirPath);
            if (!isValidPath || !cmd.Directory.Path.Equals(dirPath))
            {
                return new IpcResult(
                    IpcResultStatus.ExecutionError,
                    "directory path ids differ, or could not create AbsolutePath; directory = " + cmd.Directory.Path.ToString(m_context.PathTable) + ", directory path = " + cmd.FullDirectoryPath);
            }

            var files = m_fileContentManager.ListSealedDirectoryContents(cmd.Directory);

            Tracing.Logger.Log.ApiServerGetSealedDirectoryContentExecuted(m_loggingContext, cmd.Directory.Path.ToString(m_context.PathTable), files.Length);

            var inputContentsTasks = files
                .Select(f => m_fileContentManager.TryQuerySealedOrUndeclaredInputContentAsync(f.Path, nameof(ApiServer), allowUndeclaredSourceReads: true))
                .ToArray();

            var inputContents = await TaskUtilities.SafeWhenAll(inputContentsTasks);

            var results = new List<BuildXL.Ipc.ExternalApi.SealedDirectoryFile>();
            var failedResults = new List<string>();

            for (int i = 0; i < files.Length; ++i)
            {
                // If the content has no value or has unknown length, then we have some inconsistency wrt the sealed directory content
                // Absent files are an exception since it is possible to have sealed directories with absent files (shared opaques is an example of this). 
                // In those cases we leave the consumer to deal with them.
                if (!inputContents[i].HasValue || (inputContents[i].Value.Hash != WellKnownContentHashes.AbsentFile && !inputContents[i].Value.HasKnownLength))
                {
                    failedResults.Add(files[i].Path.ToString(m_context.PathTable));
                }
                else
                {
                    results.Add(new BuildXL.Ipc.ExternalApi.SealedDirectoryFile(
                        files[i].Path.ToString(m_context.PathTable),
                        files[i],
                        inputContents[i].Value));
                }
            }

            if (failedResults.Count > 0)
            {
                return new IpcResult(
                    IpcResultStatus.ExecutionError,
                    string.Format("Could not find content information for {0} out of {1} files inside of '{4}':{2}{3}",
                        failedResults.Count,
                        files.Length,
                        Environment.NewLine,
                        string.Join("; ", failedResults),
                        cmd.Directory.Path.ToString(m_context.PathTable)));
            }

            return IpcResult.Success(cmd.RenderResult(results));
        }

        /// <summary>
        /// Executes <see cref="LogMessageCommand"/>.
        /// </summary>
        private Task<IIpcResult> ExecuteLogMessage(LogMessageCommand cmd)
        {
            Contract.Requires(cmd != null);

            if (cmd.IsWarning)
            {
                Tracing.Logger.Log.ApiServerReceivedWarningMessage(m_loggingContext, cmd.Message);
            }
            else
            {
                Tracing.Logger.Log.ApiServerReceivedMessage(m_loggingContext, cmd.Message);
            }

            return Task.FromResult(IpcResult.Success(cmd.RenderResult(true)));
        }

        private Task<IIpcResult> ExecuteReportServicePipIsReadyAsync(ReportServicePipIsReadyCommand cmd)
        {
            Contract.Requires(cmd != null);

            m_serviceManger.ReportServiceIsReady(cmd.ProcessId, cmd.ProcessName);

            return Task.FromResult(IpcResult.Success(cmd.RenderResult(true)));
        }

        private async Task<IIpcResult> ExecuteRecomputeContentHashAsync(RecomputeContentHashCommand cmd)
        {
            Contract.Requires(cmd != null);
            await Task.Yield();

            if (!Enum.TryParse(cmd.RequestedHashType, out HashType requestedHashType))
            {
                new IpcResult(IpcResultStatus.ExecutionError, $"{cmd.RequestedHashType} is an unknown hash type");
            }

            var hash = m_pipTwoPhaseCache.TryGetMappedContentHash(cmd.Entry.Hash, requestedHashType);

            string errorMessage = "";
            if (!hash.IsValid)
            {
                // If not existed in cache, then materialize the file and compute its contenthash
                MaterializeFileCommand materializeCommand = new MaterializeFileCommand(cmd.File, cmd.Entry.FullPath);
                IIpcResult materializeResult = await ExecuteMaterializeFileAsync(materializeCommand);
                if (!materializeResult.Succeeded)
                {
                    errorMessage = $"Unable to materialize file: '{cmd.Entry.FullPath}' with hash: '{cmd.Entry.Hash.Serialize()}'. Failure: {materializeResult.Payload}";
                }
                else
                {
                    Possible<IReadOnlyList<ContentHash>> computedHashResult;
                    try
                    {
                        using (m_computingContentHashCounters.StartStopwatch(RecomputeContentHashCounters.HashComputationDuration))
                        {
                            using (var requestedHashListinstance = m_hashTypePoolForHashComputation.GetInstance())
                            {
                                var requestedHashList = requestedHashListinstance.Instance;
                                requestedHashList.Add(requestedHashType);
                                computedHashResult = await TryGetRequestedHashFromLocalFileAsync(cmd.Entry.FullPath, cmd.Entry.Hash, requestedHashList);
                            }
                        }

                        if (!computedHashResult.Succeeded || !computedHashResult.Result[0].IsValid)
                        {
                            errorMessage = "Recomputed hash failed or returned hash is invalid";
                        }
                        else
                        {
                            var computedHash = computedHashResult.Result[0];
                            m_pipTwoPhaseCache.TryStoreRemappedContentHash(cmd.Entry.Hash, computedHash);
                            var entry = new RecomputeContentHashEntry(cmd.Entry.FullPath, computedHash);
                            return IpcResult.Success(cmd.RenderResult(entry));
                        }
                    }
                    catch (Exception e)
                    {
                        errorMessage = $"Unable to recompute the content hash with hashtype {requestedHashType}  for file: '{cmd.Entry.FullPath}' '. Failure: {e}";
                    }
                }
            }
            else
            {
                m_computingContentHashCounters.IncrementCounter(RecomputeContentHashCounters.TotalNumberOfRecomputingHashHits);
                if (hash.IsValid && hash.HashType != requestedHashType)
                {
                    m_computingContentHashCounters.IncrementCounter(RecomputeContentHashCounters.TotalHashFileFailures);
                    errorMessage = $"Get unexpected hash type from cache in recomputing content hash: {hash.HashType}";
                }
                else
                {
                    var entry = new RecomputeContentHashEntry(cmd.Entry.FullPath, hash);
                    return IpcResult.Success(cmd.RenderResult(entry));
                }
            }
            m_computingContentHashCounters.IncrementCounter(RecomputeContentHashCounters.TotalHashFileFailures);
            return new IpcResult(IpcResultStatus.ExecutionError, errorMessage);
        }

        /// <summary>
        /// Executes <see cref="ReportDaemonTelemetryCommand"/>.
        /// </summary>
        private Task<IIpcResult> ExecuteReportDaemonTelemetry(ReportDaemonTelemetryCommand cmd)
        {
            Contract.Requires(cmd != null);

            Tracing.Logger.Log.ApiServerReportDaemonTelemetryExecuted(m_loggingContext, cmd.DaemonName);
            Tracing.Logger.Log.DaemonTelemetry(m_loggingContext, cmd.DaemonName, cmd.TelemetryPayload ?? string.Empty, cmd.InfoPayload ?? string.Empty);

            return Task.FromResult(IpcResult.Success(cmd.RenderResult(true)));
        }

        private Possible<Command> TryDeserialize(string operation)
        {
            try
            {
                return Command.Deserialize(operation);
            }
            catch (Exception e)
            {
                Tracing.Logger.Log.ApiServerInvalidOperation(m_loggingContext, operation, e.ToStringDemystified());
                return new Failure<string>("Invalid operation: " + operation);
            }
        }

        private static Task<IIpcResult> ExecuteCommandWithStats<TCommand>(Func<TCommand, Task<IIpcResult>> executor, TCommand cmd, ApiServerCounters totalCounter)
            where TCommand : Command
        {
            Counters.IncrementCounter(totalCounter);
            return executor(cmd);
        }

        private static Task<IIpcResult> ExecuteCommandWithStats<TCommand>(Func<TCommand, Task<IIpcResult>> executor, TCommand cmd, BuildManifestCounters totalCounter)
            where TCommand : Command
        {
            ManifestCounters.IncrementCounter(totalCounter);
            return executor(cmd);
        }
    }

    /// <summary>
    /// Counter types for all ApiServer statistics.
    /// </summary>
    public enum ApiServerCounters
    {
        /// <summary>
        /// Number of <see cref="MaterializeFileCommand"/> calls
        /// </summary>
        [CounterType(CounterType.Numeric)]
        MaterializeFileCalls,

        /// <summary>
        /// Time spent on <see cref="MaterializeFileCommand"/> calls
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        MaterializeFileCallsDuration,

        /// <summary>
        /// Number of <see cref="ReportStatisticsCommand"/> calls
        /// </summary>
        [CounterType(CounterType.Numeric)]
        TotalReportStatisticsCalls,

        /// <summary>
        /// Number of <see cref="GetSealedDirectoryContentCommand"/> calls
        /// </summary>
        [CounterType(CounterType.Numeric)]
        TotalGetSealedDirectoryContentCalls,

        /// <summary>
        /// Time spent on <see cref="GetSealedDirectoryContentCommand"/> calls
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        GetSealedDirectoryContentDuration,

        /// <summary>
        /// Number of <see cref="LogMessageCommand"/> calls
        /// </summary>
        [CounterType(CounterType.Numeric)]
        TotalLogMessageCalls,

        /// <summary>
        /// Number of <see cref="ReportServicePipIsReadyCommand"/> calls
        /// </summary>
        [CounterType(CounterType.Numeric)]
        TotalServicePipIsReadyCalls,

        /// <summary>
        /// Number of <see cref="ReportDaemonTelemetryCommand"/> calls
        /// </summary>
        [CounterType(CounterType.Numeric)]
        TotalReportDaemonTelemetryCalls,
    }

    /// <summary>
    /// Counter types for all BuildManifest statistics within ApiServer.
    /// </summary>
    public enum BuildManifestCounters
    {
        /// <summary>
        /// Time spent obtaining hash to hash mappings from cache for build manifest
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        InternalHashToHashCacheReadDuration,

        /// <summary>
        /// Number of calls for obtaining hash to hash mappings from cache for build manifest
        /// </summary>
        [CounterType(CounterType.Numeric)]
        InternalHashToHashCacheReadCount,

        /// <summary>
        /// Time spent obtaining hash to hash mappings from cache for build manifest
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        InternalHashToHashCacheWriteDuration,

        /// <summary>
        /// Number of calls for obtaining hash to hash mappings from cache for build manifest
        /// </summary>
        [CounterType(CounterType.Numeric)]
        InternalHashToHashCacheWriteCount,

        /// <summary>
        /// Time spent computing hashes for build manifest (includes materialization times for non-existing files)
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        InternalComputeHashLocallyDuration,

        /// <summary>
        /// Number of calls for computing hashes for build manifest (includes materialization times for non-existing files)
        /// </summary>
        [CounterType(CounterType.Numeric)]
        InternalComputeHashLocallyCount,

        /// <summary>
        /// Number of <see cref="RegisterFilesForBuildManifestCommand"/> calls
        /// </summary>
        [CounterType(CounterType.Numeric)]
        BatchedRegisterHashesCalls,

        /// <summary>
        /// Time spent on <see cref="RegisterFilesForBuildManifestCommand"/> calls
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        RegisterHashesDuration,

        /// <summary>
        /// Number of <see cref="GenerateBuildManifestFileListCommand"/> calls
        /// </summary>
        [CounterType(CounterType.Numeric)]
        TotalGenerateBuildManifestFileListCalls,

        /// <summary>
        /// Number of failed file hash computations during <see cref="GenerateBuildManifestFileListCommand"/> calls
        /// </summary>
        [CounterType(CounterType.Numeric)]
        TotalHashFileFailures,
    }

    /// <summary>
    /// Counters for recomputing content hash related statistics within ApiServer.
    /// </summary>
    public enum RecomputeContentHashCounters
    {
        /// <summary>
        /// Number of failed file hash computations
        /// </summary>
        [CounterType(CounterType.Numeric)]
        TotalHashFileFailures,

        /// <summary>
        /// Time spent computing hashes for files
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HashComputationDuration,

        /// <summary>
        /// Number of calls to recomputing hash
        /// </summary>
        [CounterType(CounterType.Numeric)]
        TotalNumberOfRecomputingHashCalls,

        /// <summary>
        /// Number of recomputing hash calls are cached
        /// </summary>
        [CounterType(CounterType.Numeric)]
        TotalNumberOfRecomputingHashHits,
    }
}
