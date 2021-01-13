// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.ExternalApi.Commands;
using BuildXL.Ipc.Interfaces;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Microsoft.ManifestGenerator;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// IPC server providing an implementation for the BuildXL External API <see cref="BuildXL.Ipc.ExternalApi.Client"/>.
    /// </summary>
    public sealed class ApiServer : IIpcOperationExecutor, IDisposable
    {
        private readonly FileContentManager m_fileContentManager;
        private readonly EngineCache m_engineCache;
        private readonly IServer m_server;
        private readonly PipExecutionContext m_context;
        private readonly Tracing.IExecutionLogTarget m_executionLog;
        private readonly Tracing.BuildManifestGenerator m_buildManifestGenerator;

        private long m_numMaterializeFile;
        private long m_materializeFileTotalExecutionTimeMs;
        private long m_numRegisterBuildManifestHashes;
        private long m_registerBuildManifestHashesTotalExecutionTimeMs;
        private long m_numGenerateBuildManifestFile;
        private long m_numReportStatistics;
        private long m_numGetSealedDirectoryContent;
        private long m_getSealedDirectoryContentTotalExecutionTimeMs;
        private long m_numLogMessage;

        private LoggingContext m_loggingContext;

        private readonly ConcurrentDictionary<ContentHash, ContentHash> m_inMemoryBuildManifestStore;

        /// <nodoc />
        public ApiServer(
            IIpcProvider ipcProvider,
            string ipcMonikerId,
            FileContentManager fileContentManager,
            PipExecutionContext context,
            IServerConfig config,
            EngineCache engineCache,
            Tracing.IExecutionLogTarget executionLog,
            Tracing.BuildManifestGenerator buildManifestGenerator)
        {
            Contract.Requires(ipcMonikerId != null);
            Contract.Requires(fileContentManager != null);
            Contract.Requires(context != null);
            Contract.Requires(config != null);
            Contract.Requires(engineCache != null);
            Contract.Requires(executionLog != null);

            m_fileContentManager = fileContentManager;
            m_server = ipcProvider.GetServer(ipcProvider.LoadAndRenderMoniker(ipcMonikerId), config);
            m_context = context;
            m_engineCache = engineCache;
            m_executionLog = executionLog;
            m_buildManifestGenerator = buildManifestGenerator;
            m_inMemoryBuildManifestStore = new ConcurrentDictionary<ContentHash, ContentHash>();
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

        private static (WeakContentFingerprint wf, StrongContentFingerprint sf) GetBuildManifestHashKey(ContentHash hash)
        {
            var hashBytes = hash.ToByteArray();
            Array.Resize(ref hashBytes, FingerprintUtilities.FingerprintLength);
            var wf = new WeakContentFingerprint(FingerprintUtilities.CreateFrom(hashBytes));
            var sf = new StrongContentFingerprint(wf.Hash);
            return (wf, sf);
        }

        private async Task StoreBuildManifestHashAsync(ContentHash hash, ContentHash manifestHash)
        {
            (var wf, var sf) = GetBuildManifestHashKey(hash);

            var result = await m_engineCache.TwoPhaseFingerprintStore.TryPublishCacheEntryAsync(wf, hash, sf, new CacheEntry(manifestHash, "", ArrayView<ContentHash>.Empty));

            if (!result.Succeeded)
            {
                Tracing.Logger.Log.ApiServerStoreBuildManifestHashToCacheFailed(m_loggingContext, hash.Serialize(), manifestHash.Serialize(), result.Failure.DescribeIncludingInnerFailures()); 
            }
        }

        private async Task<ContentHash?> TryGetBuildManifestHashAsync(ContentHash hash)
        {
            (var wf, var sf) = GetBuildManifestHashKey(hash);

            var result = await m_engineCache.TwoPhaseFingerprintStore.TryGetCacheEntryAsync(wf, hash, sf);

            if (result.Succeeded)
            {
                return result.Result?.MetadataHash;
            }

            return null;
        }

        private async Task<Possible<ContentHash>> TryGetBuildManifestHashFromLocalFileAsync(string fullFilePath)
        {
            if (File.Exists(fullFilePath))
            {
                try
                {
                    var hash = await ContentHashingUtilities.HashFileForBuildManifestAsync(fullFilePath);
                    Tracing.Logger.Log.ApiServerForwardedIpcServerMessage(m_loggingContext, "Verbose", $"Local file found at path '{fullFilePath}'. BuildManifestHash: '{hash.Serialize()}'");
                    return hash;
                }
                catch (BuildXLException ex)
                {
                    Tracing.Logger.Log.ApiServerForwardedIpcServerMessage(m_loggingContext, "Verbose", $"Local file found at path '{fullFilePath}' but threw exception while computing BuildManifest Hash: {ex.Message}");
                    return new Failure<string>($"Exception in TryGetBuildManifestHashFromLocalFileAsync: {ex}");
                }
            }

            Tracing.Logger.Log.ApiServerForwardedIpcServerMessage(m_loggingContext, "Verbose", $"Local file not found at path '{fullFilePath}' while computing BuildManifest Hash. Trying other methods to obtain hash.");
            return new Failure<string>($"File doesn't exist: '{fullFilePath}'");
        }

        /// <summary>
        /// Compute the SHA-256 hash for file stored in Cache. Required for Build Manifets generation.
        /// </summary>
        private async Task<Possible<ContentHash>> ComputeBuildManifestHashFromCacheAsync(BuildManifestEntry buildManifestEntry)
        {
            if (!File.Exists(buildManifestEntry.FullFilePath))
            {
                // Ensure file is materialized locally
                if (!AbsolutePath.TryCreate(m_context.PathTable, buildManifestEntry.FullFilePath, out AbsolutePath path))
                {
                    return new Failure<string>($"Invalid absolute path: '{buildManifestEntry.FullFilePath}'");
                }

                MaterializeFileCommand materializeCommand = new MaterializeFileCommand(FileArtifact.CreateOutputFile(path), buildManifestEntry.FullFilePath);
                IIpcResult materializeResult = await ExecuteMaterializeFileAsync(materializeCommand);
                if (!materializeResult.Succeeded)
                {
                    return new Failure<string>($"Unable to materialize file: '{buildManifestEntry.FullFilePath}' with hash: '{buildManifestEntry.Hash.Serialize()}'. Failure: {materializeResult.Payload}");
                }
            }

            return await TryGetBuildManifestHashFromLocalFileAsync(buildManifestEntry.FullFilePath);
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
        /// Logs statistics.
        /// </summary>
        public void LogStats(LoggingContext loggingContext)
        {
            Logger.Log.BulkStatistic(loggingContext, new Dictionary<string, long>
            {
                [Statistics.ApiTotalMaterializeFileCalls] = Volatile.Read(ref m_numMaterializeFile),
                [Statistics.ApiMaterializeFileCallsTotalExecutionTimeMs] = Volatile.Read(ref m_materializeFileTotalExecutionTimeMs),
                [Statistics.ApiBatchedRegisterBuildManifestHashesCalls] = Volatile.Read(ref m_numRegisterBuildManifestHashes),
                [Statistics.ApiRegisterBuildManifestHashesTotalExecutionTimeMs] = Volatile.Read(ref m_registerBuildManifestHashesTotalExecutionTimeMs),
                [Statistics.ApiTotalGenerateBuildManifestFileCalls] = Volatile.Read(ref m_numGenerateBuildManifestFile),
                [Statistics.ApiTotalReportStatisticsCalls] = Volatile.Read(ref m_numReportStatistics),
                [Statistics.ApiTotalGetSealedDirectoryContentCalls] = Volatile.Read(ref m_numGetSealedDirectoryContent),
                [Statistics.ApiGetSealedDirectoryContentTotalExecutionTimeMs] = Volatile.Read(ref m_getSealedDirectoryContentTotalExecutionTimeMs),
                [Statistics.ApiTotalLogMessageCalls] = Volatile.Read(ref m_numLogMessage),
            });
        }

        async Task<IIpcResult> IIpcOperationExecutor.ExecuteAsync(int id, IIpcOperation op)
        {
            Contract.Requires(op != null);

            Tracing.Logger.Log.ApiServerOperationReceived(m_loggingContext, op.Payload);
            var maybeIpcResult = await TryDeserialize(op.Payload)
                .ThenAsync(cmd => TryExecuteCommand(cmd));

            return maybeIpcResult.Succeeded
                ? maybeIpcResult.Result
                : new IpcResult(IpcResultStatus.ExecutionError, maybeIpcResult.Failure.Describe());
        }

        /// <summary>
        /// Generic ExecuteCommand.  Pattern matches <paramref name="cmd"/> and delegates
        /// to a specific Execute* method based on the commands type.
        /// </summary>
        private async Task<Possible<IIpcResult>> TryExecuteCommand(Command cmd)
        {
            Contract.Requires(cmd != null);

            var materializeFileCmd = cmd as MaterializeFileCommand;
            if (materializeFileCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteMaterializeFileAsync, materializeFileCmd, ref m_numMaterializeFile, ref m_materializeFileTotalExecutionTimeMs);
                return new Possible<IIpcResult>(result);
            }

            var registerBuildManifestHashesCmd = cmd as RegisterFilesForBuildManifestCommand;
            if (registerBuildManifestHashesCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteRecordBuildManifestHashesAsync, registerBuildManifestHashesCmd, ref m_numRegisterBuildManifestHashes, ref m_registerBuildManifestHashesTotalExecutionTimeMs);
                return new Possible<IIpcResult>(result);
            }

            var generateBuildManifestDataCmd = cmd as GenerateBuildManifestDataCommand;
            if (generateBuildManifestDataCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteGenerateBuildManifestDataAsync, generateBuildManifestDataCmd, ref m_numGenerateBuildManifestFile);
                return new Possible<IIpcResult>(result);
            }

            var reportStatisticsCmd = cmd as ReportStatisticsCommand;
            if (reportStatisticsCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteReportStatistics, reportStatisticsCmd, ref m_numReportStatistics);
                return new Possible<IIpcResult>(result);
            }

            var getSealedDirectoryFilesCmd = cmd as GetSealedDirectoryContentCommand;
            if (getSealedDirectoryFilesCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteGetSealedDirectoryContent, getSealedDirectoryFilesCmd, ref m_numGetSealedDirectoryContent, ref m_getSealedDirectoryContentTotalExecutionTimeMs);
                return new Possible<IIpcResult>(result);
            }

            var logMessageCmd = cmd as LogMessageCommand;
            if (logMessageCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteLogMessage, logMessageCmd, ref m_numLogMessage);
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

        private Task<IIpcResult> ExecuteGenerateBuildManifestDataAsync(GenerateBuildManifestDataCommand cmd)
            => Task.FromResult(ExecuteGenerateBuildManifestData(cmd));

        /// <summary>
        /// Executes <see cref="GenerateBuildManifestDataCommand"/>. Generates a BuildManifest.json file for given
        /// <see cref="GenerateBuildManifestDataCommand.DropName"/>.
        /// </summary>
        private IIpcResult ExecuteGenerateBuildManifestData(GenerateBuildManifestDataCommand cmd)
        {
            Contract.Requires(cmd != null);
            Contract.Requires(m_buildManifestGenerator != null, "Build Manifest data can only be generated on master");

            var duplicateEntries = m_buildManifestGenerator.DuplicateEntries(cmd.DropName);
            if (duplicateEntries.Count != 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append($"Operation Register BuildManifest Hash for Drop '{cmd.DropName}' failed due to files with different hashes being uploaded to the same path: ");
                foreach (var entry in duplicateEntries)
                {
                    sb.Append($"[Path: {entry.relativePath}'. RecordedHash: '{entry.recordedHash}'. RejectedHash: '{entry.rejectedHash}'] ");
                }

                return new IpcResult(IpcResultStatus.ExecutionError, sb.ToString());
            }

            BuildManifestData buildManifestData = m_buildManifestGenerator.GenerateBuildManifestData(cmd);

            return IpcResult.Success(cmd.RenderResult(buildManifestData));
        }

        /// <summary>
        /// Executes <see cref="RegisterFilesForBuildManifestCommand"/>. Checks if local files exist and computes their ContentHash.
        /// Else checks if Cache contains SHA-256 Hashes for given <see cref="BuildManifestEntry"/>.
        /// Returns an empty array on success. Any failing BuildManifestEntries are returned for logging.
        /// If SHA-256 ContentHashes do not exists, the files are materialized using <see cref="ExecuteMaterializeFileAsync"/>, the build manifest hashes are computed and stored into cache.
        /// </summary>
        private async Task<IIpcResult> ExecuteRecordBuildManifestHashesAsync(RegisterFilesForBuildManifestCommand cmd)
        {
            Contract.Requires(cmd != null);

            var tasks = cmd.BuildManifestEntries
                .Select(buildManifestEntry => ExecuteRecordBuildManifestHashAsync(cmd.DropName, buildManifestEntry))
                .ToArray();

            var result = await TaskUtilities.SafeWhenAll(tasks);

            BuildManifestEntry[] failedEntries = result.Where(value => value != null).ToArray();

            return IpcResult.Success(cmd.RenderResult(failedEntries));
        }

        /// <summary>
        /// Returns the BuildManifestEntry when hash read/computation fails. Else returns null.
        /// </summary>
        private async Task<BuildManifestEntry> ExecuteRecordBuildManifestHashAsync(string dropName, BuildManifestEntry buildManifestEntry)
        {
            await Task.Yield(); // Yield to ensure hashing happens asynchronously

            // (1) Attempt hash read from in-memory store
            if (m_inMemoryBuildManifestStore.TryGetValue(buildManifestEntry.Hash, out var buildManifestHash))
            {
                RecordFileForBuildManifestInXLG(dropName, buildManifestEntry.RelativePath, buildManifestEntry.Hash, buildManifestHash);
                return null;
            }

            // (2) Attempt hash read from cache
            ContentHash? hashFromCache = await TryGetBuildManifestHashAsync(buildManifestEntry.Hash);
            if (hashFromCache.HasValue)
            {
                m_inMemoryBuildManifestStore.TryAdd(buildManifestEntry.Hash, hashFromCache.Value);
                RecordFileForBuildManifestInXLG(dropName, buildManifestEntry.RelativePath, buildManifestEntry.Hash, hashFromCache.Value);
                return null;
            }

            // (3) Attempt to compute hash for locally existing file (Materializes non-existing files)
            var computeHashResult = await ComputeBuildManifestHashFromCacheAsync(buildManifestEntry);
            if (computeHashResult.Succeeded)
            {
                m_inMemoryBuildManifestStore.TryAdd(buildManifestEntry.Hash, computeHashResult.Result);
                RecordFileForBuildManifestInXLG(dropName, buildManifestEntry.RelativePath, buildManifestEntry.Hash, computeHashResult.Result);
                await StoreBuildManifestHashAsync(buildManifestEntry.Hash, computeHashResult.Result);

                return null;
            }

            Tracing.Logger.Log.ErrorApiServerGetBuildManifestHashFromCacheFailed(m_loggingContext, buildManifestEntry.Hash.Serialize(), computeHashResult.Failure.DescribeIncludingInnerFailures());
            return buildManifestEntry;
        }

        private void RecordFileForBuildManifestInXLG(string dropName, string relativePath, ContentHash azureArtifactsHash, ContentHash buildManifestHash)
        {
            Tracing.RecordFileForBuildManifestEventData data = new Tracing.RecordFileForBuildManifestEventData(dropName, relativePath, azureArtifactsHash, buildManifestHash);
            m_executionLog.RecordFileForBuildManifest(data);
        }

        /// <summary>
        /// Executes <see cref="ReportStatisticsCommand"/>.
        /// </summary>
        private Task<IIpcResult> ExecuteReportStatistics(ReportStatisticsCommand cmd)
        {
            Contract.Requires(cmd != null);

            Tracing.Logger.Log.ApiServerReportStatisticsExecuted(m_loggingContext, cmd.Stats.Count);
            Logger.Log.BulkStatistic(m_loggingContext, cmd.Stats);
            return Task.FromResult(IpcResult.Success(cmd.RenderResult(true)));
        }

        private async Task<IIpcResult> ExecuteGetSealedDirectoryContent(GetSealedDirectoryContentCommand cmd)
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

        private static Task<IIpcResult> ExecuteCommandWithStats<TCommand>(Func<TCommand, Task<IIpcResult>> executor, TCommand cmd, ref long totalCounter)
            where TCommand : Command
        {
            Interlocked.Increment(ref totalCounter);
            return executor(cmd);
        }

        private static Task<IIpcResult> ExecuteCommandWithStats<TCommand>(Func<TCommand, Task<IIpcResult>> executor, TCommand cmd, ref long totalCounter, ref long executionTimeCounterMs)
            where TCommand : Command
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            var result = ExecuteCommandWithStats(executor, cmd, ref totalCounter);

            stopwatch.Stop();
            Interlocked.Add(ref executionTimeCounterMs, stopwatch.ElapsedMilliseconds);
            return result;
        }
    }
}
