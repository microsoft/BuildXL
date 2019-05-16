// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Cache;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;
using static BuildXL.Scheduler.FileMonitoringViolationAnalyzer;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

namespace BuildXL.Scheduler
{
    /// <summary>
    /// This class brings pips to life
    /// </summary>
    public static partial class PipExecutor
    {
        /// <summary>
        /// The maximum number of times to retry running a pip due to internal sandboxed process execution failure.
        /// </summary>
        /// <remarks>
        /// Internal failure include <see cref="SandboxedProcessPipExecutionStatus.OutputWithNoFileAccessFailed"/>
        /// and <see cref="SandboxedProcessPipExecutionStatus.MismatchedMessageCount"/>.
        /// </remarks>
        public const int InternalSandboxedProcessExecutionFailureRetryCountMax = 5;

        private static readonly object s_telemetryDetoursHeapLock = new object();

        private static readonly ObjectPool<Dictionary<AbsolutePath, FileOutputData>> s_absolutePathFileOutputDataMapPool =
            new ObjectPool<Dictionary<AbsolutePath, FileOutputData>>(
                () => new Dictionary<AbsolutePath, FileOutputData>(),
                map => { map.Clear(); return map; });

        private static readonly ObjectPool<List<(AbsolutePath, FileMaterializationInfo)>> s_absolutePathFileMaterializationInfoTuppleListPool = Pools.CreateListPool<(AbsolutePath, FileMaterializationInfo)>();

        private static readonly ObjectPool<Dictionary<FileArtifact, Task<Possible<FileMaterializationInfo>>>> s_fileArtifactPossibleFileMaterializationInfoTaskMapPool =
            new ObjectPool<Dictionary<FileArtifact, Task<Possible<FileMaterializationInfo>>>>(
                () => new Dictionary<FileArtifact, Task<Possible<FileMaterializationInfo>>>(),
                map => { map.Clear(); return map; });

        /// <summary>
        /// Materializes pip's inputs.
        /// </summary>
        public static async Task<PipResultStatus> MaterializeInputsAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            Pip pip)
        {
            Contract.Requires(environment != null);
            Contract.Requires(pip != null);

            // Any errors will be logged within FileContentManager
            var materializedSuccess = await environment.State.FileContentManager.TryMaterializeDependenciesAsync(pip, operationContext);

            // Make sure an error was logged here close to the source since the build will fail later on without dependencies anyway
            Contract.Assert(materializedSuccess || operationContext.LoggingContext.ErrorWasLogged);
            return materializedSuccess ? PipResultStatus.Succeeded : PipResultStatus.Failed;
        }

        /// <summary>
        /// Materializes pip's outputs.
        /// </summary>
        public static async Task<PipResultStatus> MaterializeOutputsAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            Pip pip)
        {
            Contract.Requires(environment != null);
            Contract.Requires(pip != null);

            var maybeMaterialized = await environment.State.FileContentManager.TryMaterializeOutputsAsync(pip, operationContext);

            if (!maybeMaterialized.Succeeded)
            {
                if (!environment.Context.CancellationToken.IsCancellationRequested)
                {
                    Logger.Log.PipFailedToMaterializeItsOutputs(
                        operationContext,
                        pip.GetDescription(environment.Context),
                        maybeMaterialized.Failure.DescribeIncludingInnerFailures());
                }

                Contract.Assert(operationContext.LoggingContext.ErrorWasLogged);
            }

            return maybeMaterialized.Succeeded ? maybeMaterialized.Result.ToPipResult() : PipResultStatus.Failed;
        }

        /// <summary>
        /// Performs a file copy if the destination is not up-to-date with respect to the source.
        /// </summary>
        public static async Task<PipResult> ExecuteCopyFileAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            CopyFile pip,
            bool materializeOutputs = true)
        {
            Contract.Requires(environment != null);
            Contract.Requires(pip != null);

            var context = environment.Context;
            var pathTable = context.PathTable;
            var pipInfo = new PipInfo(pip, context);
            var pipDescription = pipInfo.Description;



            string destination = pip.Destination.Path.ToString(pathTable);
            string source = pip.Source.Path.ToString(pathTable);

            DateTime startTime = DateTime.UtcNow;

            using (operationContext.StartOperation(PipExecutorCounter.CopyFileDuration))
            {
                try
                {
                    FileMaterializationInfo sourceMaterializationInfo = environment.State.FileContentManager.GetInputContent(pip.Source);
                    FileContentInfo sourceContentInfo = sourceMaterializationInfo.FileContentInfo;

                    var symlinkTarget = environment.State.FileContentManager.TryGetRegisteredSymlinkFinalTarget(pip.Source.Path);
                    ReadOnlyArray<AbsolutePath> symlinkChain;
                    bool isSymLink = symlinkTarget.IsValid;
                    if (isSymLink)
                    {
                        symlinkChain = ReadOnlyArray<AbsolutePath>.FromWithoutCopy(new[] { symlinkTarget });
                    }
                    else
                    {
                        // pip.Source is not a registered symlink - check if this file forms a proper symlink chain
                        var possibleSymlinkChain = CheckValidSymlinkChainAsync(pip.Source, environment);
                        if (!possibleSymlinkChain.Succeeded)
                        {
                            possibleSymlinkChain.Failure.Throw();
                        }

                        symlinkChain = possibleSymlinkChain.Result;
                        if (symlinkChain.Length > 0)
                        {
                            symlinkTarget = symlinkChain[symlinkChain.Length - 1];
                            isSymLink = true;
                        }
                    }

                    if (isSymLink && !environment.Configuration.Schedule.AllowCopySymlink)
                    {
                        (new Failure<string>(I($"Copy symlink '{source}' is not allowed"))).Throw();
                    }

                    if (pip.Source.IsSourceFile)
                    {
                        // If the source file is not symlink, we rely on the dependents of copy file to call 'RestoreContentInCache'.

                        if (sourceContentInfo.Hash == WellKnownContentHashes.AbsentFile)
                        {
                            Logger.Log.PipCopyFileSourceFileDoesNotExist(operationContext, pipDescription, source, destination);
                            return PipResult.Create(PipResultStatus.Failed, startTime);
                        }

                        if (sourceContentInfo.Hash == WellKnownContentHashes.UntrackedFile)
                        {
                            Logger.Log.PipCopyFileFromUntrackableDir(operationContext, pipDescription, source, destination);
                            return PipResult.Create(PipResultStatus.Failed, startTime);
                        }
                    }
                    else
                    {
                        Contract.Assume(sourceContentInfo.Hash != WellKnownContentHashes.UntrackedFile);
                    }

                    bool shouldStoreOutputToCache = environment.Configuration.Schedule.StoreOutputsToCache || IsRewriteOutputFile(environment, pip.Destination);

                    // If the file is symlink and the chain is valid, the final target is a source file
                    // (otherwise, we would not have passed symlink chain validation).
                    // We now need to store the final target to the cache so it is available for file-level materialization downstream.
                    if (isSymLink && shouldStoreOutputToCache)
                    {
                        // We assume that source files cannot be made read-only so we use copy file materialization
                        // rather than hardlinking
                        var maybeStored = await environment.LocalDiskContentStore.TryStoreAsync(
                            environment.Cache.ArtifactContentCache,
                            fileRealizationModes: FileRealizationMode.Copy,
                            path: symlinkTarget,
                            tryFlushPageCacheToFileSystem: false,

                            // Trust the cache for content hash because we need the hash of the content of the target.
                            knownContentHash: null,

                            // Source should have been tracked by hash-source file pip or by CheckValidSymlinkChainAsync, no need to retrack.
                            trackPath: false);

                        if (!maybeStored.Succeeded)
                        {
                            maybeStored.Failure.Throw();
                        }

                        // save the content info of the final target
                        sourceContentInfo = maybeStored.Result.FileContentInfo;

                        var possiblyTracked = await TrackSymlinkChain(symlinkChain);
                        if (!possiblyTracked.Succeeded)
                        {
                            possiblyTracked.Failure.Throw();
                        }
                    }

                    // Just pass through the hash
                    environment.State.FileContentManager.ReportOutputContent(
                        operationContext,
                        pipDescription,
                        pip.Destination,
                        // TODO: Should we maintain the case of the source file?
                        FileMaterializationInfo.CreateWithUnknownName(sourceContentInfo),
                        PipOutputOrigin.NotMaterialized);

                    var result = PipResultStatus.NotMaterialized;
                    if (materializeOutputs || !shouldStoreOutputToCache)
                    {
                        // Materialize the outputs if specified
                        var maybeMaterialized = await environment.State.FileContentManager.TryMaterializeOutputsAsync(pip, operationContext);

                        if (!maybeMaterialized.Succeeded)
                        {
                            if (!shouldStoreOutputToCache)
                            {
                                result = await CopyAndTrackAsync(operationContext, environment, pip);

                                // Report again to notify the FileContentManager that the file has been materialized.
                                environment.State.FileContentManager.ReportOutputContent(
                                    operationContext,
                                    pipDescription,
                                    pip.Destination,
                                    // TODO: Should we maintain the case of the source file?
                                    FileMaterializationInfo.CreateWithUnknownName(sourceContentInfo),
                                    PipOutputOrigin.Produced);

                                var possiblyTracked = await TrackSymlinkChain(symlinkChain);
                                if (!possiblyTracked.Succeeded)
                                {
                                    possiblyTracked.Failure.Throw();
                                }
                            }
                            else
                            {
                                maybeMaterialized.Failure.Throw();
                            }
                        }
                        else
                        {
                            // No need to report pip output origin because TryMaterializeOutputAsync did that already through
                            // PlaceFileAsync of FileContentManager.

                            result = maybeMaterialized.Result.ToPipResult();
                        }
                    }

                    return new PipResult(
                        result,
                        PipExecutionPerformance.Create(result, startTime),
                        false,
                        // report accesses to symlink chain elements
                        symlinkChain,
                        ReadOnlyArray<AbsolutePath>.Empty);
                }
                catch (BuildXLException ex)
                {
                    Logger.Log.PipCopyFileFailed(operationContext, pipDescription, source, destination, ex);
                    return PipResult.Create(PipResultStatus.Failed, startTime);
                }
            }

            async Task<Possible<Unit>> TrackSymlinkChain(ReadOnlyArray<AbsolutePath> chain)
            {
                foreach (var chainElement in chain)
                {
                    var possiblyTracked = await environment.LocalDiskContentStore.TryTrackAsync(
                        FileArtifact.CreateSourceFile(chainElement),
                        environment.Configuration.Sandbox.FlushPageCacheToFileSystemOnStoringOutputsToCache,
                        ignoreKnownContentHashOnDiscoveringContent: true);

                    if (!possiblyTracked.Succeeded)
                    {
                        return possiblyTracked.Failure;
                    }
                }

                return Unit.Void;
            }
        }

        /// <summary>
        /// Checks whether a file forms a valid symlink chain.
        /// </summary>
        /// <remarks>
        /// A symlink chain is valid iff:
        /// (1) all target paths are valid paths
        /// (2) every element in the chain (except the head of the chain) is a source file (i.e., not produced during the build)
        /// </remarks>
        /// <returns>List of chain elements that 'source' points to (i.e., source is not included)</returns>
        private static Possible<ReadOnlyArray<AbsolutePath>> CheckValidSymlinkChainAsync(FileArtifact source, IPipExecutionEnvironment environment)
        {
            // check whether 'source' is a symlink
            // we are doing the check here using FileMaterializationInfo because 'source' might not be present on disk
            // (e.g., in case of lazyOutputMaterialization)
            var materializationInfo = environment.State.FileContentManager.GetInputContent(source);
            if (!materializationInfo.ReparsePointInfo.IsSymlink)
            {
                return ReadOnlyArray<AbsolutePath>.Empty;
            }

            var symlinkPath = source.Path;
            var maybeTarget = FileUtilities.ResolveSymlinkTarget(
                symlinkPath.ToString(environment.Context.PathTable),
                materializationInfo.ReparsePointInfo.GetReparsePointTarget());

            if (!maybeTarget.Succeeded)
            {
                return maybeTarget.Failure;
            }

            var symlinkTarget = maybeTarget.Result;

            // get the symlink chain starting at the source's target (i.e., the 2nd element of the chain formed by 'source')
            // all the elements in this sub-chain must be source files
            var openResult = FileUtilities.TryCreateOrOpenFile(
                symlinkTarget,
                FileDesiredAccess.GenericRead,
                FileShare.Read | FileShare.Delete,
                FileMode.Open,
                FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                out var handle);

            if (!openResult.Succeeded)
            {
                // we could not get a handle for the head of the sub-chain
                // it could be because the file/path does not exist
                // it might not exists because it's an output file and the file was not materialized -> invalid chain,
                // or because a symlink points to a missing file -> invalid chain
                return CreateInvalidChainFailure(I($"Failed to create a handle for a chain element ('{symlinkTarget}')"));
            }

            using (handle)
            {
                var chain = new List<AbsolutePath>();
                var symlinkChainElements = new List<string>();
                FileUtilities.GetChainOfReparsePoints(handle, symlinkTarget, symlinkChainElements);
                Contract.Assume(symlinkChainElements.Count > 0);

                // The existence of the last element in the chain returned by GetChainOfReparsePoints
                // is not guaranteed, so we need to check that the file is available.
                if (!FileUtilities.Exists(symlinkChainElements[symlinkChainElements.Count - 1]))
                {
                    return CreateInvalidChainFailure(I($"File does not exist ('{symlinkChainElements[symlinkChainElements.Count - 1]}')"));
                }

                foreach (string chainElement in symlinkChainElements)
                {
                    AbsolutePath.TryCreate(environment.Context.PathTable, chainElement, out var targetPath);

                    if (!targetPath.IsValid)
                    {
                        return CreateInvalidChainFailure(I($"Failed to parse an element of the chain ('{chainElement}')"));
                    }

                    chain.Add(targetPath);

                    var targetArtifact = environment.PipGraphView.TryGetLatestFileArtifactForPath(targetPath);
                    if (targetArtifact.IsValid && targetArtifact.IsOutputFile)
                    {
                        return CreateInvalidChainFailure(I($"An element of the chain ('{chainElement}') is a declared output of another pip."));
                    }

                    // If the file is not known to the graph, check whether the file is in an opaque or shared opaque directory.
                    // If it's inside such a directory, we treat it as an output file -> chain is not valid.
                    if (!targetArtifact.IsValid && environment.PipGraphView.IsPathUnderOutputDirectory(targetPath, out _))
                    {
                        return CreateInvalidChainFailure(I($"An element of the chain ('{chainElement}') is inside of an opaque directory."));
                    }
                }

                return ReadOnlyArray<AbsolutePath>.From(chain);
            }

            Failure<string> CreateInvalidChainFailure(string message, Failure innerFailure = null)
            {
                return new Failure<string>(I($"Invalid symlink chain ('{source.Path.ToString(environment.Context.PathTable)}' -> ...). {message}"), innerFailure);
            }
        }

        private static async Task<PipResultStatus> CopyAndTrackAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            CopyFile copyFile)
        {
            PathTable pathTable = environment.Context.PathTable;
            ExpandedAbsolutePath destination = copyFile.Destination.Path.Expand(pathTable);
            ExpandedAbsolutePath source = copyFile.Source.Path.Expand(pathTable);

            FileUtilities.CreateDirectory(Path.GetDirectoryName(destination.ExpandedPath));

            var copy = await FileUtilities.CopyFileAsync(source.ExpandedPath, destination.ExpandedPath);
            if (!copy)
            {
                (new Failure<string>(I($"Unable to copy from '{source}' to '{destination}'"))).Throw();
            }

            // if /storeOutputsToCache- was used, mark the destination here;
            // otherwise it will get marked in ReportFileArtifactPlaced.
            if (!environment.Configuration.Schedule.StoreOutputsToCache)
            {
                MakeSharedOpaqueOutputIfNeeded(environment, copyFile.Destination);
            }

            var mayBeTracked = await TrackPipOutputAsync(operationContext, environment, copyFile.Destination, isSymlink: false);

            if (!mayBeTracked.Succeeded)
            {
                mayBeTracked.Failure.Throw();
            }

            return PipResultStatus.Succeeded;
        }

        /// <summary>
        /// Writes the given <see cref="PipData"/> contents if the destination's content does not already match.
        /// </summary>
        public static async Task<PipResult> ExecuteWriteFileAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            WriteFile pip,
            bool materializeOutputs = true)
        {
            Contract.Requires(environment != null);
            Contract.Requires(pip != null);

            DateTime startTime = DateTime.UtcNow;
            return PipResult.Create(
                status:
                    FromPossibleResult(
                        await
                            TryExecuteWriteFileAsync(operationContext, environment, pip, materializeOutputs: materializeOutputs, reportOutputs: true)),
                executionStart: startTime);
        }

        private static PipResultStatus FromPossibleResult(Possible<PipResultStatus> possibleResult)
        {
            return possibleResult.Succeeded ? possibleResult.Result : PipResultStatus.Failed;
        }

        /// <summary>
        /// Writes the given <see cref="PipData"/> contents if the destination's content does not already match.
        /// </summary>
        public static async Task<Possible<PipResultStatus>> TryExecuteWriteFileAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            WriteFile pip,
            bool materializeOutputs,
            bool reportOutputs)
        {
            Contract.Requires(environment != null);
            Contract.Requires(pip != null);

            using (operationContext.StartOperation(PipExecutorCounter.WriteFileDuration))
            {
                // TODO: It'd be nice if PipData could instead write encoded bytes to a stream, in which case we could
                //       first compute the hash and then possibly do a second pass to actually write to a file (without allocating
                //       several possibly large buffers and strings).
                string contents = pip.Contents.ToString(environment.Context.PathTable);

                Encoding encoding;
                switch (pip.Encoding)
                {
                    case WriteFileEncoding.Utf8:
                        encoding = Encoding.UTF8;
                        break;
                    case WriteFileEncoding.Ascii:
                        encoding = Encoding.ASCII;
                        break;
                    default:
                        throw Contract.AssertFailure("Unexpected encoding");
                }

                Possible<PipResultStatus> writeFileStatus = await TryWriteFileAndReportOutputsAsync(
                    operationContext,
                    environment,
                    pip.Destination,
                    contents,
                    encoding,
                    pip,
                    materializeOutputs: materializeOutputs,
                    reportOutputs: reportOutputs);
                return writeFileStatus;
            }
        }

        /// <summary>
        /// Executes an Ipc pip
        /// </summary>
        public static async Task<ExecutionResult> ExecuteIpcAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            IpcPip pip)
        {
            var pathTable = environment.Context.PathTable;

            // ensure services are running
            bool ensureServicesRunning =
                await environment.State.ServiceManager.TryRunServiceDependenciesAsync(environment, pip.ServicePipDependencies, operationContext);
            if (!ensureServicesRunning)
            {
                Logger.Log.PipFailedDueToServicesFailedToRun(operationContext, pip.GetDescription(environment.Context));
                return ExecutionResult.GetFailureNotRunResult(operationContext);
            }

            // create IPC operation
            IIpcProvider ipcProvider = environment.IpcProvider;
            string monikerId = pip.IpcInfo.IpcMonikerId.ToString(pathTable.StringTable);
            string connectionString = ipcProvider.LoadAndRenderMoniker(monikerId);
            IClient client = ipcProvider.GetClient(connectionString, pip.IpcInfo.IpcClientConfig);

            var ipcOperationPayload = pip.MessageBody.ToString(environment.PipFragmentRenderer);
            var operation = new IpcOperation(ipcOperationPayload, waitForServerAck: true);

            // execute async
            IIpcResult ipcResult;
            using (operationContext.StartOperation(PipExecutorCounter.IpcSendAndHandleDuration))
            {
                // execute async
                ipcResult = await IpcSendAndHandleErrors(client, operation);
            }

            ExecutionResult executionResult = new ExecutionResult
            {
                MustBeConsideredPerpetuallyDirty = true,
            };

            if (ipcResult.Succeeded)
            {
                TimeSpan request_queueDuration = operation.Timestamp.Request_BeforeSendTime - operation.Timestamp.Request_BeforePostTime;
                TimeSpan request_sendDuration = operation.Timestamp.Request_AfterSendTime - operation.Timestamp.Request_BeforeSendTime;
                TimeSpan request_serverAckDuration = operation.Timestamp.Request_AfterServerAckTime - operation.Timestamp.Request_AfterSendTime;
                TimeSpan responseDuration = ipcResult.Timestamp.Response_BeforeDeserializeTime - operation.Timestamp.Request_AfterServerAckTime;

                TimeSpan response_deserializeDuration = ipcResult.Timestamp.Response_AfterDeserializeTime - ipcResult.Timestamp.Response_BeforeDeserializeTime;
                TimeSpan response_queueSetDuration = ipcResult.Timestamp.Response_BeforeSetTime - ipcResult.Timestamp.Response_AfterDeserializeTime;
                TimeSpan response_SetDuration = ipcResult.Timestamp.Response_AfterSetTime - ipcResult.Timestamp.Response_BeforeSetTime;
                TimeSpan response_AfterSetTaskDuration = DateTime.UtcNow - ipcResult.Timestamp.Response_AfterSetTime;

                environment.Counters.AddToCounter(
                    PipExecutorCounter.Ipc_RequestQueueDurationMs,
                    (long)request_queueDuration.TotalMilliseconds);

                environment.Counters.AddToCounter(
                    PipExecutorCounter.Ipc_RequestSendDurationMs,
                    (long)request_sendDuration.TotalMilliseconds);

                environment.Counters.AddToCounter(
                    PipExecutorCounter.Ipc_RequestServerAckDurationMs,
                    (long)request_serverAckDuration.TotalMilliseconds);

                environment.Counters.AddToCounter(
                    PipExecutorCounter.Ipc_ResponseDurationMs,
                    (long)responseDuration.TotalMilliseconds);

                environment.Counters.AddToCounter(
                    PipExecutorCounter.Ipc_ResponseDeserializeDurationMs,
                    (long)response_deserializeDuration.TotalMilliseconds);

                environment.Counters.AddToCounter(
                    PipExecutorCounter.Ipc_ResponseQueueSetDurationMs,
                    (long)response_queueSetDuration.TotalMilliseconds);

                environment.Counters.AddToCounter(
                    PipExecutorCounter.Ipc_ResponseSetDurationMs,
                    (long)response_SetDuration.TotalMilliseconds);

                environment.Counters.AddToCounter(
                    PipExecutorCounter.Ipc_ResponseAfterSetTaskDurationMs,
                    (long)response_AfterSetTaskDuration.TotalMilliseconds);

                if (environment.Configuration.Schedule.WriteIpcOutput)
                {
                    // write payload to pip.OutputFile
                    Possible<PipResultStatus> writeFileStatus = await TryWriteFileAndReportOutputsAsync(
                        operationContext,
                        environment,
                        FileArtifact.CreateOutputFile(pip.OutputFile.Path),
                        ipcResult.Payload,
                        Encoding.UTF8,
                        pip,
                        executionResult,
                        materializeOutputs: true,
                        logErrors: true);

                    executionResult.SetResult(operationContext, writeFileStatus.Succeeded ? writeFileStatus.Result : PipResultStatus.Failed);
                }
                else
                {
                    // Use absent file when write IPC output is disabled.
                    var absentFileInfo = FileMaterializationInfo.CreateWithUnknownLength(WellKnownContentHashes.AbsentFile);

                    // Report output content in result
                    executionResult.ReportOutputContent(pip.OutputFile, absentFileInfo, PipOutputOrigin.NotMaterialized);
                    executionResult.SetResult(operationContext, PipResultStatus.NotMaterialized);
                }
            }
            else
            {
                // log error if execution failed
                if (ipcResult.ExitCode == IpcResultStatus.InvalidInput)
                {
                    // we separate the 'invalid input' errors here, so they can be classified as 'user errors'
                    Logger.Log.PipIpcFailedDueToInvalidInput(
                        operationContext,
                        operation.Payload,
                        connectionString,
                        ipcResult.Payload);
                }
                else
                {
                    Logger.Log.PipIpcFailed(
                        operationContext,
                        operation.Payload,
                        connectionString,
                        ipcResult.ExitCode.ToString(),
                        ipcResult.Payload);
                }

                executionResult.SetResult(operationContext, PipResultStatus.Failed);
            }

            executionResult.Seal();
            return executionResult;
        }

        private static async Task<IIpcResult> IpcSendAndHandleErrors(IClient client, IIpcOperation operation)
        {
            try
            {
                // this should never throw, but to be extra safe we wrap this in try/catch.
                return await client.Send(operation);
            }
            catch (Exception e)
            {
                return new IpcResult(IpcResultStatus.TransmissionError, e.ToStringDemystified());
            }
        }

        private static void MakeSharedOpaqueOutputIfNeeded(IPipExecutionEnvironment environment, AbsolutePath path)
        {
            if (environment.PipGraphView.IsPathUnderOutputDirectory(path, out bool isItSharedOpaque) && isItSharedOpaque)
            {
                string expandedPath = path.ToString(environment.Context.PathTable);
                SharedOpaqueOutputHelper.EnforceFileIsSharedOpaqueOutput(expandedPath);
            }
        }

        /// <summary>
        /// Writes <paramref name="contents"/> to disk at location <paramref name="destinationFile"/> using
        /// <paramref name="encoding"/>.
        ///
        /// If writing to disk succeeds, reports the produced output to the environment (<see cref="FileContentManager.ReportOutputContent"/>).
        ///
        /// Catches any <see cref="BuildXLException"/> and logs an error when that happens.
        /// </summary>
        private static async Task<Possible<PipResultStatus>> TryWriteFileAndReportOutputsAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            FileArtifact destinationFile,
            string contents,
            Encoding encoding,
            Pip producerPip,
            ExecutionResult executionResult = null,
            bool materializeOutputs = true,
            bool logErrors = true,
            bool reportOutputs = true)
        {
            var context = environment.Context;
            var pathTable = context.PathTable;
            var pipInfo = new PipInfo(producerPip, context);
            var pipDescription = pipInfo.Description;
            var fileContentManager = environment.State.FileContentManager;

            var destinationAsString = destinationFile.Path.ToString(pathTable);
            byte[] encoded = encoding.GetBytes(contents);

            try
            {
                ContentHash contentHash;
                PipOutputOrigin outputOrigin;
                FileMaterializationInfo fileContentInfo;

                using (operationContext.StartOperation(PipExecutorCounter.WriteFileHashingDuration))
                {
                    // No need to hash the file if it is already registered with the file content manager.
                    if (!fileContentManager.TryGetInputContent(destinationFile, out fileContentInfo))
                    {
                        contentHash = ContentHashingUtilities.HashBytes(encoded);
                    }
                    else
                    {
                        contentHash = fileContentInfo.Hash;
                    }
                }

                if (materializeOutputs)
                {
                    string directoryName = ExceptionUtilities.HandleRecoverableIOException(
                        () => Path.GetDirectoryName(destinationAsString),
                        ex => { throw new BuildXLException("Cannot get directory name", ex); });
                    FileUtilities.CreateDirectory(directoryName);

                    Possible<ContentMaterializationResult>? possiblyMaterialized = null;
                    if (environment.Configuration.Distribution.BuildRole == DistributedBuildRoles.None)
                    {
                        // Optimistically check to see if the file is already in the cache. If so we can just exit
                        // TFS 929846 prevents us from utilizing this optimization on distributed builds since the pin doesn't
                        // flow through to the remote when it is successful on the local. That means that files aren't guaranteed
                        // to be available on other machines.
                        possiblyMaterialized = await environment.LocalDiskContentStore.TryMaterializeAsync(
                                environment.Cache.ArtifactContentCache,
                                GetFileRealizationMode(environment),
                                destinationFile,
                                contentHash);
                    }

                    if (possiblyMaterialized.HasValue && possiblyMaterialized.Value.Succeeded)
                    {
                        outputOrigin = possiblyMaterialized.Value.Result.Origin.ToPipOutputOriginHidingDeploymentFromCache();
                        fileContentInfo = possiblyMaterialized.Value.Result.TrackedFileContentInfo.FileMaterializationInfo;
                    }
                    else
                    {
                        bool fileWritten = await FileUtilities.WriteAllBytesAsync(destinationAsString, encoded);
                        Contract.Assume(
                            fileWritten,
                            "WriteAllBytes only returns false when the predicate parameter (not supplied) fails. Otherwise it should throw a BuildXLException and be handled below.");

                        bool shouldStoreOutputsToCache = environment.Configuration.Schedule.StoreOutputsToCache || IsRewriteOutputFile(environment, destinationFile);

                        var possiblyStored = shouldStoreOutputsToCache
                            ? await environment.LocalDiskContentStore.TryStoreAsync(
                                environment.Cache.ArtifactContentCache,
                                GetFileRealizationMode(environment),
                                destinationFile,
                                tryFlushPageCacheToFileSystem: environment.Configuration.Sandbox.FlushPageCacheToFileSystemOnStoringOutputsToCache,
                                knownContentHash: contentHash,
                                isSymlink: false)
                            : await TrackPipOutputAsync(operationContext, environment, destinationFile, isSymlink: false);

                        if (!possiblyStored.Succeeded)
                        {
                            throw possiblyStored.Failure.Throw();
                        }

                        outputOrigin = PipOutputOrigin.Produced;
                        fileContentInfo = possiblyStored.Result.FileMaterializationInfo;
                    }
                }
                else
                {
                    outputOrigin = PipOutputOrigin.NotMaterialized;
                    fileContentInfo = FileMaterializationInfo.CreateWithUnknownName(new FileContentInfo(contentHash, encoded.Length));
                }

                if (reportOutputs)
                {
                    if (executionResult != null)
                    {
                        // IPC pips specify an execution result which is reported back to the scheduler
                        // which then reports the output content to the file content manager on the worker
                        // and master machines in distributed builds
                        executionResult.ReportOutputContent(destinationFile, fileContentInfo, outputOrigin);
                    }
                    else
                    {
                        // Write file pips do not specify execution result since they are not distributed
                        // (i.e. they only run on the master). Given that, they report directly to the file content manager. 
                        fileContentManager.ReportOutputContent(
                            operationContext,
                            pipDescription,
                            destinationFile,
                            fileContentInfo,
                            outputOrigin);
                    }
                }

                MakeSharedOpaqueOutputIfNeeded(environment, destinationFile.Path);

                return outputOrigin.ToPipResult();
            }
            catch (BuildXLException ex)
            {
                if (logErrors)
                {
                    Logger.Log.PipWriteFileFailed(operationContext, pipDescription, destinationAsString, ex);
                    return PipResultStatus.Failed;
                }
                else
                {
                    return new Failure<string>(Logger.PipWriteFileFailedMessage(pipDescription, destinationAsString, ex));
                }
            }
        }

        /// <summary>
        /// Analyze pip violations and store two-phase cache entry.
        /// </summary>
        public static async Task<ExecutionResult> PostProcessExecution(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            CacheableProcess cacheableProcess,
            ExecutionResult processExecutionResult)
        {
            Contract.Requires(environment != null);
            Contract.Requires(cacheableProcess != null);

            var process = cacheableProcess.Process;
            processExecutionResult.Seal();

            PipResultStatus status = processExecutionResult.Result;
            StoreCacheEntryResult storeCacheEntryResult = StoreCacheEntryResult.Succeeded;

            using (operationContext.StartOperation(PipExecutorCounter.StoreProcessToCacheDurationMs, details: processExecutionResult.TwoPhaseCachingInfo?.ToString()))
            {
                if (processExecutionResult.TwoPhaseCachingInfo != null)
                {
                    storeCacheEntryResult = await StoreTwoPhaseCacheEntryAsync(
                        operationContext,
                        process,
                        cacheableProcess,
                        environment,
                        state,
                        processExecutionResult.TwoPhaseCachingInfo);

                    if (storeCacheEntryResult.Converged && !IsProcessPreservingOutputs(environment, process))
                    {
                        environment.Counters.IncrementCounter(PipExecutorCounter.ProcessPipTwoPhaseCacheEntriesConverged);

                        // Copy the status into the result, if the pip was successful, it will remain so, if the pip
                        // failed during fingerprint storage we want that status,
                        // and finally, the pip can have its status converted from executed to run from cache
                        // if determinism recovery happened and the cache forced convergence.
                        processExecutionResult = processExecutionResult.CreateSealedConvergedExecutionResult(storeCacheEntryResult.ConvergedExecutionResult);
                    }
                    else
                    {
                        environment.Counters.IncrementCounter(PipExecutorCounter.ProcessPipTwoPhaseCacheEntriesAdded);
                    }
                }
            }

            return processExecutionResult;
        }

        /// <summary>
        /// Report results from given execution result to the environment and file content manager
        /// </summary>
        internal static void ReportExecutionResultOutputContent(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            string pipDescription,
            ExecutionResult processExecutionResult,
            bool doubleWriteErrorsAreWarnings = false)
        {
            PipOutputOrigin? overrideOutputOrigin = null;
            if (processExecutionResult.Result == PipResultStatus.NotMaterialized)
            {
                overrideOutputOrigin = PipOutputOrigin.NotMaterialized;
            }

            foreach (var (directoryArtifact, fileArtifactArray) in processExecutionResult.DirectoryOutputs)
            {
                environment.State.FileContentManager.ReportDynamicDirectoryContents(
                    directoryArtifact,
                    fileArtifactArray,
                    overrideOutputOrigin ?? PipOutputOrigin.Produced);
            }

            foreach (var output in processExecutionResult.OutputContent)
            {
                environment.State.FileContentManager.ReportOutputContent(
                    operationContext,
                    pipDescription,
                    output.fileArtifact,
                    output.fileInfo,
                    overrideOutputOrigin ?? output.Item3,
                    doubleWriteErrorsAreWarnings);
            }

            if (processExecutionResult.NumberOfWarnings > 0)
            {
                environment.ReportWarnings(fromCache: false, count: processExecutionResult.NumberOfWarnings);
            }
        }

        /// <summary>
        /// Analyze process file access violations
        /// </summary>
        internal static ExecutionResult AnalyzeFileAccessViolations(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            ExecutionResult processExecutionResult,
            Process process,
            out bool pipIsSafeToCache,
            out IReadOnlyDictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)> allowedSameContentDoubleWriteViolations)
        {
            pipIsSafeToCache = true;

            using (operationContext.StartOperation(PipExecutorCounter.AnalyzeFileAccessViolationsDuration))
            {
                var analyzePipViolationsResult = AnalyzePipViolationsResult.NoViolations;
                allowedSameContentDoubleWriteViolations = CollectionUtilities.EmptyDictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)>();

                var exclusiveOpaqueDirectories = processExecutionResult.DirectoryOutputs.Where(directoryArtifactWithContent => !directoryArtifactWithContent.directoryArtifact.IsSharedOpaque).ToReadOnlyArray();

                // Regardless of if we will fail the pip or not, maybe analyze them for higher-level dependency violations.
                if (processExecutionResult.FileAccessViolationsNotWhitelisted != null
                    || processExecutionResult.WhitelistedFileAccessViolations != null
                    || processExecutionResult.SharedDynamicDirectoryWriteAccesses != null
                    || exclusiveOpaqueDirectories != null
                    || processExecutionResult.AllowedUndeclaredReads != null
                    || processExecutionResult.AbsentPathProbesUnderOutputDirectories != null)
                {
                    analyzePipViolationsResult = environment.FileMonitoringViolationAnalyzer.AnalyzePipViolations(
                        process,
                        processExecutionResult.FileAccessViolationsNotWhitelisted,
                        processExecutionResult.WhitelistedFileAccessViolations,
                        exclusiveOpaqueDirectories,
                        processExecutionResult.SharedDynamicDirectoryWriteAccesses,
                        processExecutionResult.AllowedUndeclaredReads,
                        processExecutionResult.AbsentPathProbesUnderOutputDirectories,
                        processExecutionResult.OutputContent,
                        out allowedSameContentDoubleWriteViolations);
                }

                if (!analyzePipViolationsResult.IsViolationClean)
                {
                    Contract.Assume(operationContext.LoggingContext.ErrorWasLogged, "Error should have been logged by FileMonitoringViolationAnalyzer");
                    processExecutionResult = processExecutionResult.CloneSealedWithResult(PipResultStatus.Failed);
                }

                pipIsSafeToCache = analyzePipViolationsResult.PipIsSafeToCache;

                return processExecutionResult;
            }
        }

        /// <summary>
        /// Analyze process double write violations after the cache converged outputs
        /// </summary>
        internal static ExecutionResult AnalyzeDoubleWritesOnCacheConvergence(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            ExecutionResult processExecutionResult,
            Process process,
            IReadOnlyDictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)> allowedSameContentDoubleWriteViolations)
        {
            using (operationContext.StartOperation(PipExecutorCounter.AnalyzeFileAccessViolationsDuration))
            {
                var analyzePipViolationsResult = AnalyzePipViolationsResult.NoViolations;

                if (allowedSameContentDoubleWriteViolations.Count > 0)
                {
                    analyzePipViolationsResult = environment.FileMonitoringViolationAnalyzer.AnalyzeDoubleWritesOnCacheConvergence(
                        process,
                        processExecutionResult.OutputContent,
                        allowedSameContentDoubleWriteViolations);
                }

                if (!analyzePipViolationsResult.IsViolationClean)
                {
                    Contract.Assume(operationContext.LoggingContext.ErrorWasLogged, "Error should have been logged by FileMonitoringViolationAnalyzer");
                    processExecutionResult = processExecutionResult.CloneSealedWithResult(PipResultStatus.Failed);
                }

                return processExecutionResult;
            }
        }

        /// <summary>
        /// Run process from cache and replay warnings.
        /// </summary>
        public static async Task<ExecutionResult> RunFromCacheWithWarningsAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            Process pip,
            RunnableFromCacheResult runnableFromCacheCheckResult,
            string processDescription)
        {
            using (operationContext.StartOperation(PipExecutorCounter.RunProcessFromCacheDuration))
            {
                RunnableFromCacheResult.CacheHitData cacheHitData = runnableFromCacheCheckResult.GetCacheHitData();
                Logger.Log.ScheduleProcessPipCacheHit(
                    operationContext,
                    processDescription,
                    runnableFromCacheCheckResult.Fingerprint.ToString(),
                    cacheHitData.Metadata.Id);

                ExecutionResult executionResult = GetCacheHitExecutionResult(operationContext, environment, pip, runnableFromCacheCheckResult);
                executionResult.Seal();

                // File access violation analysis must be run before reporting the execution result output content.
                var exclusiveOpaqueContent = executionResult.DirectoryOutputs.Where(directoryArtifactWithContent => !directoryArtifactWithContent.directoryArtifact.IsSharedOpaque).ToReadOnlyArray();

                if ((executionResult.SharedDynamicDirectoryWriteAccesses?.Count > 0 || executionResult.AllowedUndeclaredReads?.Count > 0 || executionResult.AbsentPathProbesUnderOutputDirectories?.Count > 0 || exclusiveOpaqueContent.Length > 0)
                    && !environment.FileMonitoringViolationAnalyzer.AnalyzeDynamicViolations(
                            pip,
                            exclusiveOpaqueContent,
                            executionResult.SharedDynamicDirectoryWriteAccesses,
                            executionResult.AllowedUndeclaredReads,
                            executionResult.AbsentPathProbesUnderOutputDirectories,
                            executionResult.OutputContent))
                {
                    Contract.Assume(operationContext.LoggingContext.ErrorWasLogged, "Error should have been logged by FileMonitoringViolationAnalyzer");
                    return executionResult.CloneSealedWithResult(PipResultStatus.Failed);
                }

                ReportExecutionResultOutputContent(
                    operationContext,
                    environment,
                    processDescription,
                    executionResult,
                    pip.PipType == PipType.Process ? ((Process)pip).DoubleWritePolicy.ImpliesDoubleWriteIsWarning() : false);

                if (cacheHitData.Metadata.NumberOfWarnings > 0 && environment.Configuration.Logging.ReplayWarnings)
                {
                    Logger.Log.PipWarningsFromCache(
                        operationContext,
                        processDescription,
                        cacheHitData.Metadata.NumberOfWarnings);

                    await ReplayWarningsFromCacheAsync(operationContext, environment, state, pip, cacheHitData);
                }

                return executionResult;
            }
        }

        /// <summary>
        /// Execute a service start or shutdown pip
        /// </summary>
        /// <param name="operationContext">Current logging context</param>
        /// <param name="environment">The pip environment</param>
        /// <param name="pip">The pip to execute</param>
        /// <param name="processIdListener">Callback to call when the process is actually started</param>
        /// <returns>A task that returns the execution restult when done</returns>
        internal static async Task<ExecutionResult> ExecuteServiceStartOrShutdownAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            Process pip,
            Action<int> processIdListener = null)
        {
            // TODO: Try to materialize dependencies. This is not needed in the normal case because
            // scheduler has explicit MaterializeInputs step for pips which it schedules
            using (operationContext.StartOperation(PipExecutorCounter.ServiceInputMaterializationDuration))
            {
                // ensure dependencies materialized
                var materializationResult = await MaterializeInputsAsync(operationContext, environment, pip);
                if (materializationResult.IndicatesFailure())
                {
                    return ExecutionResult.GetFailureNotRunResult(operationContext);
                }
            }

            var result = await ExecuteProcessAsync(
                operationContext,
                environment,
                environment.State.GetScope(pip),
                pip,
                fingerprint: null,
                processIdListener: processIdListener);

            result.Seal();
            return result;
        }

        /// <summary>
        /// Execute a process pip
        /// </summary>
        /// <param name="operationContext">Current logging context</param>
        /// <param name="environment">The pip environment</param>
        /// <param name="state">the pip scoped execution state</param>
        /// <param name="pip">The pip to execute</param>
        /// <param name="fingerprint">The pip fingerprint</param>
        /// <param name="processIdListener">Callback to call when the process is actually started</param>
        /// <param name="expectedRamUsageMb">the expected ram usage for the process in megabytes</param>
        /// <returns>A task that returns the execution result when done</returns>
        public static async Task<ExecutionResult> ExecuteProcessAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            Process pip,

            // TODO: This should be removed, or should become a WeakContentFingerprint
            ContentFingerprint? fingerprint,
            Action<int> processIdListener = null,
            int expectedRamUsageMb = 0)
        {
            var context = environment.Context;
            var counters = environment.Counters;
            var configuration = environment.Configuration;
            var pathTable = context.PathTable;
            var processExecutionResult = new ExecutionResult();
            if (fingerprint.HasValue)
            {
                processExecutionResult.WeakFingerprint = new WeakContentFingerprint(fingerprint.Value.Hash);
            }

            // Pips configured to disable cache lookup must be set to being perpetually dirty to ensure incremental scheduling
            // gets misses
            if (pip.DisableCacheLookup)
            {
                processExecutionResult.MustBeConsideredPerpetuallyDirty = true;
            }

            string processDescription = pip.GetDescription(context);

            using (operationContext.StartOperation(PipExecutorCounter.RunServiceDependenciesDuration))
            {
                bool ensureServicesRunning =
                    await environment.State.ServiceManager.TryRunServiceDependenciesAsync(environment, pip.ServicePipDependencies, operationContext);
                if (!ensureServicesRunning)
                {
                    Logger.Log.PipFailedDueToServicesFailedToRun(operationContext, processDescription);
                    return ExecutionResult.GetFailureNotRunResult(operationContext);
                }
            }

            // When preserving outputs, we need to make sure to remove any hardlinks to the cache.
            Func<FileArtifact, Task<bool>> makeOutputPrivate =
                async artifactNeededPrivate =>
                {
                    string originalPath = artifactNeededPrivate.Path.ToString(pathTable);

                    try
                    {
                        if (!FileUtilities.FileExistsNoFollow(originalPath))
                        {
                            // Output file doesn't exist. No need to make it private, 
                            // but return false so BuildXL ensures the output directory is created.
                            return false;
                        }

                        if (FileUtilities.GetHardLinkCount(originalPath) == 1 &&
                            FileUtilities.HasWritableAccessControl(originalPath))
                        {
                            // Output file is already private. File will not be deleted.
                            return true;
                        }

                        // We want to use a temp filename that's as short as the original filename.
                        // To achieve this, we use the original filename and the PathId which is unique across all files in the build. 
                        // This ensures uniquness, keeps the temp file as short as the original, and tends to keep the file in the same directory 
                        // as the original.
                        var maybePrivate = await FileUtilities.TryMakeExclusiveLinkAsync(
                            artifactNeededPrivate.Path.ToString(pathTable),
                            optionalTemporaryFileName: artifactNeededPrivate.Path.Value.Value.ToString(CultureInfo.InvariantCulture),
                            preserveOriginalTimestamp: true);

                        if (!maybePrivate.Succeeded)
                        {
                            maybePrivate.Failure.Throw();
                        }

                        return true;
                    }
                    catch (BuildXLException ex)
                    {
                        Logger.Log.PreserveOutputsFailedToMakeOutputPrivate(
                            operationContext,
                            processDescription,
                            originalPath,
                            ex.GetLogEventMessage());
                        return false;
                    }
                };

            // To do in-place rewrites, we need to make writable, private copies of inputs to be rewritten (they may be read-only hardlinks into the cache, for example).
            Func<FileArtifact, Task<bool>> makeInputPrivate =
                async artifactNeededPrivate =>
                {
                    FileMaterializationInfo inputMaterializationInfo =
                        environment.State.FileContentManager.GetInputContent(artifactNeededPrivate);

                    if (inputMaterializationInfo.ReparsePointInfo.IsSymlink)
                    {
                        // Do nothing in case of re-writing a symlink --- a process can safely change
                        // symlink's target since it won't affect things in CAS.
                        return true;
                    }

                    ContentHash artifactHash = inputMaterializationInfo.Hash;

                    // Source files aren't guaranteed in cache, until we first have a reason to ingress them.
                    // Note that this is only relevant for source files rewritten in place, which is only
                    // used in some team-internal trace-conversion scenarios as of writing.
                    if (artifactNeededPrivate.IsSourceFile)
                    {
                        // We assume that source files cannot be made read-only so we use copy file materialization
                        // rather than ever hardlinking
                        var maybeStored = await environment.LocalDiskContentStore.TryStoreAsync(
                            environment.Cache.ArtifactContentCache,
                            fileRealizationModes: FileRealizationMode.Copy,
                            path: artifactNeededPrivate.Path,
                            tryFlushPageCacheToFileSystem: false,
                            knownContentHash: artifactHash,

                            // Source should have been tracked by hash-source file pip, no need to retrack.
                            trackPath: false,
                            isSymlink: false);

                        if (!maybeStored.Succeeded)
                        {
                            Logger.Log.StorageCacheIngressFallbackContentToMakePrivateError(
                                operationContext,
                                contentHash: artifactHash.ToHex(),
                                fallbackPath:
                                    artifactNeededPrivate.Path.ToString(pathTable),
                                errorMessage: maybeStored.Failure.DescribeIncludingInnerFailures());
                            return false;
                        }
                    }

                    // We need a private version of the output - it must be writable and have link count 1.
                    // We can achieve that property by forcing a copy of the content (by hash) out of cache.
                    // The content should be in the cache in usual cases. See special case above for source-file rewriting
                    // (should not be common; only used in some trace-conversion scenarios as of writing).
                    var maybeMadeWritable =
                        await
                            environment.LocalDiskContentStore
                                .TryMaterializeTransientWritableCopyAsync(
                                    environment.Cache.ArtifactContentCache,
                                    artifactNeededPrivate.Path,
                                    artifactHash);

                    if (!maybeMadeWritable.Succeeded)
                    {
                        Logger.Log.StorageCacheGetContentError(
                            operationContext,
                            contentHash: artifactHash.ToHex(),
                            destinationPath:
                                artifactNeededPrivate.Path.ToString(pathTable),
                            errorMessage:
                                maybeMadeWritable.Failure.DescribeIncludingInnerFailures());
                        return false;
                    }

                    return true;
                };

            SemanticPathExpander semanticPathExpander = state.PathExpander;

            var processMonitoringLogger = new ProcessExecutionMonitoringLogger(operationContext, pip, context, environment.State.ExecutionLog);

            // Service related pips cannot be cancelled
            bool allowResourceBasedCancellation = pip.ServiceInfo == null || pip.ServiceInfo.Kind == ServicePipKind.None;

            // Execute the process when resources are available
            SandboxedProcessPipExecutionResult executionResult = await environment.State.ResourceManager
                .ExecuteWithResources(
                    operationContext,
                    pip.PipId,
                    expectedRamUsageMb,
                    allowResourceBasedCancellation,
                    async (resourceLimitCancellationToken, registerQueryRamUsageMb) =>
                    {
                        // Inner cancellation token source for tracking cancellation time
                        using (var innerResourceLimitCancellationTokenSource = new CancellationTokenSource())
                        using (operationContext.StartOperation(PipExecutorCounter.ProcessPossibleRetryWallClockDuration))
                        {
                            int lastObservedPeakRamUsage = 0;
                            TimeSpan? cancellationStartTime = null;
                            resourceLimitCancellationToken.Register(
                                () =>
                                {
                                    cancellationStartTime = TimestampUtilities.Timestamp;
                                    Logger.Log.StartCancellingProcessPipExecutionDueToResourceExhaustion(
                                        operationContext,
                                        processDescription,
                                        (long)(operationContext.Duration?.TotalMilliseconds ?? -1),
                                        peakMemoryMb: lastObservedPeakRamUsage,
                                        expectedMemoryMb: expectedRamUsageMb);

                                    using (operationContext.StartAsyncOperation(PipExecutorCounter.ResourceLimitCancelProcessDuration))
                                    {
                                        innerResourceLimitCancellationTokenSource.Cancel();
                                    }
                                });

                            int remainingUserRetries = pip.RetryExitCodes.Length > 0 ? configuration.Schedule.ProcessRetries : 0;
                            int remainingInternalSandboxedProcessExecutionFailureRetries = InternalSandboxedProcessExecutionFailureRetryCountMax;

                            int retryCount = 0;
                            SandboxedProcessPipExecutionResult result;

                            // Retry pip count up to limit if we produce result without detecting file access.
                            // There are very rare cases where a child process is started not Detoured and we don't observe any file accesses from such process.
                            while (true)
                            {
                                lastObservedPeakRamUsage = 0;

                                var executor = new SandboxedProcessPipExecutor(
                                    context,
                                    operationContext.LoggingContext,
                                    pip,
                                    configuration.Sandbox,
                                    configuration.Layout,
                                    configuration.Logging,
                                    environment.RootMappings,
                                    environment.ProcessInContainerManager,
                                    state.FileAccessWhitelist,
                                    makeInputPrivate,
                                    makeOutputPrivate,
                                    semanticPathExpander,
                                    configuration.Engine.DisableConHostSharing,
                                    pipEnvironment: environment.State.PipEnvironment,
                                    validateDistribution: configuration.Distribution.ValidateDistribution,
                                    directoryArtifactContext: new DirectoryArtifactContext(environment),
                                    logger: processMonitoringLogger,
                                    processIdListener: processIdListener,
                                    pipDataRenderer: environment.PipFragmentRenderer,
                                    buildEngineDirectory: configuration.Layout.BuildEngineDirectory,
                                    directoryTranslator: environment.DirectoryTranslator,
                                    remainingUserRetryCount: remainingUserRetries,
                                    vmInitializer: environment.VmInitializer);

                                registerQueryRamUsageMb(
                                    () =>
                                    {
                                        using (operationContext.StartAsyncOperation(PipExecutorCounter.QueryRamUsageDuration))
                                        {
                                            lastObservedPeakRamUsage =
                                                (int)ByteSizeFormatter.ToMegabytes((long)(executor.GetActivePeakMemoryUsage() ?? 0));
                                        }

                                        return lastObservedPeakRamUsage;
                                    });

                                // Increment the counters only on the first try.
                                if (retryCount == 0)
                                {
                                    counters.IncrementCounter(PipExecutorCounter.ExternalProcessCount);
                                    environment.SetMaxExternalProcessRan();
                                }

                                result = await executor.RunAsync(innerResourceLimitCancellationTokenSource.Token, sandboxedKextConnection: environment.SandboxedKextConnection);

                                ++retryCount;

                                lock (s_telemetryDetoursHeapLock)
                                {
                                    if (counters.GetCounterValue(PipExecutorCounter.MaxDetoursHeapInBytes) <
                                        result.MaxDetoursHeapSizeInBytes)
                                    {
                                        // Zero out the counter first and then set the new value.
                                        counters.AddToCounter(
                                            PipExecutorCounter.MaxDetoursHeapInBytes,
                                            -counters.GetCounterValue(PipExecutorCounter.MaxDetoursHeapInBytes));
                                        counters.AddToCounter(
                                            PipExecutorCounter.MaxDetoursHeapInBytes,
                                            result.MaxDetoursHeapSizeInBytes);
                                    }
                                }

                                if (result.Status == SandboxedProcessPipExecutionStatus.OutputWithNoFileAccessFailed ||
                                    result.Status == SandboxedProcessPipExecutionStatus.MismatchedMessageCount)
                                {
                                    if (remainingInternalSandboxedProcessExecutionFailureRetries > 0)
                                    {
                                        --remainingInternalSandboxedProcessExecutionFailureRetries;

                                        switch (result.Status)
                                        {
                                            case SandboxedProcessPipExecutionStatus.OutputWithNoFileAccessFailed:
                                                counters.IncrementCounter(PipExecutorCounter.OutputsWithNoFileAccessRetriesCount);
                                                break;

                                            case SandboxedProcessPipExecutionStatus.MismatchedMessageCount:
                                                counters.IncrementCounter(PipExecutorCounter.MismatchMessageRetriesCount);
                                                break;

                                            default:
                                                Contract.Assert(false, "Unexpected result error type.");
                                                break;
                                        }

                                        continue;
                                    }

                                    switch (result.Status)
                                    {
                                        case SandboxedProcessPipExecutionStatus.OutputWithNoFileAccessFailed:
                                            Logger.Log.FailPipOutputWithNoAccessed(
                                                operationContext,
                                                pip.SemiStableHash,
                                                processDescription);
                                            break;

                                        case SandboxedProcessPipExecutionStatus.MismatchedMessageCount:
                                            Logger.Log.LogMismatchedDetoursErrorCount(
                                                operationContext,
                                                pip.SemiStableHash,
                                                processDescription);
                                            break;

                                        default:
                                            Contract.Assert(false, "Unexpected result error type gotten.");
                                            break;
                                    }

                                    // Just break the loop below. The result is already set properly.
                                }

                                if (result.Status == SandboxedProcessPipExecutionStatus.ShouldBeRetriedDueToExitCode)
                                {
                                    Contract.Assert(remainingUserRetries > 0);

                                    --remainingUserRetries;
                                    Logger.Log.PipWillBeRetriedDueToExitCode(
                                        operationContext,
                                        pip.SemiStableHash,
                                        processDescription,
                                        result.ExitCode,
                                        remainingUserRetries);
                                    counters.IncrementCounter(PipExecutorCounter.ProcessUserRetries);

                                    continue;
                                }

                                break;
                            }

                            counters.DecrementCounter(PipExecutorCounter.ExternalProcessCount);

                            if (result.Status == SandboxedProcessPipExecutionStatus.Canceled)
                            {
                                if (resourceLimitCancellationToken.IsCancellationRequested)
                                {
                                    TimeSpan? cancelTime = TimestampUtilities.Timestamp - cancellationStartTime;

                                    counters.IncrementCounter(PipExecutorCounter.ProcessRetriesDueToResourceLimits);
                                    Logger.Log.CancellingProcessPipExecutionDueToResourceExhaustion(
                                        operationContext,
                                        processDescription,
                                        (long)(operationContext.Duration?.TotalMilliseconds ?? -1),
                                        peakMemoryMb:
                                            (int)ByteSizeFormatter.ToMegabytes((long)(result.JobAccountingInformation?.PeakMemoryUsage ?? 0)),
                                        expectedMemoryMb: expectedRamUsageMb,
                                        cancelMilliseconds: (int)(cancelTime?.TotalMilliseconds ?? 0));
                                }
                            }

                            return result;
                        }
                    });

            processExecutionResult.ReportSandboxedExecutionResult(executionResult);

            counters.AddToCounter(PipExecutorCounter.SandboxedProcessPrepDurationMs, executionResult.SandboxPrepMs);
            counters.AddToCounter(
                PipExecutorCounter.SandboxedProcessProcessResultDurationMs,
                executionResult.ProcessSandboxedProcessResultMs);
            counters.AddToCounter(PipExecutorCounter.ProcessStartTimeMs, executionResult.ProcessStartTimeMs);

            // We may have some violations reported already (outright denied by the sandbox manifest).
            FileAccessReportingContext fileAccessReportingContext = executionResult.UnexpectedFileAccesses;

            if (executionResult.Status == SandboxedProcessPipExecutionStatus.PreparationFailed)
            {
                // Preparation failures provide minimal feedback.
                // We do not have any execution-time information (observed accesses or file monitoring violations) to analyze.
                processExecutionResult.SetResult(operationContext, PipResultStatus.Failed);

                counters.IncrementCounter(PipExecutorCounter.PreparationFailureCount);

                if (executionResult.NumberOfProcessLaunchRetries > 0)
                {
                    counters.IncrementCounter(PipExecutorCounter.PreparationFailurePartialCopyCount);
                }

                return processExecutionResult;
            }

            if (executionResult.Status == SandboxedProcessPipExecutionStatus.Canceled)
            {
                // Don't do post processing if canceled
                processExecutionResult.SetResult(operationContext, PipResultStatus.Canceled);

                ReportFileAccesses(processExecutionResult, fileAccessReportingContext);

                counters.AddToCounter(
                    PipExecutorCounter.CanceledProcessExecuteDuration,
                    executionResult.PrimaryProcessTimes.TotalWallClockTime);

                return processExecutionResult;
            }

            // These are the results we know how to handle. PreperationFailed has already been handled above.
            if (!(executionResult.Status == SandboxedProcessPipExecutionStatus.Succeeded ||
                executionResult.Status == SandboxedProcessPipExecutionStatus.ExecutionFailed ||
                executionResult.Status == SandboxedProcessPipExecutionStatus.FileAccessMonitoringFailed ||
                executionResult.Status == SandboxedProcessPipExecutionStatus.OutputWithNoFileAccessFailed ||
                executionResult.Status == SandboxedProcessPipExecutionStatus.MismatchedMessageCount))
            {
                Contract.Assert(false, "Unexpected execution result " + executionResult.Status);
            }

            bool succeeded = executionResult.Status == SandboxedProcessPipExecutionStatus.Succeeded;

            if (executionResult.Status == SandboxedProcessPipExecutionStatus.ExecutionFailed ||
                executionResult.Status == SandboxedProcessPipExecutionStatus.FileAccessMonitoringFailed ||
                executionResult.Status == SandboxedProcessPipExecutionStatus.OutputWithNoFileAccessFailed ||
                executionResult.Status == SandboxedProcessPipExecutionStatus.MismatchedMessageCount)
            {
                Contract.Assert(operationContext.LoggingContext.ErrorWasLogged, I($"Error should have been logged for '{executionResult.Status}'"));
            }

            Contract.Assert(executionResult.ObservedFileAccesses != null, "Success / ExecutionFailed provides all execution-time fields");
            Contract.Assert(executionResult.UnexpectedFileAccesses != null, "Success / ExecutionFailed provides all execution-time fields");
            Contract.Assert(executionResult.PrimaryProcessTimes != null, "Success / ExecutionFailed provides all execution-time fields");

            counters.AddToCounter(PipExecutorCounter.ExecuteProcessDuration, executionResult.PrimaryProcessTimes.TotalWallClockTime);

            using (operationContext.StartOperation(PipExecutorCounter.ProcessOutputsDuration))
            {
                ObservedInputProcessingResult observedInputValidationResult;

                using (operationContext.StartOperation(PipExecutorCounter.ProcessOutputsObservedInputValidationDuration))
                {
                    // In addition, we need to verify that additional reported inputs are actually allowed, and furthermore record them.
                    //
                    // Don't track file changes in observed input processor when process execution failed. Running observed input processor has side effects
                    // that some files get tracked by the file change tracker. Suppose that the process failed because it accesses paths that
                    // are supposed to be untracked (but the user forgot to specify it in the spec). Those paths will be tracked by 
                    // file change tracker because the observed input processor may try to probe and track those paths.
                    observedInputValidationResult =
                        await ValidateObservedFileAccesses(
                            operationContext,
                            environment,
                            state,
                            state.GetCacheableProcess(pip, environment),
                            fileAccessReportingContext,
                            executionResult.ObservedFileAccesses,
                            trackFileChanges: succeeded);
                }

                // Store the dynamically observed accesses
                processExecutionResult.DynamicallyObservedFiles = observedInputValidationResult.DynamicallyObservedFiles;
                processExecutionResult.DynamicallyObservedEnumerations = observedInputValidationResult.DynamicallyObservedEnumerations;
                processExecutionResult.AllowedUndeclaredReads = observedInputValidationResult.AllowedUndeclaredSourceReads;
                processExecutionResult.AbsentPathProbesUnderOutputDirectories = observedInputValidationResult.AbsentPathProbesUnderNonDependenceOutputDirectories;

                if (observedInputValidationResult.Status == ObservedInputProcessingStatus.Aborted)
                {
                    succeeded = false;
                    Contract.Assume(operationContext.LoggingContext.ErrorWasLogged, "No error was logged when ValidateObservedAccesses failed");
                }

                if (pip.ProcessAbsentPathProbeInUndeclaredOpaquesMode == Process.AbsentPathProbeInUndeclaredOpaquesMode.Relaxed
                    && observedInputValidationResult.AbsentPathProbesUnderNonDependenceOutputDirectories.Count > 0)
                {
                    bool isDirty = false;
                    foreach (var absentPathProbe in observedInputValidationResult.AbsentPathProbesUnderNonDependenceOutputDirectories)
                    {
                        if (!pip.DirectoryDependencies.Any(dir => absentPathProbe.IsWithin(pathTable, dir)))
                        {
                            isDirty = true;
                            break;
                        }
                    }

                    processExecutionResult.MustBeConsideredPerpetuallyDirty = isDirty;
                }

                // We have all violations now.
                UnexpectedFileAccessCounters unexpectedFilesAccesses = fileAccessReportingContext.Counters;
                processExecutionResult.ReportUnexpectedFileAccesses(unexpectedFilesAccesses);

                // Set file access violations which were not whitelisted for use by file access violation analyzer
                processExecutionResult.FileAccessViolationsNotWhitelisted = fileAccessReportingContext.FileAccessViolationsNotWhitelisted;
                processExecutionResult.WhitelistedFileAccessViolations = fileAccessReportingContext.WhitelistedFileAccessViolations;

                // We need to update this instance so used a boxed representation
                BoxRef<ProcessFingerprintComputationEventData> fingerprintComputation =
                    new ProcessFingerprintComputationEventData
                    {
                        Kind = FingerprintComputationKind.Execution,
                        PipId = pip.PipId,
                        WeakFingerprint = new WeakContentFingerprint((fingerprint ?? ContentFingerprint.Zero).Hash),

                        // This field is set later for successful strong fingerprint computation
                        StrongFingerprintComputations = CollectionUtilities.EmptyArray<ProcessStrongFingerprintComputationData>(),
                    };

                bool outputHashSuccess = false;

                if (succeeded)
                {
                    // We are now be able to store a descriptor and content for this process to cache if we wish.
                    // But if the pip completed with (warning level) file monitoring violations (suppressed or not), there's good reason
                    // to believe that there are missing inputs or outputs for the pip. This allows a nice compromise in which a build
                    // author can iterate quickly on fixing monitoring errors in a large build - mostly cached except for those parts with warnings.
                    // Of course, if the whitelist was configured to explicitly allow caching for those violations, we allow it.
                    //
                    // N.B. fileAccessReportingContext / unexpectedFilesAccesses accounts for violations from the execution itself as well as violations added by ValidateObservedAccesses
                    bool skipCaching = true;
                    ObservedInputProcessingResult? observedInputProcessingResultForCaching = null;

                    if (unexpectedFilesAccesses.HasUncacheableFileAccesses)
                    {
                        Logger.Log.ScheduleProcessNotStoredToCacheDueToFileMonitoringViolations(operationContext, processDescription);
                    }
                    else if (executionResult.NumberOfWarnings > 0 &&
                             ExtraFingerprintSalts.ArePipWarningsPromotedToErrors(configuration.Logging))
                    {
                        // Just like not caching errors, we also don't want to cache warnings that are promoted to errors
                        Logger.Log.ScheduleProcessNotStoredToWarningsUnderWarnAsError(operationContext, processDescription);
                    }
                    else if (!fingerprint.HasValue)
                    {
                        Logger.Log.ScheduleProcessNotStoredToCacheDueToInherentUncacheability(operationContext, processDescription);
                    }
                    else
                    {
                        Contract.Assume(
                            observedInputValidationResult.Status == ObservedInputProcessingStatus.Success,
                            "Should never cache a process that failed observed file input validation (cacheable-whitelisted violations leave the validation successful).");

                        // Note that we discard observed inputs if cache-ineligible (required by StoreDescriptorAndContentForProcess)
                        observedInputProcessingResultForCaching = observedInputValidationResult;
                        skipCaching = false;
                    }

                    // TODO: Maybe all counter updates should occur on distributed build master.
                    if (skipCaching)
                    {
                        counters.IncrementCounter(PipExecutorCounter.ProcessPipsExecutedButUncacheable);
                    }

                    using (operationContext.StartOperation(PipExecutorCounter.ProcessOutputsStoreContentForProcessAndCreateCacheEntryDuration))
                    {
                        outputHashSuccess = await StoreContentForProcessAndCreateCacheEntryAsync(
                            operationContext,
                            environment,
                            state,
                            pip,
                            processDescription,
                            observedInputProcessingResultForCaching,
                            executionResult.EncodedStandardOutput,
                            // Possibly null
                            executionResult.EncodedStandardError,
                            // Possibly null
                            executionResult.NumberOfWarnings,
                            processExecutionResult,
                            enableCaching: !skipCaching,
                            fingerprintComputation: fingerprintComputation,
                            executionResult.ContainerConfiguration);
                    }

                    if (outputHashSuccess)
                    {
                        processExecutionResult.SetResult(operationContext, PipResultStatus.Succeeded);
                        processExecutionResult.MustBeConsideredPerpetuallyDirty = skipCaching;
                    }
                    else
                    {
                        // The Pip itself did not fail, but we are marking it as a failure because we could not handle the post processing.
                        Contract.Assume(
                            operationContext.LoggingContext.ErrorWasLogged,
                            "Error should have been logged for StoreContentForProcessAndCreateCacheEntry() failure");
                    }
                }

                // If there were any failures, attempt to log partial information to execution log.
                // Only do this if the ObservedInputProcessor was able to process changes successfully. This will exclude
                // both the Aborted and Mismatch states, which means builds with file access violations won't get their
                // StrongFingerprint information logged. Ideally it could be logged, but there are a number of paths in
                // the ObservedInputProcessor where the final result has invalid state if the statups wasn't Success.
                if (!succeeded && observedInputValidationResult.Status == ObservedInputProcessingStatus.Success)
                {
                    var pathSet = observedInputValidationResult.GetPathSet(state.UnsafeOptions);
                    var pathSetHash = await environment.State.Cache.SerializePathSet(pathSet);

                    // This strong fingerprint is meaningless and not-cached, but compute it for the sake of
                    // execution analyzer logic that rely on having a successful strong fingerprint
                    var strongFingerprint = observedInputValidationResult.ComputeStrongFingerprint(
                        pathTable,
                        fingerprintComputation.Value.WeakFingerprint,
                        pathSetHash);

                    fingerprintComputation.Value.StrongFingerprintComputations = new[]
                    {
                        ProcessStrongFingerprintComputationData.CreateForExecution(
                            pathSetHash,
                            pathSet,
                            observedInputValidationResult.ObservedInputs,
                            strongFingerprint),
                    };
                }

                // Log the fingerprint computation
                environment.State.ExecutionLog?.ProcessFingerprintComputation(fingerprintComputation.Value);

                if (!outputHashSuccess)
                {
                    processExecutionResult.SetResult(operationContext, PipResultStatus.Failed);
                }

                return processExecutionResult;
            }
        }

        private static void ReportFileAccesses(ExecutionResult processExecutionResult, FileAccessReportingContext fileAccessReportingContext)
        {
            // We have all violations now.
            UnexpectedFileAccessCounters unexpectedFilesAccesses = fileAccessReportingContext.Counters;
            processExecutionResult.ReportUnexpectedFileAccesses(unexpectedFilesAccesses);

            // Set file access violations which were not whitelisted for use by file access violation analyzer
            processExecutionResult.FileAccessViolationsNotWhitelisted = fileAccessReportingContext.FileAccessViolationsNotWhitelisted;
            processExecutionResult.WhitelistedFileAccessViolations = fileAccessReportingContext.WhitelistedFileAccessViolations;
        }

        /// <summary>
        /// Tries to find a valid cache descriptor for the given process.
        /// - If a cache lookup proceeds successfully (whether or not it produces a usable descriptor / runnable-from-cache process),
        ///   a non-null result is returned.
        /// - If cache lookup fails (i.e., the result is inconclusive due to failed hashing, etc.), a null result is returned.
        /// </summary>
        public static async Task<RunnableFromCacheResult> TryCheckProcessRunnableFromCacheAsync(
            ProcessRunnablePip processRunnable,
            PipExecutionState.PipScopeState state,
            CacheableProcess cacheableProcess)
        {
            Contract.Requires(processRunnable != null);
            Contract.Requires(cacheableProcess != null);

            var operationContext = processRunnable.OperationContext;
            var environment = processRunnable.Environment;

            var pathTable = environment.Context.PathTable;
            Contract.Assume(pathTable != null);
            var cache = environment.State.Cache;
            Contract.Assume(cache != null);
            var content = environment.Cache.ArtifactContentCache;
            Contract.Assume(content != null);

            var process = cacheableProcess.Process;

            var processFingerprintComputationResult = new ProcessFingerprintComputationEventData
            {
                Kind = FingerprintComputationKind.CacheCheck,
                PipId = process.PipId,
                StrongFingerprintComputations =
                    CollectionUtilities.EmptyArray<ProcessStrongFingerprintComputationData>(),
            };

            BoxRef<PipCacheMissEventData> pipCacheMiss = new PipCacheMissEventData
            {
                PipId = process.PipId,
                CacheMissType = PipCacheMissType.Invalid,
            };

            int numPathSetsDownloaded = 0, numCacheEntriesVisited = 0;

            using (operationContext.StartOperation(PipExecutorCounter.CheckProcessRunnableFromCacheDuration))
            {
                // Totally usable descriptor (may additionally require content availability), or null.
                RunnableFromCacheResult.CacheHitData usableDescriptor = null;
                PublishedEntryRefLocality? refLocality;
                ObservedInputProcessingResult? maybeUsableProcessingResult = null;

                string description = processRunnable.Description;

                WeakContentFingerprint weakFingerprint;
                using (operationContext.StartOperation(PipExecutorCounter.ComputeWeakFingerprintDuration))
                {
                    weakFingerprint = new WeakContentFingerprint(cacheableProcess.ComputeWeakFingerprint().Hash);
                    processFingerprintComputationResult.WeakFingerprint = weakFingerprint;
                }

                if (cacheableProcess.ShouldHaveArtificialMiss())
                {
                    pipCacheMiss.Value.CacheMissType = PipCacheMissType.MissForDescriptorsDueToArtificialMissOptions;
                    Logger.Log.ScheduleArtificialCacheMiss(operationContext, description);
                    refLocality = null;
                }
                else if (cacheableProcess.DisableCacheLookup())
                {
                    // No sense in going into the strong fingerprint lookup if cache lookup is disabled.
                    pipCacheMiss.Value.CacheMissType = PipCacheMissType.MissForProcessConfiguredUncacheable;
                    Logger.Log.ScheduleProcessConfiguredUncacheable(operationContext, description);
                    refLocality = null;
                }
                else
                {
                    // Chapter 1: Determine Strong Fingerprint
                    // First, we will evaluate a sequence of (path set, strong fingerprint) pairs.
                    // Each path set generates a particular strong fingerprint based on local build state (input hashes);
                    // if we find a pair such that the generated strong fingerprint matches, then we should be able to find
                    // a usable entry (describing the output hashes, etc.) to replay.

                    // We will set this to the first usable-looking entry we find, if any.
                    // Note that we do not bother investigating further pairs if we find an entry-ref that can't be fetched,
                    // or if the fetched entry refers to content that cannot be found. Both are fairly unusual failures for well-behaved caches.
                    // So, this is assigned at most once for entry into Chapter 2.
                    PublishedEntryRef? maybeUsableEntryRef = null;
                    ObservedPathSet? maybePathSet = null;

                    // Set if we find a usable entry.
                    refLocality = null;

                    using (operationContext.StartOperation(PipExecutorCounter.CheckProcessRunnableFromCacheChapter1DetermineStrongFingerprintDuration))
                    using (var strongFingerprintCacheWrapper = SchedulerPools.HashFingerprintDataMapPool.GetInstance())
                    using (var strongFingerprintComputationListWrapper = SchedulerPools.StrongFingerprintDataListPool.GetInstance())
                    {
                        // It is common to have many entry refs for the same PathSet, since often path content changes more often than the set of paths
                        // (i.e., the refs differ by strong fingerprint). We cache the strong fingerprint computation per PathSet; this saves the repeated
                        // cost of fetching and deserializing the path set, validating access to the paths and finding their content, and computing the overall strong fingerprint.
                        // For those path sets that are ill-defined for the pip (e.g. inaccessible paths), we use a null marker.
                        Dictionary<ContentHash, Tuple<BoxRef<ProcessStrongFingerprintComputationData>, ObservedInputProcessingResult, ObservedPathSet>> strongFingerprintCache =
                            strongFingerprintCacheWrapper.Instance;
                        List<BoxRef<ProcessStrongFingerprintComputationData>> strongFingerprintComputationList =
                            strongFingerprintComputationListWrapper.Instance;

                        foreach (
                            Task<Possible<PublishedEntryRef, Failure>> batchPromise in
                                cache.ListPublishedEntriesByWeakFingerprint(operationContext, weakFingerprint))
                        {
                            if (environment.Context.CancellationToken.IsCancellationRequested)
                            {
                                break;
                            }

                            Possible<PublishedEntryRef> maybeBatch;
                            using (operationContext.StartOperation(PipExecutorCounter.CacheQueryingWeakFingerprintDuration))
                            {
                                maybeBatch = await batchPromise;
                            }

                            if (!maybeBatch.Succeeded)
                            {
                                Logger.Log.TwoPhaseFailureQueryingWeakFingerprint(
                                    operationContext,
                                    description,
                                    weakFingerprint.ToString(),
                                    maybeBatch.Failure.DescribeIncludingInnerFailures());
                                continue;
                            }

                            PublishedEntryRef entryRef = maybeBatch.Result;

                            if (entryRef.IgnoreEntry)
                            {
                                continue;
                            }

                            // Only increment for valid entries
                            ++numCacheEntriesVisited;

                            // First, we use the path-set component of the entry to compute the strong fingerprint we would accept.
                            // Note that we often can re-use an already computed strong fingerprint (this wouldn't be needed if instead
                            // the cache returned (path set, [strong fingerprint 1, strong fingerprint 2, ...])
                            Tuple<BoxRef<ProcessStrongFingerprintComputationData>, ObservedInputProcessingResult, ObservedPathSet> strongFingerprintComputation;
                            StrongContentFingerprint? strongFingerprint = null;
                            if (!strongFingerprintCache.TryGetValue(entryRef.PathSetHash, out strongFingerprintComputation))
                            {
                                using (operationContext.StartOperation(PipExecutorCounter.TryLoadPathSetFromContentCacheDuration))
                                {
                                    maybePathSet = await TryLoadPathSetFromContentCache(
                                        operationContext,
                                        environment,
                                        description,
                                        weakFingerprint,
                                        entryRef.PathSetHash);
                                }

                                ++numPathSetsDownloaded;

                                if (!maybePathSet.HasValue)
                                {
                                    // Failure reason already logged.
                                    // Poison this path set hash so we don't repeatedly try to retrieve and parse it.
                                    strongFingerprintCache[entryRef.PathSetHash] = null;
                                    continue;
                                }

                                var pathSet = maybePathSet.Value;
                                (bool succeeded, ObservedInputProcessingResult observedInputProcessingResult, StrongContentFingerprint? strongContentFingerprint, ObservedPathSet pathSetUsed, ContentHash pathSetHashUsed)
                                    strongFingerprintComputationResult =
                                        await TryComputeStrongFingerprintBasedOnPriorObservedPathSetAsync(
                                            operationContext,
                                            environment,
                                            state,
                                            cacheableProcess,
                                            weakFingerprint,
                                            pathSet,
                                            entryRef.PathSetHash);

                                // Record the most relevant strong fingerprint information, defaulting to information retrieved from cache
                                BoxRef<ProcessStrongFingerprintComputationData> strongFingerprintComputationData = strongFingerprintComputationResult.succeeded
                                    ? new ProcessStrongFingerprintComputationData(
                                        pathSet: strongFingerprintComputationResult.pathSetUsed,
                                        pathSetHash: strongFingerprintComputationResult.pathSetHashUsed,
                                        priorStrongFingerprints: new List<StrongContentFingerprint>(1) { strongFingerprintComputationResult.strongContentFingerprint.Value })
                                    : new ProcessStrongFingerprintComputationData(
                                        pathSet: pathSet,
                                        pathSetHash: entryRef.PathSetHash,
                                        priorStrongFingerprints: new List<StrongContentFingerprint>(1) { entryRef.StrongFingerprint });

                                strongFingerprintComputationList.Add(strongFingerprintComputationData);

                                ObservedInputProcessingResult observedInputProcessingResult = strongFingerprintComputationResult.observedInputProcessingResult;
                                ObservedInputProcessingStatus processingStatus = observedInputProcessingResult.Status;

                                switch (processingStatus)
                                {
                                    case ObservedInputProcessingStatus.Success:
                                        strongFingerprint = strongFingerprintComputationResult.strongContentFingerprint;
                                        Contract.Assume(strongFingerprint.HasValue);

                                        strongFingerprintComputationData.Value = strongFingerprintComputationData.Value.ToSuccessfulResult(
                                            computedStrongFingerprint: strongFingerprint.Value,
                                            observedInputs: observedInputProcessingResult.ObservedInputs.BaseArray);

                                        if (ETWLogger.Log.IsEnabled(EventLevel.Verbose, Events.Keywords.Diagnostics))
                                        {
                                            Logger.Log.TwoPhaseStrongFingerprintComputedForPathSet(
                                                operationContext,
                                                description,
                                                weakFingerprint.ToString(),
                                                entryRef.PathSetHash.ToHex(),
                                                strongFingerprint.Value.ToString());
                                        }

                                        break;
                                    case ObservedInputProcessingStatus.Mismatched:
                                        // This pip can't access some of the paths. We should remember that (the path set may be repeated many times).
                                        strongFingerprint = null;
                                        if (ETWLogger.Log.IsEnabled(EventLevel.Verbose, Events.Keywords.Diagnostics))
                                        {
                                            Logger.Log.TwoPhaseStrongFingerprintUnavailableForPathSet(
                                                operationContext,
                                                description,
                                                weakFingerprint.ToString(),
                                                entryRef.PathSetHash.ToHex());
                                        }

                                        break;
                                    default:
                                        Contract.Assume(operationContext.LoggingContext.ErrorWasLogged);
                                        Contract.Assert(processingStatus == ObservedInputProcessingStatus.Aborted);

                                        // An error has already been logged. We have to bail out and fail the pip.
                                        return null;
                                }

                                strongFingerprintCache[entryRef.PathSetHash] = strongFingerprintComputation = Tuple.Create(strongFingerprintComputationData, observedInputProcessingResult, pathSet);
                            }
                            else if (strongFingerprintComputation != null)
                            {
                                // Add the strong fingerprint to the list of strong fingerprints to be reported
                                strongFingerprintComputation.Item1.Value.AddPriorStrongFingerprint(entryRef.StrongFingerprint);

                                // Set the strong fingerprint computed for this path set so it can be compared to the
                                // prior strong fingerprint for a cache hit/miss
                                if (strongFingerprintComputation.Item1.Value.Succeeded)
                                {
                                    strongFingerprint = strongFingerprintComputation.Item1.Value.ComputedStrongFingerprint;
                                }
                            }

                            // Now we might have a strong fingerprint.
                            if (!strongFingerprint.HasValue)
                            {
                                // Recall that 'null' is a special value meaning 'this path set will never work'
                                continue;
                            }

                            if (strongFingerprint.Value == entryRef.StrongFingerprint)
                            {
                                // Hit! We will immediately commit to this entry-ref. We will have a cache-hit iff
                                // the entry can be fetched and (if requested) the referenced content can be loaded.
                                strongFingerprintComputation.Item1.Value.IsStrongFingerprintHit = true;
                                maybeUsableEntryRef = entryRef;

                                // We remember locality (local or remote) for attribution later (e.g. we count remote hits separately from local hits).
                                refLocality = entryRef.Locality;

                                // We also remember the processingResult
                                maybeUsableProcessingResult = strongFingerprintComputation.Item2;
                                maybePathSet = strongFingerprintComputation.Item3;

                                Logger.Log.TwoPhaseStrongFingerprintMatched(
                                    operationContext,
                                    description,
                                    strongFingerprint: entryRef.StrongFingerprint.ToString(),
                                    strongFingerprintCacheId: entryRef.OriginatingCache);
                                environment.ReportCacheDescriptorHit(entryRef.OriginatingCache);
                                break;
                            }

                            if (ETWLogger.Log.IsEnabled(EventLevel.Verbose, BuildXL.Utilities.Tracing.Events.Keywords.Diagnostics))
                            {
                                Logger.Log.TwoPhaseStrongFingerprintRejected(
                                    operationContext,
                                    description,
                                    pathSetHash: entryRef.PathSetHash.ToHex(),
                                    rejectedStrongFingerprint: entryRef.StrongFingerprint.ToString(),
                                    availableStrongFingerprint: strongFingerprint.Value.ToString());
                            }
                        }

                        // Update the strong fingerprint computations list
                        processFingerprintComputationResult.StrongFingerprintComputations = strongFingerprintComputationList.SelectArray(s => s.Value);
                    }

                    CacheEntry? maybeUsableCacheEntry = null;
                    using (operationContext.StartOperation(PipExecutorCounter.CheckProcessRunnableFromCacheChapter2RetrieveCacheEntryDuration))
                    {
                        // Chapter 2: Retrieve Cache Entry
                        // If we found a usable-looking entry-ref, then we should be able to fetch the actual entry (containing metadata, and output hashes).
                        if (maybeUsableEntryRef.HasValue)
                        {
                            PublishedEntryRef usableEntryRef = maybeUsableEntryRef.Value;

                            // The speed of Chapter2 is basically all just this call to GetContentHashList
                            Possible<CacheEntry?> entryFetchResult =
                                await cache.TryGetCacheEntryAsync(
                                    cacheableProcess.Process,
                                    weakFingerprint,
                                    usableEntryRef.PathSetHash,
                                    usableEntryRef.StrongFingerprint);

                            if (entryFetchResult.Succeeded)
                            {
                                if (entryFetchResult.Result != null)
                                {
                                    maybeUsableCacheEntry = entryFetchResult.Result;
                                }
                                else
                                {
                                    // TryGetCacheEntryAsync indicates a graceful miss by returning a null entry. In general, this is reasonable.
                                    // However, since we tried to fetch an entry just recently mentioned by ListPublishedEntriesByWeakFingerprint,
                                    // this is unusual (unusual enough that we don't bother looking for other (path set, strong fingerprint) pairs.
                                    Logger.Log.TwoPhaseCacheEntryMissing(
                                        operationContext,
                                        description,
                                        weakFingerprint: weakFingerprint.ToString(),
                                        strongFingerprint: maybeUsableEntryRef.Value.StrongFingerprint.ToString());
                                    pipCacheMiss.Value.CacheMissType = PipCacheMissType.MissForCacheEntry;
                                }
                            }
                            else
                            {
                                Logger.Log.TwoPhaseFetchingCacheEntryFailed(
                                    operationContext,
                                    description,
                                    maybeUsableEntryRef.Value.StrongFingerprint.ToString(),
                                    entryFetchResult.Failure.DescribeIncludingInnerFailures());
                                pipCacheMiss.Value.CacheMissType = PipCacheMissType.MissForCacheEntry;
                            }
                        }
                        else
                        {
                            // We didn't find a usable ref. We can attribute this as a new fingerprint (no refs checked at all)
                            // or a mismatch of strong fingerprints (at least one ref checked).
                            if (numCacheEntriesVisited == 0)
                            {
                                pipCacheMiss.Value.CacheMissType = PipCacheMissType.MissForDescriptorsDueToWeakFingerprints;
                                Logger.Log.TwoPhaseCacheDescriptorMissDueToWeakFingerprint(
                                    operationContext,
                                    description,
                                    weakFingerprint.ToString());
                            }
                            else
                            {
                                pipCacheMiss.Value.CacheMissType = PipCacheMissType.MissForDescriptorsDueToStrongFingerprints;
                                Logger.Log.TwoPhaseCacheDescriptorMissDueToStrongFingerprints(
                                    operationContext,
                                    description,
                                    weakFingerprint.ToString());
                            }
                        }

                    }

                    if (maybeUsableCacheEntry.HasValue)
                    {
                        usableDescriptor = await TryConvertToRunnableFromCacheResult(
                         processRunnable,
                         operationContext,
                         environment,
                         state,
                         cacheableProcess,
                         refLocality.Value,
                         description,
                         weakFingerprint,
                         maybeUsableEntryRef.Value.PathSetHash,
                         maybeUsableEntryRef.Value.StrongFingerprint,
                         maybeUsableCacheEntry,
                         maybePathSet,
                         pipCacheMiss);
                    }
                }

                RunnableFromCacheResult runnableFromCacheResult;

                runnableFromCacheResult = CreateRunnableFromCacheResult(
                    usableDescriptor,
                    environment,
                    refLocality,
                    maybeUsableProcessingResult,
                    weakFingerprint);

                if (!runnableFromCacheResult.CanRunFromCache)
                {
                    Contract.Assert(pipCacheMiss.Value.CacheMissType != PipCacheMissType.Invalid, "Must have valid cache miss reason");
                    environment.Counters.IncrementCounter((PipExecutorCounter)pipCacheMiss.Value.CacheMissType);

                    Logger.Log.ScheduleProcessPipCacheMiss(
                        operationContext,
                        cacheableProcess.Description,
                        runnableFromCacheResult.Fingerprint.ToString());
                    environment.State.ExecutionLog?.PipCacheMiss(pipCacheMiss.Value);
                }

                using (operationContext.StartOperation(PipExecutorCounter.CheckProcessRunnableFromCacheExecutionLogDuration))
                {
                    environment.State.ExecutionLog?.ProcessFingerprintComputation(processFingerprintComputationResult);
                }

                processRunnable.CacheLookupPerfInfo.LogCounters(pipCacheMiss.Value.CacheMissType, numPathSetsDownloaded, numCacheEntriesVisited);
                return runnableFromCacheResult;
            }
        }

        private static RunnableFromCacheResult CreateRunnableFromCacheResult(
            RunnableFromCacheResult.CacheHitData cacheHitData,
            IPipExecutionEnvironment environment,
            PublishedEntryRefLocality? refLocality,
            ObservedInputProcessingResult? observedInputProcessingResult,
            WeakContentFingerprint weakFingerprint)
        {
            if (cacheHitData != null)
            {
                // We remembered the locality of the descriptor's ref earlier, since we want to count
                // 'remote' hits separately (i.e., how much does a remote cache help?)
                Contract.Assume(refLocality.HasValue);
                if (refLocality.Value == PublishedEntryRefLocality.Remote)
                {
                    environment.Counters.IncrementCounter(PipExecutorCounter.RemoteCacheHitsForProcessPipDescriptorAndContent);

                    // TODO: For now we estimate the size of remotely downloaded content as the sum of output sizes
                    //       for remote descriptors. However, this is an over-estimate of what was *actually* downloaded,
                    //       since some or all of that content may be already local (or maybe several outputs have the same content).
                    environment.Counters.AddToCounter(
                        PipExecutorCounter.RemoteContentDownloadedBytes,
                        cacheHitData.Metadata.TotalOutputSize);
                }

                return RunnableFromCacheResult.CreateForHit(
                    weakFingerprint,
                    // We use the weak fingerprint so that misses and hits are consistent (no strong fingerprint available on some misses).
                    dynamicallyObservedFiles: observedInputProcessingResult.HasValue
                        ? observedInputProcessingResult.Value.DynamicallyObservedFiles
                        : ReadOnlyArray<AbsolutePath>.Empty,
                    dynamicallyObservedEnumerations: observedInputProcessingResult.HasValue
                        ? observedInputProcessingResult.Value.DynamicallyObservedEnumerations
                        : ReadOnlyArray<AbsolutePath>.Empty,
                    allowedUndeclaredSourceReads: observedInputProcessingResult.HasValue
                        ? observedInputProcessingResult.Value.AllowedUndeclaredSourceReads
                        : CollectionUtilities.EmptySet<AbsolutePath>(),
                    absentPathProbesUnderNonDependenceOutputDirectories: observedInputProcessingResult.HasValue
                        ? observedInputProcessingResult.Value.AbsentPathProbesUnderNonDependenceOutputDirectories
                        : CollectionUtilities.EmptySet<AbsolutePath>(),
                    cacheHitData: cacheHitData);
            }

            return RunnableFromCacheResult.CreateForMiss(weakFingerprint);
        }

        /// <summary>
        /// Tries convert <see cref="ExecutionResult"/> to <see cref="RunnableFromCacheResult"/>.
        /// </summary>
        /// <remarks>
        /// This method is used for distributed cache look-up. The result of cache look-up done on the worker is transferred back
        /// to the master as <see cref="ExecutionResult"/> (for the sake of reusing existing transport structure). This method
        /// then converts it to <see cref="RunnableFromCacheResult"/> that can be consumed by the scheduler's cache look-up step.
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters")]
        public static RunnableFromCacheResult TryConvertToRunnableFromCacheResult(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            CacheablePip pip,
            ExecutionResult executionResult)
        {
            Contract.Assert(!executionResult.Result.IndicatesFailure());
            Contract.Assert(executionResult.WeakFingerprint.HasValue);

            if (executionResult.PipCacheDescriptorV2Metadata == null || executionResult.TwoPhaseCachingInfo == null)
            {
                return RunnableFromCacheResult.CreateForMiss(executionResult.WeakFingerprint.Value);
            }

            var cacheHitData = TryCreatePipCacheDescriptorFromMetadata(
                environment,
                state,
                pip,
                metadata: executionResult.PipCacheDescriptorV2Metadata,
                refLocality: PublishedEntryRefLocality.Remote,
                pathSetHash: executionResult.TwoPhaseCachingInfo.PathSetHash,
                strongFingerprint: executionResult.TwoPhaseCachingInfo.StrongFingerprint,
                metadataHash: executionResult.TwoPhaseCachingInfo.CacheEntry.MetadataHash,
                pathSet: executionResult.PathSet);

            return cacheHitData != null
                ? RunnableFromCacheResult.CreateForHit(
                    weakFingerprint: executionResult.TwoPhaseCachingInfo.WeakFingerprint,
                    dynamicallyObservedFiles: executionResult.DynamicallyObservedFiles,
                    dynamicallyObservedEnumerations: executionResult.DynamicallyObservedEnumerations,
                    allowedUndeclaredSourceReads: executionResult.AllowedUndeclaredReads,
                    absentPathProbesUnderNonDependenceOutputDirectories: executionResult.AbsentPathProbesUnderOutputDirectories,
                    cacheHitData: cacheHitData)
                : RunnableFromCacheResult.CreateForMiss(executionResult.TwoPhaseCachingInfo.WeakFingerprint);
        }

        private static async Task<RunnableFromCacheResult.CacheHitData> TryConvertToRunnableFromCacheResult(
            ProcessRunnablePip processRunnable,
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            CacheablePip pip,
            PublishedEntryRefLocality refLocality,
            string processDescription,
            WeakContentFingerprint weakFingerprint,
            ContentHash pathSetHash,
            StrongContentFingerprint strongFingerprint,
            CacheEntry? maybeUsableCacheEntry,
            ObservedPathSet? pathSet,
            BoxRef<PipCacheMissEventData> pipCacheMiss)
        {
            RunnableFromCacheResult.CacheHitData maybeParsedDescriptor = null;
            using (operationContext.StartOperation(PipExecutorCounter.CheckProcessRunnableFromCacheChapter3RetrieveAndParseMetadataDuration))
            {
                // Chapter 3: Interpret Cache Entry
                // Finally, we will try to turn a usable cache entry into a complete RunnableFromCacheResult.
                // Given a naked entry just retrieved from the cache, we need to interpret that entry according
                // to the process pip in question:
                // - The cache entry must have the special reserved slots for execution metadata and stdout/stderr.
                // - We *always* fetch metadata content (used as part of the RunnableFromCacheResult), even
                //   if fetching *output* content was not requested.
                if (maybeUsableCacheEntry != null)
                {
                    // Almost all of the cost of chapter3 is in the TryLoadAndDeserializeContent call
                    CacheEntry usableCacheEntry = maybeUsableCacheEntry.Value;
                    bool isFromHistoricMetadataCache = usableCacheEntry.OriginatingCache == HistoricMetadataCache.OriginatingCacheId;
                    Possible<PipCacheDescriptorV2Metadata> maybeMetadata =
                        await environment.State.Cache.TryRetrieveMetadataAsync(
                            pip.UnderlyingPip,
                            weakFingerprint,
                            strongFingerprint,
                            usableCacheEntry.MetadataHash,
                            pathSetHash);

                    if (maybeMetadata.Succeeded && maybeMetadata.Result != null)
                    {
                        maybeParsedDescriptor = TryCreatePipCacheDescriptorFromMetadata(
                            environment,
                            state,
                            pip,
                            maybeMetadata.Result,
                            refLocality,
                            pathSetHash,
                            strongFingerprint,
                            metadataHash: usableCacheEntry.MetadataHash,
                            pathSet: pathSet);

                        // Parsing can fail if the descriptor is malformed, despite being valid from the cache's perspective
                        // (e.g. missing required content)
                        if (maybeParsedDescriptor == null)
                        {
                            Logger.Log.ScheduleInvalidCacheDescriptorForContentFingerprint(
                                        operationContext,
                                        processDescription,
                                        weakFingerprint.ToString(),
                                        GetCacheLevelForLocality(refLocality),
                                        string.Empty);
                            pipCacheMiss.Value.CacheMissType = PipCacheMissType.MissDueToInvalidDescriptors;
                        }
                    }
                    else if (!maybeMetadata.Succeeded)
                    {
                        environment.State.Cache.Counters.IncrementCounter(PipCachingCounter.MetadataRetrievalFails);
                        if (maybeMetadata.Failure is Failure<PipFingerprintEntry>)
                        {
                            Logger.Log.ScheduleInvalidCacheDescriptorForContentFingerprint(
                                operationContext,
                                processDescription,
                                weakFingerprint.ToString(),
                                GetCacheLevelForLocality(refLocality),
                                maybeMetadata.Failure.DescribeIncludingInnerFailures());
                            pipCacheMiss.Value.CacheMissType = PipCacheMissType.MissDueToInvalidDescriptors;
                        }
                        else
                        {
                            Logger.Log.TwoPhaseFetchingMetadataForCacheEntryFailed(
                                operationContext,
                                processDescription,
                                strongFingerprint.ToString(),
                                usableCacheEntry.MetadataHash.ToHex(),
                                maybeMetadata.Failure.DescribeIncludingInnerFailures());
                            pipCacheMiss.Value.CacheMissType = isFromHistoricMetadataCache
                                ? PipCacheMissType.MissForProcessMetadataFromHistoricMetadata
                                : PipCacheMissType.MissForProcessMetadata;
                        }
                    }
                    else
                    {
                        Contract.Assert(maybeMetadata.Result == null);

                        // This is a content-miss for the metadata blob. We expected it present since it was referenced
                        // by the cache entry, and for well-behaved caches that should imply a hit.
                        Logger.Log.TwoPhaseMissingMetadataForCacheEntry(
                            operationContext,
                            processDescription,
                            strongFingerprint: strongFingerprint.ToString(),
                            metadataHash: usableCacheEntry.MetadataHash.ToHex());
                        pipCacheMiss.Value.CacheMissType = isFromHistoricMetadataCache
                            ? PipCacheMissType.MissForProcessMetadataFromHistoricMetadata
                            : PipCacheMissType.MissForProcessMetadata;
                    }
                }

                if (maybeParsedDescriptor != null)
                {
                    // Descriptor hit. We may increment the 'miss due to unavailable content' counter below however.
                    Logger.Log.ScheduleCacheDescriptorHitForContentFingerprint(
                        operationContext,
                        processDescription,
                        weakFingerprint.ToString(),
                        maybeParsedDescriptor.Metadata.Id,
                        GetCacheLevelForLocality(refLocality));
                    environment.Counters.IncrementCounter(PipExecutorCounter.CacheHitsForProcessPipDescriptors);
                }
            }

            using (operationContext.StartOperation(PipExecutorCounter.CheckProcessRunnableFromCacheChapter4CheckContentAvailabilityDuration))
            {
                // Chapter 4: Check Content Availability
                // We additionally require output content availability.
                // This is the last check; we set `usableDescriptor` here.
                RunnableFromCacheResult.CacheHitData usableDescriptor;
                if (maybeParsedDescriptor != null)
                {
                    bool isContentAvailable =
                        await
                            TryLoadAvailableOutputContentAsync(
                                operationContext,
                                environment,
                                pip,
                                maybeParsedDescriptor.CachedArtifactContentHashes,
                                strongFingerprint: strongFingerprint,
                                metadataHash: maybeParsedDescriptor.MetadataHash,
                                standardOutput: maybeParsedDescriptor.StandardOutput,
                                standardError: maybeParsedDescriptor.StandardError);

                    if (!isContentAvailable)
                    {
                        usableDescriptor = null;

                        Logger.Log.ScheduleContentMissAfterContentFingerprintCacheDescriptorHit(
                            operationContext,
                            processDescription,
                            weakFingerprint.ToString(),
                            maybeParsedDescriptor.Metadata.Id);
                        pipCacheMiss.Value.CacheMissType = PipCacheMissType.MissForProcessOutputContent;
                    }
                    else
                    {
                        usableDescriptor = maybeParsedDescriptor;
                    }
                }
                else
                {
                    // Non-usable descriptor; no content to fetch, and we've failed already.
                    usableDescriptor = null;
                }

                return usableDescriptor;
            }
        }

        private static int GetCacheLevelForLocality(PublishedEntryRefLocality locality)
        {
            return locality == PublishedEntryRefLocality.Local ? 1 : 2;
        }

        private static async Task<ObservedPathSet?> TryLoadPathSetFromContentCache(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            string processDescription,
            WeakContentFingerprint weakFingerprint,
            ContentHash pathSetHash)
        {
            var maybePathSet = await environment.State.Cache.TryRetrievePathSetAsync(operationContext, weakFingerprint, pathSetHash);

            if (!maybePathSet.Succeeded)
            {
                if (maybePathSet.Failure is ObservedPathSet.DeserializeFailure)
                {
                    Logger.Log.TwoPhasePathSetInvalid(
                        operationContext,
                        processDescription,
                        weakFingerprint: weakFingerprint.ToString(),
                        pathSetHash: pathSetHash.ToHex(),
                        failure: maybePathSet.Failure.Describe());
                }
                else
                {
                    Logger.Log.TwoPhaseLoadingPathSetFailed(
                        operationContext,
                        processDescription,
                        weakFingerprint: weakFingerprint.ToString(),
                        pathSetHash: pathSetHash.ToHex(),
                        failure: maybePathSet.Failure.DescribeIncludingInnerFailures());
                }

                return null;
            }

            return maybePathSet.Result;
        }

        private static void CheckCachedMetadataIntegrity(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            Process process,
            RunnableFromCacheResult runnableFromCacheCheckResult)
        {
            Contract.Requires(environment != null);
            Contract.Requires(process != null);
            Contract.Requires(runnableFromCacheCheckResult != null);
            Contract.Requires(runnableFromCacheCheckResult.CanRunFromCache);

            var pathTable = environment.Context.PathTable;
            var stringTable = environment.Context.StringTable;
            var cacheHitData = runnableFromCacheCheckResult.GetCacheHitData();
            var metadata = cacheHitData.Metadata;
            var currentProcessWeakFingerprintText = runnableFromCacheCheckResult.WeakFingerprint.ToString();
            var currentProcessStrongFingerprintText = cacheHitData.StrongFingerprint.ToString();

            if ((!string.IsNullOrEmpty(metadata.WeakFingerprint) &&
                 !string.Equals(currentProcessWeakFingerprintText, metadata.WeakFingerprint, StringComparison.OrdinalIgnoreCase))
                ||
                (!string.IsNullOrEmpty(metadata.StrongFingerprint) &&
                 !string.Equals(currentProcessStrongFingerprintText, metadata.StrongFingerprint, StringComparison.OrdinalIgnoreCase)))
            {
                string message =
                    I($"Metadata retrieved for Pip{process.SemiStableHash:X16} (Weak fingerprint: {currentProcessWeakFingerprintText}, Strong fingerprint: {currentProcessStrongFingerprintText}) belongs to Pip{metadata.SemiStableHash:X16} (Weak fingerprint:{metadata.WeakFingerprint}, Strong fingerprint:{metadata.StrongFingerprint})");
                var stringBuilder = new StringBuilder();
                stringBuilder.AppendLine(message);

                if (process.FileOutputs.Count(f => f.CanBeReferencedOrCached()) != cacheHitData.CachedArtifactContentHashes.Length)
                {
                    stringBuilder.AppendLine(I($"Output files of Pip{process.SemiStableHash:X16}:"));
                    stringBuilder.AppendLine(
                        string.Join(
                            Environment.NewLine,
                            process.FileOutputs.Where(f => f.CanBeReferencedOrCached())
                                .Select(f => "\t" + f.Path.ToString(pathTable))));
                }

                stringBuilder.AppendLine(
                    I($"Output files of Pip{process.SemiStableHash:X16} and their corresponding file names in metadata of Pip{metadata.SemiStableHash:X16}:"));
                stringBuilder.AppendLine(
                   string.Join(
                       Environment.NewLine,
                       cacheHitData.CachedArtifactContentHashes.Select(f => I($"\t{f.fileArtifact.Path.ToString(pathTable)} : ({f.fileMaterializationInfo.FileName.ToString(stringTable)})"))));

                if (process.DirectoryOutputs.Length != metadata.DynamicOutputs.Count)
                {
                    stringBuilder.AppendLine(I($"{Pip.SemiStableHashPrefix}{process.SemiStableHash:X16} and {Pip.SemiStableHashPrefix}{metadata.SemiStableHash:X16} have different numbers of output directories"));
                }

                Logger.Log.PipCacheMetadataBelongToAnotherPip(
                    operationContext.LoggingContext,
                    process.SemiStableHash,
                    process.GetDescription(environment.Context),
                    stringBuilder.ToString());

                throw new BuildXLException(message, ExceptionRootCause.CorruptedCache);
            }
        }

        private static void AssertNoFileNamesMismatch(
            IPipExecutionEnvironment environment,
            Process process,
            RunnableFromCacheResult runnableFromCacheCheckResult,
            FileArtifact file,
            in FileMaterializationInfo info)
        {
            Contract.Requires(environment != null);
            Contract.Requires(process != null);
            Contract.Requires(file.IsValid);

            if (!info.FileName.IsValid)
            {
                return;
            }

            PathAtom fileArtifactFileName = file.Path.GetName(environment.Context.PathTable);
            if (!info.FileName.CaseInsensitiveEquals(environment.Context.StringTable, fileArtifactFileName))
            {
                var pathTable = environment.Context.PathTable;
                var stringTable = environment.Context.StringTable;
                var cacheHitData = runnableFromCacheCheckResult.GetCacheHitData();

                string fileArtifactPathString = file.Path.ToString(pathTable);
                string fileMaterializationFileNameString = info.FileName.ToString(stringTable);
                var stringBuilder = new StringBuilder();
                stringBuilder.AppendLine(
                    I($"File name should only differ by casing. File artifact's full path: '{fileArtifactPathString}'; file artifact's file name: '{fileArtifactFileName.ToString(stringTable)}'; materialization info file name: '{fileMaterializationFileNameString}'."));
                stringBuilder.AppendLine(I($"[{process.FormattedSemiStableHash}] Weak FP: '{runnableFromCacheCheckResult.WeakFingerprint.ToString()}', Strong FP: '{cacheHitData.StrongFingerprint.ToString()}', Metadata Hash: '{cacheHitData.MetadataHash.ToString()}'"));

                Contract.Assert(false, stringBuilder.ToString());
            }
        }

        private static ExecutionResult GetCacheHitExecutionResult(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            Process pip,
            RunnableFromCacheResult runnableFromCacheCheckResult)
        {
            Contract.Requires(environment != null);
            Contract.Requires(pip != null);
            Contract.Requires(runnableFromCacheCheckResult != null);
            Contract.Requires(runnableFromCacheCheckResult.CanRunFromCache);

            var cacheHitData = runnableFromCacheCheckResult.GetCacheHitData();

            var executionResult = new ExecutionResult
            {
                WeakFingerprint = runnableFromCacheCheckResult.WeakFingerprint,

                // This is the cache-hit path, so there were no uncacheable file accesses.
                MustBeConsideredPerpetuallyDirty = false,
                DynamicallyObservedFiles = runnableFromCacheCheckResult.DynamicallyObservedFiles,
                DynamicallyObservedEnumerations = runnableFromCacheCheckResult.DynamicallyObservedEnumerations,
                AllowedUndeclaredReads = runnableFromCacheCheckResult.AllowedUndeclaredReads,
                AbsentPathProbesUnderOutputDirectories = runnableFromCacheCheckResult.AbsentPathProbesUnderNonDependenceOutputDirectories,
            };

            CheckCachedMetadataIntegrity(operationContext, environment, pip, runnableFromCacheCheckResult);

            for (int i = 0; i < cacheHitData.CachedArtifactContentHashes.Length; i++)
            {
                var info = cacheHitData.CachedArtifactContentHashes[i].fileMaterializationInfo;
                var file = cacheHitData.CachedArtifactContentHashes[i].fileArtifact;

                AssertNoFileNamesMismatch(environment, pip, runnableFromCacheCheckResult, file, info);
                executionResult.ReportOutputContent(file, info, PipOutputOrigin.NotMaterialized);
            }

            // For each opaque directory, iterate its dynamic outputs which are stored in cache descriptor metadata.
            // The ordering of pip.DirectoryOutputs and metadata.DynamicOutputs is consistent.

            // The index of the first artifact corresponding to an opaque directory input
            using (var poolFileList = Pools.GetFileArtifactList())
            {
                var fileList = poolFileList.Instance;
                for (int i = 0; i < pip.DirectoryOutputs.Length; i++)
                {
                    fileList.Clear();

                    foreach (var dynamicOutputFileAndInfo in cacheHitData.DynamicDirectoryContents[i])
                    {
                        fileList.Add(dynamicOutputFileAndInfo.fileArtifact);
                    }

                    executionResult.ReportDirectoryOutput(pip.DirectoryOutputs[i], fileList);
                }
            }

            // Report absent files
            var absentFileInfo = FileMaterializationInfo.CreateWithUnknownLength(WellKnownContentHashes.AbsentFile);
            if (cacheHitData.AbsentArtifacts != null)
            {
                foreach (var absentFile in cacheHitData.AbsentArtifacts)
                {
                    executionResult.ReportOutputContent(absentFile, absentFileInfo, PipOutputOrigin.NotMaterialized);
                }
            }

            // Report the standard error/output files
            // These may or may not also be declared pip outputs, it is safe to report the content twice
            if (cacheHitData.StandardError != null)
            {
                var fileArtifact = GetCachedSandboxedProcessOutputArtifact(cacheHitData, pip, SandboxedProcessFile.StandardError);
                var fileMaterializationInfo = FileMaterializationInfo.CreateWithUnknownLength(cacheHitData.StandardError.Item2);

                executionResult.ReportOutputContent(fileArtifact, fileMaterializationInfo, PipOutputOrigin.NotMaterialized);
            }

            if (cacheHitData.StandardOutput != null)
            {
                var fileArtifact = GetCachedSandboxedProcessOutputArtifact(cacheHitData, pip, SandboxedProcessFile.StandardOutput);
                var fileMaterializationInfo = FileMaterializationInfo.CreateWithUnknownLength(cacheHitData.StandardOutput.Item2);

                executionResult.ReportOutputContent(fileArtifact, fileMaterializationInfo, PipOutputOrigin.NotMaterialized);
            }

            executionResult.SetResult(operationContext, PipResultStatus.NotMaterialized);
            return executionResult;
        }

        private static async Task<bool> ReplayWarningsFromCacheAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            Process pip,
            RunnableFromCacheResult.CacheHitData cacheHitData)
        {
            Contract.Requires(environment != null);
            Contract.Requires(pip != null);
            Contract.Requires(cacheHitData.Metadata.NumberOfWarnings > 0);

            // TODO: Deploying here is redundant if the console stream was also a declared output.
            //       Should collapse together such that console streams are always declared outputs.
            var fileContentManager = environment.State.FileContentManager;
            IEnumerable<(FileArtifact, ContentHash)> failedFiles = CollectionUtilities.EmptyArray<(FileArtifact, ContentHash)>();

            if (cacheHitData.StandardOutput != null)
            {
                var file = GetCachedSandboxedProcessOutputArtifact(cacheHitData, pip, SandboxedProcessFile.StandardOutput);
                failedFiles = await TryMaterializeStandardOutputFileHelperAsync(operationContext, pip, fileContentManager, failedFiles, file, cacheHitData.StandardOutput.Item2);
            }

            if (cacheHitData.StandardError != null)
            {
                var file = GetCachedSandboxedProcessOutputArtifact(cacheHitData, pip, SandboxedProcessFile.StandardError);
                failedFiles = await TryMaterializeStandardOutputFileHelperAsync(operationContext, pip, fileContentManager, failedFiles, file, cacheHitData.StandardError.Item2);
            }

            if (failedFiles.Any())
            {
                // FileContentManager will log warnings for materialization failures
                // Log overall error for failed materialization
                Logger.Log.PipFailedToMaterializeItsOutputs(
                    operationContext,
                    pip.GetDescription(environment.Context),
                    new ArtifactMaterializationFailure(failedFiles.ToReadOnlyArray(), environment.Context.PathTable).DescribeIncludingInnerFailures());
                return false;
            }

            var pathTable = environment.Context.PathTable;
            var configuration = environment.Configuration;
            SemanticPathExpander semanticPathExpander = state.PathExpander;
            var pipDataRenderer = new PipFragmentRenderer(
                pathTable,
                monikerRenderer: monikerGuid => environment.IpcProvider.LoadAndRenderMoniker(monikerGuid),
                hashLookup: environment.ContentFingerprinter.ContentHashLookupFunction);

            var executor = new SandboxedProcessPipExecutor(
                environment.Context,
                operationContext.LoggingContext,
                pip,
                configuration.Sandbox,
                configuration.Layout,
                configuration.Logging,
                environment.RootMappings,
                environment.ProcessInContainerManager,
                pipEnvironment: environment.State.PipEnvironment,
                validateDistribution: configuration.Distribution.ValidateDistribution,
                directoryArtifactContext: new DirectoryArtifactContext(environment),
                whitelist: null,
                makeInputPrivate: null,
                makeOutputPrivate: null,
                semanticPathExpander: semanticPathExpander,
                disableConHostSharing: configuration.Engine.DisableConHostSharing,
                pipDataRenderer: pipDataRenderer,
                directoryTranslator: environment.DirectoryTranslator,
                vmInitializer: environment.VmInitializer);

            if (!await executor.TryInitializeWarningRegexAsync())
            {
                Contract.Assert(operationContext.LoggingContext.ErrorWasLogged, "Error was not logged for initializing the warning regex");
                return false;
            }

            var standardOutput = GetOptionalSandboxedProcessOutputFromFile(pathTable, cacheHitData.StandardOutput, SandboxedProcessFile.StandardOutput);
            var standardError = GetOptionalSandboxedProcessOutputFromFile(pathTable, cacheHitData.StandardError, SandboxedProcessFile.StandardError);
            var success = await executor.TryLogWarningAsync(standardOutput, standardError);

            if (success)
            {
                environment.ReportWarnings(fromCache: true, count: cacheHitData.Metadata.NumberOfWarnings);
            }

            return true;
        }

        private static async Task<IEnumerable<(FileArtifact, ContentHash)>> TryMaterializeStandardOutputFileHelperAsync(
            OperationContext operationContext,
            Process pip,
            FileContentManager fileContentManager,
            IEnumerable<(FileArtifact, ContentHash)> failedFiles,
            FileArtifact file,
            ContentHash contentHash)
        {
            var filesToMaterialize = new[] { file };
            var result = await fileContentManager.TryMaterializeFilesAsync(
                    requestingPip: pip,
                    operationContext: operationContext,
                    filesToMaterialize: filesToMaterialize,
                    materializatingOutputs: true,
                    isDeclaredProducer: pip.GetOutputs().Contains(file));

            if (result != ArtifactMaterializationResult.Succeeded)
            {
                failedFiles = failedFiles.Concat(new[] { (file, contentHash) });
            }

            return failedFiles;
        }

        private static FileArtifact GetCachedSandboxedProcessOutputArtifact(
            RunnableFromCacheResult.CacheHitData cacheHitData,
            Process pip,
            SandboxedProcessFile file)
        {
            var standardFileData = file == SandboxedProcessFile.StandardError ?
                cacheHitData.StandardError :
                cacheHitData.StandardOutput;

            if (standardFileData == null)
            {
                return FileArtifact.Invalid;
            }

            FileArtifact pipStandardFileArtifact = file.PipFileArtifact(pip);
            AbsolutePath standardFilePath = standardFileData.Item1;

            if (pipStandardFileArtifact.Path == standardFilePath)
            {
                return pipStandardFileArtifact;
            }

            return FileArtifact.CreateOutputFile(standardFilePath);
        }

        private static SandboxedProcessOutput GetOptionalSandboxedProcessOutputFromFile(
            PathTable pathTable,
            Tuple<AbsolutePath, ContentHash, string> output,
            SandboxedProcessFile file)
        {
            return output == null
                ? null
                : SandboxedProcessOutput.FromFile(
                    output.Item1.ToString(pathTable),
                    output.Item3,
                    file);
        }

        private static RunnableFromCacheResult.CacheHitData TryCreatePipCacheDescriptorFromMetadata(
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            CacheablePip pip,
            PipCacheDescriptorV2Metadata metadata,
            PublishedEntryRefLocality refLocality,
            ContentHash pathSetHash,
            StrongContentFingerprint strongFingerprint,
            ContentHash metadataHash,
            ObservedPathSet? pathSet)
        {
            Contract.Requires(environment != null);
            Contract.Requires(state != null);
            Contract.Requires(pip != null);

            var pathTable = environment.Context.PathTable;
            var stringTable = environment.Context.StringTable;
            var pathExpander = state.PathExpander;

            if (metadata.StaticOutputHashes.Count != pip.CacheableStaticOutputsCount ||
                metadata.DynamicOutputs.Count != pip.DirectoryOutputs.Length)
            {
                return null;
            }

            // TODO: We store a (path, hash, encoding) tuple for stdout/stderr on the metadata. This is because
            //       these streams have been special cases, and the output paths are not necessarily even declared on the pip...
            Tuple<AbsolutePath, ContentHash, string> standardOutput;
            Tuple<AbsolutePath, ContentHash, string> standardError;
            if (!TryParseOptionalStandardConsoleStreamHash(pathTable, pathExpander, metadata.StandardOutput, out standardOutput) ||
                !TryParseOptionalStandardConsoleStreamHash(pathTable, pathExpander, metadata.StandardError, out standardError))
            {
                return null;
            }

            List<FileArtifact> absentArtifacts = null; // Almost never populated, since outputs are almost always required.
            List<(FileArtifact, FileMaterializationInfo)> cachedArtifactContentHashes =
                new List<(FileArtifact, FileMaterializationInfo)>(pip.Outputs.Length);

            // Outputs should be the same as what was in the metadata section.
            int outputHashIndex = 0;
            for (int i = 0; i < pip.Outputs.Length; i++)
            {
                FileArtifactWithAttributes attributedOutput = pip.Outputs[i];
                if (!attributedOutput.CanBeReferencedOrCached())
                {
                    // Skipping non-cacheable outputs (note that StoreContentForProcess does the same).
                    continue;
                }

                FileArtifact output = attributedOutput.ToFileArtifact();

                FileMaterializationInfo materializationInfo = metadata.StaticOutputHashes[outputHashIndex].ToFileMaterializationInfo(pathTable);
                outputHashIndex++;

                // Following logic should be in sync with StoreContentForProcess method.
                bool isRequired = IsRequiredForCaching(attributedOutput);
                if (materializationInfo.Hash != WellKnownContentHashes.AbsentFile)
                {
                    cachedArtifactContentHashes.Add((output, materializationInfo));
                }
                else if (isRequired)
                {
                    // Required but looks absent; entry is invalid.
                    return null;
                }
                else
                {
                    if (absentArtifacts == null)
                    {
                        absentArtifacts = new List<FileArtifact>();
                    }

                    absentArtifacts.Add(output);
                }
            }

            int staticExistentArtifactCount = cachedArtifactContentHashes.Count;

            // For each opaque directory, iterate its dynamic outputs which are stored in cache descriptor metadata.
            // The ordering of pip.DirectoryOutputs and metadata.DynamicOutputs is consistent.
            var opaqueIndex = 0;
            foreach (var opaqueDir in pip.DirectoryOutputs)
            {
                var dirPath = opaqueDir.Path;
                foreach (var dynamicOutput in metadata.DynamicOutputs[opaqueIndex++])
                {
                    // Dynamic output is stored with content hash and relative path from its opaque directory.
                    var filePath = dirPath.Combine(pathTable, RelativePath.Create(stringTable, dynamicOutput.RelativePath));
                    FileArtifact outputFile = FileArtifact.CreateOutputFile(filePath);
                    cachedArtifactContentHashes.Add((outputFile, dynamicOutput.Info.ToFileMaterializationInfo(pathTable)));
                }
            }

            var cachedArtifactContentHashesArray = cachedArtifactContentHashes.ToArray();

            // Create segments of cached artifact contents array that correspond to dynamic directory contents
            var dynamicDirectoryContents = new ArrayView<(FileArtifact, FileMaterializationInfo)>[pip.DirectoryOutputs.Length];
            int lastDynamicArtifactIndex = staticExistentArtifactCount;
            for (int i = 0; i < metadata.DynamicOutputs.Count; i++)
            {
                var directoryContentsCount = metadata.DynamicOutputs[i].Count;

                dynamicDirectoryContents[i] = new ArrayView<(FileArtifact, FileMaterializationInfo)>(
                    cachedArtifactContentHashesArray,
                    lastDynamicArtifactIndex,
                    directoryContentsCount);

                lastDynamicArtifactIndex += directoryContentsCount;
            }

            return new RunnableFromCacheResult.CacheHitData(
                    pathSetHash: pathSetHash,
                    strongFingerprint: strongFingerprint,
                    metadata: metadata,
                    cachedArtifactContentHashes: cachedArtifactContentHashesArray,
                    absentArtifacts: (IReadOnlyList<FileArtifact>)absentArtifacts ?? CollectionUtilities.EmptyArray<FileArtifact>(),
                    standardError: standardError,
                    standardOutput: standardOutput,
                    dynamicDirectoryContents: dynamicDirectoryContents,
                    locality: refLocality,
                    metadataHash: metadataHash,
                    pathSet: pathSet);
        }

        private static bool TryParseOptionalStandardConsoleStreamHash(
            PathTable pathTable,
            PathExpander semanticPathExpander,
            EncodedStringKeyedHash standardConsoleStream,
            out Tuple<AbsolutePath, ContentHash, string> resolvedStandardConsoleStream)
        {
            if (standardConsoleStream == null)
            {
                resolvedStandardConsoleStream = null;
                return true;
            }

            AbsolutePath path;
            if (!semanticPathExpander.TryCreatePath(pathTable, standardConsoleStream.StringKeyedHash.Key, out path))
            {
                resolvedStandardConsoleStream = null;
                return false;
            }

            resolvedStandardConsoleStream = Tuple.Create(path, standardConsoleStream.StringKeyedHash.ContentHash.ToContentHash(), standardConsoleStream.EncodingName);
            return true;
        }

        private readonly struct TwoPhasePathSetValidationTarget : IObservedInputProcessingTarget<ObservedPathEntry>
        {
            private readonly string m_pipDescription;
            private readonly OperationContext m_operationContext;
            private readonly PathTable m_pathTable;
            private readonly IPipExecutionEnvironment m_environment;

            public TwoPhasePathSetValidationTarget(IPipExecutionEnvironment environment, OperationContext operationContext, string pipDescription, PathTable pathTable)
            {
                m_environment = environment;
                m_pipDescription = pipDescription;
                m_operationContext = operationContext;
                m_pathTable = pathTable;
            }

            public string Description => m_pipDescription;

            public AbsolutePath GetPathOfObservation(ObservedPathEntry assertion)
            {
                return assertion.Path;
            }

            public ObservationFlags GetObservationFlags(ObservedPathEntry assertion)
            {
                return (assertion.IsFileProbe ? ObservationFlags.FileProbe : ObservationFlags.None) |
                    (assertion.IsDirectoryPath ? ObservationFlags.DirectoryLocation : ObservationFlags.None) |
                    // If there are enumerations on the Path then it is an enumeration.
                    (assertion.DirectoryEnumeration ? ObservationFlags.Enumeration : ObservationFlags.None);
            }

            public ObservedInputAccessCheckFailureAction OnAccessCheckFailure(ObservedPathEntry assertion, bool fromTopLevelDirectory)
            {
                // The path can't be accessed. Note that we don't apply a whitelist here (that only applies to process execution).
                // We let this cause overall failure (i.e., a failed ObservedInputProcessingResult, and an undefined StrongContentFingerprint).
                if (!BuildXL.Scheduler.ETWLogger.Log.IsEnabled(EventLevel.Verbose, BuildXL.Utilities.Tracing.Events.Keywords.Diagnostics))
                {
                    Logger.Log.PathSetValidationTargetFailedAccessCheck(m_operationContext, m_pipDescription, assertion.Path.ToString(m_pathTable));
                }

                return ObservedInputAccessCheckFailureAction.Fail;
            }

            public void CheckProposedObservedInput(ObservedPathEntry assertion, ObservedInput proposedObservedInput)
            {
                return;
            }

            public bool IsSearchPathEnumeration(ObservedPathEntry directoryEnumeration)
            {
                return directoryEnumeration.IsSearchPath;
            }

            public string GetEnumeratePatternRegex(ObservedPathEntry directoryEnumeration)
            {
                return directoryEnumeration.EnumeratePatternRegex;
            }

            public void ReportUnexpectedAccess(ObservedPathEntry assertion, ObservedInputType observedInputType)
            {
                if (m_environment.Configuration.Schedule.UnexpectedSymlinkAccessReportingMode == UnexpectedSymlinkAccessReportingMode.All)
                {
                    m_environment.State.FileContentManager.ReportUnexpectedSymlinkAccess(m_operationContext, m_pipDescription, assertion.Path, observedInputType, reportedAccesses: default(CompactSet<ReportedFileAccess>));
                }
            }

            public bool IsReportableUnexpectedAccess(AbsolutePath path)
            {
                return m_environment.Configuration.Schedule.UnexpectedSymlinkAccessReportingMode == UnexpectedSymlinkAccessReportingMode.All &&
                    m_environment.State.FileContentManager.TryGetSymlinkPathKind(path, out var kind);
            }
        }

        private readonly struct ObservedFileAccessValidationTarget : IObservedInputProcessingTarget<ObservedFileAccess>
        {
            private readonly IPipExecutionEnvironment m_environment;
            private readonly OperationContext m_operationContext;
            private readonly FileAccessReportingContext m_fileAccessReportingContext;
            private readonly string m_processDescription;
            private readonly PipExecutionState.PipScopeState m_state;

            public string Description => m_processDescription;

            public ObservedFileAccessValidationTarget(
                OperationContext operationContext,
                IPipExecutionEnvironment environment,
                FileAccessReportingContext fileAccessReportingContext,
                PipExecutionState.PipScopeState state,
                string processDescription)
            {
                m_operationContext = operationContext;
                m_environment = environment;
                m_processDescription = processDescription;
                m_fileAccessReportingContext = fileAccessReportingContext;
                m_state = state;
            }

            public ObservationFlags GetObservationFlags(ObservedFileAccess observation)
            {
                return observation.ObservationFlags;
            }

            public AbsolutePath GetPathOfObservation(ObservedFileAccess observation)
            {
                return observation.Path;
            }

            public void CheckProposedObservedInput(ObservedFileAccess observation, ObservedInput proposedObservedInput)
            {
                return;
            }

            public void ReportUnexpectedAccess(ObservedFileAccess observation, ObservedInputType observedInputType)
            {
                if (m_environment.Configuration.Schedule.UnexpectedSymlinkAccessReportingMode != UnexpectedSymlinkAccessReportingMode.None)
                {
                    m_environment.State.FileContentManager.ReportUnexpectedSymlinkAccess(
                        m_operationContext,
                        m_processDescription,
                        observation.Path,
                        observedInputType,
                        observation.Accesses);
                }
            }

            public bool IsReportableUnexpectedAccess(AbsolutePath path)
            {
                return m_environment.Configuration.Schedule.UnexpectedSymlinkAccessReportingMode != UnexpectedSymlinkAccessReportingMode.None &&
                    m_environment.State.FileContentManager.TryGetSymlinkPathKind(path, out var kind);
            }

            public ObservedInputAccessCheckFailureAction OnAccessCheckFailure(ObservedFileAccess observation, bool fromTopLevelDirectory)
            {
                // TODO: Should be able to log provenance of the sealed directory here (we don't even know which directory artifact corresponds).
                //       This is a fine argument to move this function into the execution environment.
                if (m_fileAccessReportingContext.MatchAndReportUnexpectedObservedFileAccess(observation) != FileAccessWhitelist.MatchType.NoMatch)
                {
                    return ObservedInputAccessCheckFailureAction.SuppressAndIgnorePath;
                }

                // If the access was whitelisted, some whitelist-related events will have been reported.
                // Otherwise, error or warning level events for the unexpected accesses will have been reported; in
                // that case we will additionally log a final message specific to this being a sealed directory
                // related issue (see/* TODO:above about logging provenance of a containing seal).
                if (fromTopLevelDirectory)
                {
                    Logger.Log.DisallowedFileAccessInTopOnlySourceSealedDirectory(m_operationContext, m_processDescription, observation.Path.ToString(m_environment.Context.PathTable));
                }
                else
                {
                    Logger.Log.ScheduleDisallowedFileAccessInSealedDirectory(m_operationContext, m_processDescription, observation.Path.ToString(m_environment.Context.PathTable));
                }

                return ObservedInputAccessCheckFailureAction.Fail;
            }

            public bool IsSearchPathEnumeration(ObservedFileAccess directoryEnumeration)
            {
                // A directory enumeration is a search path enumeration if at least one of the accessing tools are marked
                // as search path enumeration tools in the directory membership fingerprinter rule set
                string lastToolPath = null;
                foreach (var access in directoryEnumeration.Accesses)
                {
                    if (access.Process.Path == lastToolPath)
                    {
                        // Skip if we already checked this path
                        continue;
                    }

                    if (access.RequestedAccess != RequestedAccess.Enumerate)
                    {
                        continue;
                    }

                    lastToolPath = access.Process.Path;
                    if (m_state.DirectoryMembershipFingerprinterRuleSet?.IsSearchPathEnumerationTool(access.Process.Path) == true)
                    {
                        return true;
                    }
                }

                return false;
            }

            public string GetEnumeratePatternRegex(ObservedFileAccess directoryEnumeration)
            {
                using (var setPool = Pools.GetStringSet())
                {
                    var set = setPool.Instance;
                    foreach (var access in directoryEnumeration.Accesses)
                    {
                        if (access.RequestedAccess != RequestedAccess.Enumerate)
                        {
                            continue;
                        }

                        if (m_state.DirectoryMembershipFingerprinterRuleSet?.IsSearchPathEnumerationTool(access.Process.Path) == true)
                        {
                            continue;
                        }

                        if (access.EnumeratePattern == null)
                        {
                            Contract.Assert(false, "Enumerate pattern cannot be null: " + directoryEnumeration.Path.ToString(m_environment.Context.PathTable) + Environment.NewLine
                                + string.Join(Environment.NewLine, directoryEnumeration.Accesses.Select(a => a.Describe())));
                        }

                        set.Add(access.EnumeratePattern);
                    }

                    return RegexDirectoryMembershipFilter.ConvertWildcardsToRegex(set.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToArray());
                }
            }
        }

        /// <summary>
        /// Processes an <see cref="ObservedPathSet"/> consisting of prior observed accesses (as if the process were to run now, and
        /// access these paths). If all accesses are allowable for the pip (this may not be the case due to pip graph changes since the prior execution),
        /// this function returns a <see cref="StrongContentFingerprint"/>. That returned fingerprint extends the provided <paramref name="weakFingerprint"/>
        /// to account for those additional inputs; a prior execution for the same strong fingerprint is safely re-usable.
        /// Note that if the returned processing status is <see cref="ObservedInputProcessingStatus.Aborted"/>, then a failure has been logged and pip
        /// execution must fail.
        /// </summary>
        private static async Task<(bool, ObservedInputProcessingResult, StrongContentFingerprint?, ObservedPathSet, ContentHash)> TryComputeStrongFingerprintBasedOnPriorObservedPathSetAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            CacheablePip pip,
            WeakContentFingerprint weakFingerprint,
            ObservedPathSet pathSet,
            ContentHash pathSetHash)
        {
            Contract.Requires(environment != null);
            Contract.Requires(pip != null);
            Contract.Requires(pathSet.Paths.IsValid);

            using (operationContext.StartOperation(PipExecutorCounter.PriorPathSetEvaluationToProduceStrongFingerprintDuration))
            {
                ObservedInputProcessingResult validationResult =
                    await ObservedInputProcessor.ProcessPriorPathSetAsync(
                        operationContext,
                        environment,
                        state,
                        new TwoPhasePathSetValidationTarget(environment, operationContext, pip.Description, environment.Context.PathTable),
                        pip,
                        pathSet);

                environment.Counters.IncrementCounter(PipExecutorCounter.PriorPathSetsEvaluatedToProduceStrongFingerprint);

                // force cache miss if observed input processing result is not 'Success'
                if (validationResult.Status != ObservedInputProcessingStatus.Success)
                {
                    return (false, validationResult, default(StrongContentFingerprint?), default(ObservedPathSet), default(ContentHash));
                }

                // check if now running with safer options than before (i.e., prior are not strictly safer than current)
                var currentUnsafeOptions = state.UnsafeOptions;
                var priorUnsafeOptions = pathSet.UnsafeOptions;

                // prior options are safer --> use the precomputed path set hash to aim for a cache hit
                var finalPathSetHash = pathSetHash;
                var finalPathSet = pathSet;
                if (!priorUnsafeOptions.IsAsSafeOrSaferThan(currentUnsafeOptions))
                {
                    // prior options are less safe --> compute new path set hash (with updated unsafe options)
                    finalPathSet = pathSet.WithUnsafeOptions(currentUnsafeOptions);
                    finalPathSetHash = await pathSet.WithUnsafeOptions(currentUnsafeOptions).ToContentHash(environment.Context.PathTable, state.PathExpander);
                }

                // log and compute strong fingerprint using the PathSet hash from the cache
                LogInputAssertions(
                    operationContext,
                    environment.Context,
                    pip,
                    validationResult);

                StrongContentFingerprint? strongFingerprint;
                using (operationContext.StartOperation(PipExecutorCounter.ComputeStrongFingerprintDuration))
                {
                    strongFingerprint = validationResult.ComputeStrongFingerprint(
                        environment.Context.PathTable,
                        weakFingerprint,
                        finalPathSetHash);
                }

                return (true, validationResult, strongFingerprint, finalPathSet, finalPathSetHash);
            }
        }

        /// <summary>
        /// Validates that all observed file accesses of a pip's execution are allowable, based on the pip's declared dependencies and configuration.
        /// Note that **<paramref name="observedFileAccesses"/> may be sorted in place**.
        /// </summary>
        private static async Task<ObservedInputProcessingResult> ValidateObservedFileAccesses(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            CacheablePip pip,
            FileAccessReportingContext fileAccessReportingContext,
            SortedReadOnlyArray<ObservedFileAccess, ObservedFileAccessExpandedPathComparer> observedFileAccesses,
            bool trackFileChanges = true)
        {
            Contract.Requires(environment != null);
            Contract.Requires(pip != null);
            Contract.Requires(fileAccessReportingContext != null);

            var target = new ObservedFileAccessValidationTarget(
                operationContext,
                environment,
                fileAccessReportingContext,
                state,
                pip.Description);

            var result = await ObservedInputProcessor.ProcessNewObservationsAsync(
                operationContext,
                environment,
                state,
                target,
                pip,
                observedFileAccesses,
                trackFileChanges);

            LogInputAssertions(
                operationContext,
                environment.Context,
                pip,
                result);

            return result;
        }

        /// <summary>
        /// We have optional tracing for all discovered directory / file / absent-path dependencies.
        /// This function dumps out the result of an <see cref="ObservedInputProcessor"/> if needed.
        /// Note that this is generic to cache-hit vs. cache-miss as well as single-phase vs. two-phase lookup.
        /// </summary>
        private static void LogInputAssertions(
            OperationContext operationContext,
            PipExecutionContext context,
            CacheablePip pip,
            ObservedInputProcessingResult processedInputs)
        {
            // ObservedInputs are only available on processing success.
            if (processedInputs.Status != ObservedInputProcessingStatus.Success)
            {
                return;
            }

            // Tracing input assertions is expensive (many events and many string expansions); we avoid tracing when nobody is listening.
            if (!BuildXL.Scheduler.ETWLogger.Log.IsEnabled(EventLevel.Verbose, BuildXL.Utilities.Tracing.Events.Keywords.Diagnostics))
            {
                return;
            }

            foreach (ObservedInput input in processedInputs.ObservedInputs)
            {
                if (input.Type == ObservedInputType.DirectoryEnumeration)
                {
                    Logger.Log.PipDirectoryMembershipAssertion(
                        operationContext,
                        pip.Description,
                        input.Path.ToString(context.PathTable),
                        input.Hash.ToHex());
                }
                else
                {
                    Logger.Log.TracePipInputAssertion(
                        operationContext,
                        pip.Description,
                        input.Path.ToString(context.PathTable),
                        input.Hash.ToHex());
                }
            }
        }

        /// <summary>
        /// Attempt to bring multiple file contents into the local cache.
        /// </summary>
        /// <remarks>
        /// May log warnings (but not errors) on failure.
        /// </remarks>
        /// <param name="operationContext">Logging context associated with the pip</param>
        /// <param name="environment">Execution environment</param>
        /// <param name="pip">Pip that requested these contents (for logging)</param>
        /// <param name="cachedArtifactContentHashes">Enumeration of content to copy locally.</param>
        /// <param name="strongFingerprint">the associated strong fingerprint</param>
        /// <param name="metadataHash">the hash of the metadata which entry which references the content</param>
        /// <param name="standardOutput">Standard output</param>
        /// <param name="standardError">Standard error</param>
        /// <returns>True if all succeeded, otherwise false.</returns>
        private static async Task<bool> TryLoadAvailableOutputContentAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            CacheablePip pip,
            IReadOnlyCollection<(FileArtifact fileArtifact, FileMaterializationInfo fileMaterializationInfo)> cachedArtifactContentHashes,
            StrongContentFingerprint strongFingerprint,
            ContentHash metadataHash,
            Tuple<AbsolutePath, ContentHash, string> standardOutput = null,
            Tuple<AbsolutePath, ContentHash, string> standardError = null)
        {
            Contract.Requires(environment != null);
            Contract.Requires(pip != null);
            Contract.Requires(cachedArtifactContentHashes != null);

            var allHashes =
                new List<(FileArtifact, ContentHash)>(
                    cachedArtifactContentHashes.Count + (standardError == null ? 0 : 1) + (standardOutput == null ? 0 : 1));

            // only check/load "real" files - reparse points are not stored in CAS, they are stored in metadata that we have already obtained
            // if we try to load reparse points' content from CAS, content availability check would fail, and as a result,
            // BuildXL would have to re-run the pip (even if all other outputs are available)
            // Also, do not load zero-hash files (there is nothing in CAS with this hash) 
            allHashes.AddRange(cachedArtifactContentHashes
                .Where(pair => pair.fileMaterializationInfo.IsCacheable)
                .Select(a => (a.fileArtifact, a.fileMaterializationInfo.Hash)));

            if (standardOutput != null)
            {
                allHashes.Add((FileArtifact.CreateOutputFile(standardOutput.Item1), standardOutput.Item2));
            }

            if (standardError != null)
            {
                allHashes.Add((FileArtifact.CreateOutputFile(standardError.Item1), standardError.Item2));
            }

            // Check whether the cache provides a strong guarantee that content will be available for a successful pin
            bool hasStrongOutputContentAvailabilityGuarantee = environment.State.Cache.HasStrongOutputContentAvailabilityGuarantee(metadataHash);

            // When VerifyCacheLookupPin is specified (and cache provides no strong guarantee), we need to materialize as well to ensure the content is actually available
            bool materializeToVerifyAvailability = environment.Configuration.Schedule.VerifyCacheLookupPin && !hasStrongOutputContentAvailabilityGuarantee;

            // If pin cached outputs is off and verify cache lookup pin is not enabled/triggered for the current pip,
            // then just return true.
            if (!environment.Configuration.Schedule.PinCachedOutputs && !materializeToVerifyAvailability)
            {
                return true;
            }

            using (operationContext.StartOperation(materializeToVerifyAvailability ?
                PipExecutorCounter.TryLoadAvailableOutputContent_VerifyCacheLookupPinDuration :
                PipExecutorCounter.TryLoadAvailableOutputContent_PinDuration))
            {
                var succeeded = await environment.State.FileContentManager.TryLoadAvailableOutputContentAsync(
                    pip,
                    operationContext,
                    allHashes,
                    materialize: materializeToVerifyAvailability);

                if (materializeToVerifyAvailability || !succeeded)
                {
                    environment.State.Cache.RegisterOutputContentMaterializationResult(strongFingerprint, metadataHash, succeeded);
                }

                return succeeded;
            }
        }

        [Flags]
        private enum OutputFlags
        {
            /// <summary>
            /// Declared output file. <see cref="Process.FileOutputs"/>
            /// </summary>
            DeclaredFile = 1,

            /// <summary>
            /// Dynamic output file under process output directory. <see cref="Process.DirectoryOutputs"/>
            /// </summary>
            DynamicFile = 1 << 1,

            /// <summary>
            /// Standard output file
            /// </summary>
            StandardOut = 1 << 2,

            /// <summary>
            /// Standard error file
            /// </summary>
            StandardError = 1 << 3,
        }

        /// <summary>
        /// The file artifact with attributes and output flags
        /// </summary>
        private struct FileOutputData
        {
            /// <summary>
            /// The output file artifact.
            /// </summary>
            internal int OpaqueDirectoryIndex;

            /// <summary>
            /// The flags associated with the output file
            /// </summary>
            internal OutputFlags Flags;

            /// <summary>
            /// Gets whether all the given flags are applicable to the output file
            /// </summary>
            internal bool HasAllFlags(OutputFlags flags)
            {
                return (Flags & flags) == flags;
            }

            /// <summary>
            /// Gets whether any of the given flags are applicable to the output file
            /// </summary>
            internal bool HasAnyFlag(OutputFlags flags)
            {
                return (Flags & flags) != 0;
            }

            /// <summary>
            /// Updates the file data for the path in the map
            /// </summary>
            /// <param name="map">map of path to file data</param>
            /// <param name="path">the path to the output file</param>
            /// <param name="flags">flags to add (if any)</param>
            /// <param name="index">the opaque directory index (if applicable i.e. <paramref name="flags"/> is <see cref="OutputFlags.DynamicFile"/>)</param>
            internal static void UpdateFileData(Dictionary<AbsolutePath, FileOutputData> map, AbsolutePath path, OutputFlags? flags = null, int? index = null)
            {
                Contract.Assert(flags != OutputFlags.DynamicFile || index != null, "Opaque index must be specified for dynamic output files");

                FileOutputData fileData;
                if (!map.TryGetValue(path, out fileData))
                {
                    fileData = default(FileOutputData);
                }

                if (flags != null)
                {
                    fileData.Flags |= flags.Value;
                }

                if (index != null)
                {
                    fileData.OpaqueDirectoryIndex = index.Value;
                }

                map[path] = fileData;
            }
        }

        private static bool CheckForAllowedDirectorySymlinkOrJunctionProduction(AbsolutePath outputPath, OperationContext operationContext, string description, PathTable pathTable, ExecutionResult processExecutionResult)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                return true;
            }

            var pathstring = outputPath.ToString(pathTable);
            if (FileUtilities.IsDirectorySymlinkOrJunction(pathstring))
            {
                // We don't support storing directory symlinks/junctions to the cache in Windows right now.
                // We won't fail the pip
                // We won't cache it either
                Logger.Log.StorageSymlinkDirInOutputDirectoryWarning(
                    operationContext,
                    description,
                    pathstring);
                processExecutionResult.MustBeConsideredPerpetuallyDirty = true;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Discovers the content hashes of a process pip's outputs, which must now be on disk.
        /// The pip's outputs will be stored into the <see cref="IArtifactContentCache"/> of <see cref="IPipExecutionEnvironment.Cache"/>,
        /// and (if caching is enabled) a cache entry (the types varies; either single-phase or two-phase depending on configuration) will be created.
        /// The cache entry itself is not immediately stored, and is instead placed on the <paramref name="processExecutionResult"/>. This is so that
        /// in distributed builds, workers can handle output processing and validation but defer all metadata storage to the master.
        /// </summary>
        /// <remarks>
        /// This may be called even if the execution environment lacks a cache, in which case the outputs are hashed and reported (but nothing else).
        /// </remarks>
        /// <param name="operationContext">Current logging context</param>
        /// <param name="environment">Execution environment</param>
        /// <param name="state">Pip execution state</param>
        /// <param name="process">The process which has finished executing</param>
        /// <param name="description">Description of <paramref name="process"/></param>
        /// <param name="observedInputs">Observed inputs which should be part of the cache value</param>
        /// <param name="encodedStandardOutput">The optional standard output file</param>
        /// <param name="encodedStandardError">The optional standard error file</param>
        /// <param name="numberOfWarnings">Number of warnings found in standard output and error</param>
        /// <param name="processExecutionResult">The process execution result for recording process execution information</param>
        /// <param name="enableCaching">If set, the pip's descriptor and content will be stored to the cache. Otherwise, its outputs will be hashed but not stored or referenced by a new descriptor.</param>
        /// <param name="fingerprintComputation">Stores fingerprint computation information</param>
        /// <param name="containerConfiguration">The configuration used to run the process in a container, if that option was specified</param>
        private static async Task<bool> StoreContentForProcessAndCreateCacheEntryAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            Process process,
            string description,
            ObservedInputProcessingResult? observedInputs,
            Tuple<AbsolutePath, Encoding> encodedStandardOutput,
            Tuple<AbsolutePath, Encoding> encodedStandardError,
            int numberOfWarnings,
            ExecutionResult processExecutionResult,
            bool enableCaching,
            BoxRef<ProcessFingerprintComputationEventData> fingerprintComputation,
            ContainerConfiguration containerConfiguration)
        {
            Contract.Requires(environment != null);
            Contract.Requires(process != null);
            Contract.Requires(
                enableCaching == observedInputs.HasValue,
                "Should provide observed inputs (relevant only when caching) iff caching is enabled.");
            Contract.Requires(encodedStandardOutput == null || (encodedStandardOutput.Item1.IsValid && encodedStandardOutput.Item2 != null));
            Contract.Requires(encodedStandardError == null || (encodedStandardError.Item1.IsValid && encodedStandardError.Item2 != null));
            Contract.Requires(!processExecutionResult.IsSealed);

            var pathTable = environment.Context.PathTable;

            long totalOutputSize = 0;
            int numDynamicOutputs = 0;
            ContentHash? standardOutputContentHash = null;
            ContentHash? standardErrorContentHash = null;

            using (var poolFileArtifactWithAttributesList = Pools.GetFileArtifactWithAttributesList())
            using (var poolAbsolutePathFileOutputDataMap = s_absolutePathFileOutputDataMapPool.GetInstance())
            using (var poolAbsolutePathFileArtifactWithAttributes = Pools.GetAbsolutePathFileArtifactWithAttributesMap())
            using (var poolFileList = Pools.GetFileArtifactList())
            using (var poolAbsolutePathFileMaterializationInfoTuppleList = s_absolutePathFileMaterializationInfoTuppleListPool.GetInstance())
            using (var poolFileArtifactPossibleFileMaterializationInfoTaskMap = s_fileArtifactPossibleFileMaterializationInfoTaskMapPool.GetInstance())
            {
                // Each dynamic output should map to which opaque directory they belong.
                // Because we will store the hashes and paths of the dynamic outputs by preserving the ordering of process.DirectoryOutputs
                var allOutputs = poolFileArtifactWithAttributesList.Instance;
                var allOutputData = poolAbsolutePathFileOutputDataMap.Instance;

                // If container isolation is enabled, this will represent a map between original outputs and its redirected output
                // This is used to achieve output isolation: we keep the original paths, but use the content of redirected outputs
                Dictionary<AbsolutePath, FileArtifactWithAttributes> allRedirectedOutputs = null;
                if (containerConfiguration.IsIsolationEnabled)
                {
                    allRedirectedOutputs = poolAbsolutePathFileArtifactWithAttributes.Instance;
                }

                // Let's compute what got actually redirected
                bool outputFilesAreRedirected = containerConfiguration.IsIsolationEnabled && process.ContainerIsolationLevel.IsolateOutputFiles();
                bool exclusiveOutputDirectoriesAreRedirected = containerConfiguration.IsIsolationEnabled && process.ContainerIsolationLevel.IsolateExclusiveOpaqueOutputDirectories();
                bool sharedOutputDirectoriesAreRedirected = containerConfiguration.IsIsolationEnabled && process.ContainerIsolationLevel.IsolateSharedOpaqueOutputDirectories();

                foreach (var output in process.FileOutputs)
                {
                    FileOutputData.UpdateFileData(allOutputData, output.Path, OutputFlags.DeclaredFile);

                    if (!CheckForAllowedDirectorySymlinkOrJunctionProduction(output.Path, operationContext, description, pathTable, processExecutionResult))
                    {
                        enableCaching = false;
                        continue;
                    }

                    // If the directory containing the output file was redirected, then we want to cache the content of the redirected output instead.
                    if (outputFilesAreRedirected && environment.ProcessInContainerManager.TryGetRedirectedDeclaredOutputFile(output.Path, containerConfiguration, out AbsolutePath redirectedOutputPath))
                    {
                        var redirectedOutput = new FileArtifactWithAttributes(redirectedOutputPath, output.RewriteCount, output.FileExistence);
                        allRedirectedOutputs.Add(output.Path, redirectedOutput);
                    }

                    allOutputs.Add(output);
                }

                // We need to discover dynamic outputs in the given opaque directories.
                var fileList = poolFileList.Instance;

                for (int i = 0; i < process.DirectoryOutputs.Length; i++)
                {
                    fileList.Clear();
                    var directoryArtifact = process.DirectoryOutputs[i];
                    var directoryArtifactPath = directoryArtifact.Path;

                    var index = i;

                    // For the case of an opaque directory, the content is determined by scanning the file system
                    if (!directoryArtifact.IsSharedOpaque)
                    {
                        var enumerationResult = environment.State.FileContentManager.EnumerateDynamicOutputDirectory(
                            directoryArtifact,
                            handleFile: fileArtifact =>
                            {
                                if (!CheckForAllowedDirectorySymlinkOrJunctionProduction(fileArtifact.Path, operationContext, description, pathTable, processExecutionResult))
                                {
                                    enableCaching = false;
                                    return;
                                }

                                fileList.Add(fileArtifact);
                                FileOutputData.UpdateFileData(allOutputData, fileArtifact.Path, OutputFlags.DynamicFile, index);
                                var fileArtifactWithAttributes = fileArtifact.WithAttributes(FileExistence.Required);
                                allOutputs.Add(fileArtifactWithAttributes);

                                if (exclusiveOutputDirectoriesAreRedirected)
                                {
                                    PopulateRedirectedOutputsForFileInOpaque(pathTable, environment, containerConfiguration, directoryArtifactPath, fileArtifactWithAttributes, allRedirectedOutputs);
                                }
                            },

                            // TODO: Currently the logic skips empty subdirectories. The logic needs to preserve the structure of opaque directories.
                            // TODO: THe above issue is tracked by task 872930.
                            handleDirectory: null);

                        if (!enumerationResult.Succeeded)
                        {
                            Logger.Log.ProcessingPipOutputDirectoryFailed(
                                operationContext,
                                description,
                                directoryArtifactPath.ToString(pathTable),
                                enumerationResult.Failure.DescribeIncludingInnerFailures());
                            return false;
                        }
                    }
                    else
                    {
                        // For the case of shared opaque directories, the content is based on detours
                        var writeAccessesByPath = processExecutionResult.SharedDynamicDirectoryWriteAccesses;
                        if (!writeAccessesByPath.TryGetValue(directoryArtifactPath, out var accesses))
                        {
                            accesses = CollectionUtilities.EmptyArray<AbsolutePath>();
                        }

                        // So we know that all accesses here were write accesses, but we don't actually know if in the end the corresponding file
                        // still exists (the tool could have created it and removed it right after). So we only add existing files.
                        foreach (var access in accesses)
                        {
                            var maybeResult = FileUtilities.TryProbePathExistence(access.ToString(pathTable), followSymlink: false);
                            if (!maybeResult.Succeeded)
                            {
                                Logger.Log.ProcessingPipOutputDirectoryFailed(
                                    operationContext,
                                    description,
                                    directoryArtifactPath.ToString(pathTable),
                                    maybeResult.Failure.DescribeIncludingInnerFailures());
                                return false;
                            }

                            // TODO: directories are not reported as explicit content, since we don't have the functionality today to persist them in the cache.
                            // But we could do this here in the future
                            if (maybeResult.Result == PathExistence.ExistsAsDirectory)
                            {
                                continue;
                            }

                            if (!CheckForAllowedDirectorySymlinkOrJunctionProduction(access, operationContext, description, pathTable, processExecutionResult))
                            {
                                enableCaching = false;
                                continue;
                            }

                            var fileArtifact = FileArtifact.CreateOutputFile(access);
                            fileList.Add(fileArtifact);
                            FileOutputData.UpdateFileData(allOutputData, fileArtifact.Path, OutputFlags.DynamicFile, index);

                            var fileArtifactWithAttributes = fileArtifact.WithAttributes(
                                maybeResult.Result == PathExistence.Nonexistent
                                ? FileExistence.Temporary
                                : FileExistence.Required);
                            allOutputs.Add(fileArtifactWithAttributes);

                            if (sharedOutputDirectoriesAreRedirected)
                            {
                                PopulateRedirectedOutputsForFileInOpaque(pathTable, environment, containerConfiguration, directoryArtifactPath, fileArtifactWithAttributes, allRedirectedOutputs);
                            }
                        }

                        // The result of a shared opaque directory is always considered dirty
                        // since it is not clear how to infer what to run based on the state
                        // of the file system.
                        processExecutionResult.MustBeConsideredPerpetuallyDirty = true;
                    }

                    processExecutionResult.ReportDirectoryOutput(directoryArtifact, fileList);
                    numDynamicOutputs += fileList.Count;
                }

                if (encodedStandardOutput != null)
                {
                    var path = encodedStandardOutput.Item1;
                    FileOutputData.UpdateFileData(allOutputData, path, OutputFlags.StandardOut);
                    allOutputs.Add(FileArtifact.CreateOutputFile(path).WithAttributes(FileExistence.Required));
                }

                if (encodedStandardError != null)
                {
                    var path = encodedStandardError.Item1;
                    FileOutputData.UpdateFileData(allOutputData, path, OutputFlags.StandardError);
                    allOutputs.Add(FileArtifact.CreateOutputFile(path).WithAttributes(FileExistence.Required));
                }

                var outputHashPairs = poolAbsolutePathFileMaterializationInfoTuppleList.Instance;
                var storeProcessOutputCompletionsByPath = poolFileArtifactPossibleFileMaterializationInfoTaskMap.Instance;

                bool successfullyProcessedOutputs = true;

                SemaphoreSlim concurrencySemaphore = new SemaphoreSlim(Environment.ProcessorCount);
                foreach (var output in allOutputs)
                {
                    var outputData = allOutputData[output.Path];

                    // For all cacheable outputs, start a task to store into the cache
                    if (output.CanBeReferencedOrCached())
                    {
                        Task<Possible<FileMaterializationInfo>> storeCompletion;

                        // Deduplicate output store operations so outputs are not stored to the cache concurrently.
                        // (namely declared file outputs can also be under a dynamic directory as a dynamic output)
                        if (!storeProcessOutputCompletionsByPath.TryGetValue(output.ToFileArtifact(), out storeCompletion))
                        {
                            var contentToStore = output;
                            // If there is a redirected output for this path, use that one, so we always cache redirected outputs instead of the original ones
                            if (containerConfiguration.IsIsolationEnabled && allRedirectedOutputs.TryGetValue(output.Path, out var redirectedOutput))
                            {
                                contentToStore = redirectedOutput;
                            }

                            storeCompletion = Task.Run(
                                async () =>
                                {
                                    using (await concurrencySemaphore.AcquireAsync())
                                    {
                                        if (environment.Context.CancellationToken.IsCancellationRequested)
                                        {
                                            return environment.Context.CancellationToken.CreateFailure();
                                        }

                                        return await StoreCacheableProcessOutputAsync(
                                                environment,
                                                operationContext,
                                                process,
                                                contentToStore,
                                                outputData);
                                    }
                                });

                            storeProcessOutputCompletionsByPath[output.ToFileArtifact()] = storeCompletion;
                        }
                    }
                    else if (outputData.HasAllFlags(OutputFlags.DynamicFile))
                    {
                        // Do not attempt to store dynamic temporary files into cache. However, we store them as a part of metadata as files with AbsentFile hash,
                        // so accesses could be properly reported to FileMonitoringViolationAnalyzer on cache replay.
                        storeProcessOutputCompletionsByPath[output.ToFileArtifact()] = Task.FromResult(Possible.Create(FileMaterializationInfo.CreateWithUnknownLength(WellKnownContentHashes.AbsentFile)));
                    }
                }

                // We cannot enumerate over storeProcessOutputCompletionsByPath here
                // because the order of such an enumeration is not deterministic.
                foreach (var output in allOutputs)
                {
                    FileArtifact outputArtifact = output.ToFileArtifact();
                    if (storeProcessOutputCompletionsByPath.TryGetValue(outputArtifact, out var storeProcessOutputTask))
                    {
                        // the task is now 'processed' => remove it, so we do not add duplicate entries to outputHashPairs
                        storeProcessOutputCompletionsByPath.Remove(outputArtifact);
                    }
                    else
                    {
                        // there is no task for this artifact => we must have already processed it
                        continue;
                    }

                    var outputData = allOutputData[outputArtifact.Path];

                    Possible<FileMaterializationInfo> possiblyStoredOutputArtifactInfo;
                    using (operationContext.StartOperation(PipExecutorCounter.SerializeAndStorePipOutputDuration))
                    {
                        possiblyStoredOutputArtifactInfo = await storeProcessOutputTask;
                    }

                    if (possiblyStoredOutputArtifactInfo.Succeeded)
                    {
                        FileMaterializationInfo outputArtifactInfo = possiblyStoredOutputArtifactInfo.Result;
                        outputHashPairs.Add((outputArtifact.Path, outputArtifactInfo));

                        // Sometimes standard error / standard out is a declared output. Other times it is an implicit output that we shouldn't report.
                        // If it is a declared output, we notice that here and avoid trying to look at the file again below.
                        // Generally we want to avoid looking at a file repeatedly to avoid seeing it in multiple states (perhaps even deleted).
                        // TODO: Would be cleaner to always model console streams as outputs, but 'maybe present' (a generally useful status for outputs).
                        if (outputData.HasAllFlags(OutputFlags.StandardOut))
                        {
                            standardOutputContentHash = outputArtifactInfo.Hash;
                        }

                        if (outputData.HasAllFlags(OutputFlags.StandardError))
                        {
                            standardErrorContentHash = outputArtifactInfo.Hash;
                        }

                        PipOutputOrigin origin;
                        if (outputArtifactInfo.FileContentInfo.HasKnownLength)
                        {
                            totalOutputSize += outputArtifactInfo.Length;
                            origin = PipOutputOrigin.Produced;
                        }
                        else
                        {
                            // Absent file
                            origin = PipOutputOrigin.UpToDate;
                        }

                        if (!outputData.HasAnyFlag(OutputFlags.DeclaredFile | OutputFlags.DynamicFile))
                        {
                            // Only report output content if file is a 'real' pip output
                            // i.e. declared or dynamic output (not just standard out/error)
                            continue;
                        }

                        processExecutionResult.ReportOutputContent(
                            outputArtifact,
                            outputArtifactInfo,
                            origin);
                    }
                    else
                    {
                        if (!(possiblyStoredOutputArtifactInfo.Failure is CancellationFailure))
                        {
                            // Storing output to cache failed. Log failure.
                            Logger.Log.ProcessingPipOutputFileFailed(
                                operationContext,
                                description,
                                outputArtifact.Path.ToString(pathTable),
                                possiblyStoredOutputArtifactInfo.Failure.DescribeIncludingInnerFailures());
                        }

                        successfullyProcessedOutputs = false;
                    }
                }

                // Short circuit before updating cache to avoid potentially creating an incorrect cache descriptor since there may
                // be some missing output file hashes
                if (!successfullyProcessedOutputs)
                {
                    Contract.Assume(operationContext.LoggingContext.ErrorWasLogged);
                    return false;
                }

                Contract.Assert(encodedStandardOutput == null || standardOutputContentHash.HasValue, "Hashed as a declared output, or independently");
                Contract.Assert(encodedStandardError == null || standardErrorContentHash.HasValue, "Hashed as a declared output, or independently");

                if (enableCaching)
                {
                    Contract.Assert(observedInputs.HasValue);

                    PipCacheDescriptorV2Metadata metadata =
                        new PipCacheDescriptorV2Metadata
                        {
                            Id = PipFingerprintEntry.CreateUniqueId(),
                            NumberOfWarnings = numberOfWarnings,
                            StandardError = GetOptionalEncodedStringKeyedHash(environment, state, encodedStandardError, standardErrorContentHash),
                            StandardOutput = GetOptionalEncodedStringKeyedHash(environment, state, encodedStandardOutput, standardOutputContentHash),
                            TraceInfo = operationContext.LoggingContext.Session.Environment,
                            TotalOutputSize = totalOutputSize,
                            SemiStableHash = process.SemiStableHash,
                            WeakFingerprint = fingerprintComputation.Value.WeakFingerprint.ToString(),
                        };

                    RecordOutputsOnMetadata(metadata, process, allOutputData, outputHashPairs, pathTable);

                    // An assertion for the static outputs
                    Contract.Assert(metadata.StaticOutputHashes.Count == process.GetCacheableOutputsCount());

                    // An assertion for the dynamic outputs
                    Contract.Assert(metadata.DynamicOutputs.Sum(a => a.Count) == numDynamicOutputs);

                    var entryStore = await TryCreateTwoPhaseCacheEntryAndStoreMetadata(
                        operationContext,
                        environment,
                        state,
                        process,
                        description,
                        metadata,
                        outputHashPairs,
                        standardOutputContentHash,
                        standardErrorContentHash,
                        observedInputs.Value,
                        fingerprintComputation);

                    if (entryStore == null)
                    {
                        Contract.Assume(operationContext.LoggingContext.ErrorWasLogged);
                        return false;
                    }

                    processExecutionResult.TwoPhaseCachingInfo = entryStore;

                    if (environment.State.Cache.IsNewlyAdded(entryStore.PathSetHash))
                    {
                        processExecutionResult.PathSet = observedInputs.Value.GetPathSet(state.UnsafeOptions);
                    }

                    if (environment.State.Cache.IsNewlyAdded(entryStore.CacheEntry.MetadataHash))
                    {
                        processExecutionResult.PipCacheDescriptorV2Metadata = metadata;
                    }
                }

                return true;
            }
        }

        private static void PopulateRedirectedOutputsForFileInOpaque(PathTable pathTable, IPipExecutionEnvironment environment, ContainerConfiguration containerConfiguration, AbsolutePath opaqueDirectory, FileArtifactWithAttributes fileArtifactInOpaque, Dictionary<AbsolutePath, FileArtifactWithAttributes> allRedirectedOutputs)
        {
            if (environment.ProcessInContainerManager.TryGetRedirectedOpaqueFile(fileArtifactInOpaque.Path, opaqueDirectory, containerConfiguration, out AbsolutePath redirectedPath))
            {
                var redirectedOutput = new FileArtifactWithAttributes(redirectedPath, fileArtifactInOpaque.RewriteCount, fileArtifactInOpaque.FileExistence);
                allRedirectedOutputs.Add(fileArtifactInOpaque.Path, redirectedOutput);
            }
        }

        private static async Task<Possible<FileMaterializationInfo>> StoreCacheableProcessOutputAsync(
            IPipExecutionEnvironment environment,
            OperationContext operationContext,
            Process process,
            FileArtifactWithAttributes output,
            FileOutputData outputData)
        {
            Contract.Assert(output.CanBeReferencedOrCached());

            var pathTable = environment.Context.PathTable;

            FileArtifact outputArtifact = output.ToFileArtifact();

            ExpandedAbsolutePath expandedPath = outputArtifact.Path.Expand(pathTable);
            string path = expandedPath.ExpandedPath;

            var isRequired =
                // Dynamic outputs and standard files are required
                outputData.HasAnyFlag(OutputFlags.DynamicFile | OutputFlags.StandardError | OutputFlags.StandardOut) ||
                IsRequiredForCaching(output);

            // Store content for the existing outputs and report them.
            // For non-existing ones just store well known descriptors
            FileMaterializationInfo outputArtifactInfo;

            bool requiredOrExistent;
            if (isRequired)
            {
                requiredOrExistent = true;
            }
            else
            {
                // TODO: Shouldn't be doing a tracking probe here; instead should just make the store operation allow absence.
                // TODO: File.Exists returns false for directories; this is unintentional and we should move away from that (but that may break some existing users)
                //       So for now we replicate File.Exists behavior by checking for ExistsAsFile.
                // N.B. we use local-disk store here rather than the VFS. We need an authentic local-file-system result
                // (the VFS would just say 'output path should exist eventually', which is what we are working on).
                Possible<bool> possibleProbeResult =
                    environment.LocalDiskContentStore.TryProbeAndTrackPathForExistence(expandedPath)
                        .Then(existence => existence == PathExistence.ExistsAsFile);
                if (!possibleProbeResult.Succeeded)
                {
                    return possibleProbeResult.Failure;
                }

                requiredOrExistent = possibleProbeResult.Result;
            }

            if (requiredOrExistent)
            {
                bool isProcessPreservingOutputs = IsProcessPreservingOutputs(environment, process);
                bool isDynamicOutputFile = outputData.HasAnyFlag(OutputFlags.DynamicFile);
                bool isRewrittenOutputFile = IsRewriteOutputFile(environment, outputArtifact);

                bool shouldOutputBePreserved =
                    // Process is marked for allowing preserved output.
                    isProcessPreservingOutputs &&
                    // Preserving dynamic output is currently not supported.
                    !isDynamicOutputFile &&
                    // Rewritten output is stored to the cache.
                    !isRewrittenOutputFile;

                var reparsePointType = FileUtilities.TryGetReparsePointType(outputArtifact.Path.ToString(environment.Context.PathTable));

                bool isSymlink = reparsePointType.Succeeded && reparsePointType.Result == ReparsePointType.SymLink;

                bool shouldStoreOutputToCache =
                    ((environment.Configuration.Schedule.StoreOutputsToCache && !shouldOutputBePreserved) ||
                    isRewrittenOutputFile)
                    && !isSymlink;

                Possible<TrackedFileContentInfo> possiblyStoredOutputArtifact = shouldStoreOutputToCache
                    ? await StoreProcessOutputToCacheAsync(operationContext, environment, process, outputArtifact, isSymlink)
                    : await TrackPipOutputAsync(
                        operationContext,
                        environment,
                        outputArtifact,
                        environment.ShouldCreateHandleWithSequentialScan(outputArtifact),
                        isSymlink);

                if (!possiblyStoredOutputArtifact.Succeeded)
                {
                    return possiblyStoredOutputArtifact.Failure;
                }

                outputArtifactInfo = possiblyStoredOutputArtifact.Result.FileMaterializationInfo;
                return outputArtifactInfo;
            }

            outputArtifactInfo = FileMaterializationInfo.CreateWithUnknownLength(WellKnownContentHashes.AbsentFile);
            return outputArtifactInfo;
        }

        private static bool IsProcessPreservingOutputs(IPipExecutionEnvironment environment, Process process)
        {
            Contract.Requires(environment != null);
            Contract.Requires(process != null);

            return process.AllowPreserveOutputs &&
                   environment.Configuration.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs != PreserveOutputsMode.Disabled;
        }

        private static bool IsRewriteOutputFile(IPipExecutionEnvironment environment, FileArtifact file)
        {
            Contract.Requires(environment != null);
            Contract.Requires(file.IsOutputFile);

            // Either the file is the next version of an output file or it will be rewritten later.
            return file.RewriteCount > 1 || environment.IsFileRewritten(file);
        }

        /// <summary>
        /// Records the static and dynamic (SealedDynamicDirectories) outputs data on the cache entry.
        /// </summary>
        private static void RecordOutputsOnMetadata(
            PipCacheDescriptorV2Metadata metadata,
            Process process,
            Dictionary<AbsolutePath, FileOutputData> allOutputData,
            List<(AbsolutePath, FileMaterializationInfo)> outputHashPairs,
            PathTable pathTable)
        {
            // Initialize the list of dynamic outputs per directory output (opaque directory)
            for (int i = 0; i < process.DirectoryOutputs.Length; i++)
            {
                metadata.DynamicOutputs.Add(new List<RelativePathFileMaterializationInfo>());
            }

            foreach (var outputHashPair in outputHashPairs)
            {
                var path = outputHashPair.Item1;
                var materializationInfo = outputHashPair.Item2;
                var outputData = allOutputData[path];

                if (outputData.HasAllFlags(OutputFlags.DeclaredFile))
                {
                    // If it is a static output, just store its hash in the descriptor.
                    metadata.StaticOutputHashes.Add(materializationInfo.ToBondFileMaterializationInfo(pathTable));
                }

                if (outputData.HasAllFlags(OutputFlags.DynamicFile))
                {
                    // If it is a dynamic output, store the hash and relative path from the opaque directory by preserving the ordering.
                    int opaqueIndex = outputData.OpaqueDirectoryIndex;
                    Contract.Assert(process.DirectoryOutputs.Length > opaqueIndex);
                    RelativePath relativePath;
                    var success = process.DirectoryOutputs[opaqueIndex].Path.TryGetRelative(pathTable, path, out relativePath);
                    Contract.Assert(success);
                    var keyedHash = new RelativePathFileMaterializationInfo
                    {
                        RelativePath = relativePath.ToString(pathTable.StringTable),
                        Info = materializationInfo.ToBondFileMaterializationInfo(pathTable),
                    };
                    metadata.DynamicOutputs[opaqueIndex].Add(keyedHash);
                }
            }
        }

        /// <summary>
        /// Returns a cache entry that can later be stored to an <see cref="ITwoPhaseFingerprintStore"/>.
        /// In prep for storing the cache entry, we first store some supporting metadata content to the CAS:
        /// - The path-set (set of additional observed inputs, used to generate the strong fingerprint)
        /// - The metadata blob (misc. fields such as number of warnings, and provenance info).
        /// Some cache implementations may enforce that this content is stored in order to accept an entry.
        /// Returns 'null' if the supporting metadata cannot be stored.
        /// </summary>
        private static async Task<TwoPhaseCachingInfo> TryCreateTwoPhaseCacheEntryAndStoreMetadata(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            Process process,
            string processDescription,
            PipCacheDescriptorV2Metadata metadata,
            List<(AbsolutePath, FileMaterializationInfo)> outputHashPairs,
            ContentHash? standardOutputContentHash,
            ContentHash? standardErrorContentHash,
            ObservedInputProcessingResult observedInputProcessingResult,
            BoxRef<ProcessFingerprintComputationEventData> fingerprintComputation)
        {
            Contract.Requires(environment != null);
            Contract.Requires(metadata != null);
            Contract.Requires(outputHashPairs != null);

            var twoPhaseCache = environment.State.Cache;
            var weakFingerprint = fingerprintComputation.Value.WeakFingerprint;

            var pathSet = observedInputProcessingResult.GetPathSet(state.UnsafeOptions);
            Possible<ContentHash> maybePathSetStored = await environment.State.Cache.TryStorePathSetAsync(pathSet);
            if (!maybePathSetStored.Succeeded)
            {
                Logger.Log.TwoPhaseFailedToStoreMetadataForCacheEntry(
                    operationContext,
                    processDescription,
                    maybePathSetStored.Failure.Annotate("Unable to store path set.").DescribeIncludingInnerFailures());
                return null;
            }

            ContentHash pathSetHash = maybePathSetStored.Result;
            StrongContentFingerprint strongFingerprint;
            using (operationContext.StartOperation(PipExecutorCounter.ComputeStrongFingerprintDuration))
            {
                strongFingerprint = observedInputProcessingResult.ComputeStrongFingerprint(
                    environment.Context.PathTable,
                    weakFingerprint,
                    pathSetHash);
                metadata.StrongFingerprint = strongFingerprint.ToString();
            }

            fingerprintComputation.Value.StrongFingerprintComputations = new[]
            {
                ProcessStrongFingerprintComputationData.CreateForExecution(
                    pathSetHash,
                    pathSet,
                    observedInputProcessingResult.ObservedInputs,
                    strongFingerprint),
            };

            Possible<ContentHash> maybeStoredMetadata;
            using (operationContext.StartOperation(PipExecutorCounter.SerializeAndStorePipMetadataDuration))
            {
                // Note that we wrap the metadata in a PipFingerprintEntry before storing it; this is symmetric with the read-side (TryCreatePipCacheDescriptorFromMetadataAndReferencedContent)
                maybeStoredMetadata = await twoPhaseCache.TryStoreMetadataAsync(metadata);
                if (!maybeStoredMetadata.Succeeded)
                {
                    Logger.Log.TwoPhaseFailedToStoreMetadataForCacheEntry(
                        operationContext,
                        processDescription,
                        maybeStoredMetadata.Failure.Annotate("Unable to store metadata blob.").DescribeIncludingInnerFailures());
                    return null;
                }
            }

            ContentHash metadataHash = maybeStoredMetadata.Result;
            twoPhaseCache.RegisterOutputContentMaterializationResult(strongFingerprint, metadataHash, true);

            // Even though, we don't store the outputs to the cache when outputs should be preserved,
            // the output hashes are still included in the referenced contents.
            // Suppose that we don't include the output hashes. Let's have a pip P whose output o should be preserved.
            // P executes, and stores #M of metadata hash with some strong fingerprint SF.
            // Before the next build, o is deleted from disk. Now, P maintains its SF because its input has not changed.
            // P gets a cache hit, but when BuildXL tries to load o with #o (stored in M), it fails because o wasn't stored in the cache 
            // and o doesn't exist on disk. Thus, P executes and produces o with different hash #o'. However, the post-execution of P 
            // will fail to store #M because the entry has existed.
            //
            // In the next run, P again gets a cache hit, but when BuildXL tries to load o with #o (stored in M) it fails because o wasn't stored 
            // in the cache and o, even though exists on disk, has different hash (#o vs. #o'). Thus, P executes again and produces o with different hash #o''.
            // This will happen over and over again.
            //
            // If #o is also included in the reference content, then when cache cannot pin #o (because it was never stored in the cache),
            // then it removes the entry, and thus, the post-execution of P will succeed in storing (#M, #o').
            var referencedContent = new List<ContentHash>();

            Func<ContentHash?, bool> isValidContentHash = hash => hash != null && !hash.Value.IsSpecialValue();

            if (isValidContentHash(standardOutputContentHash))
            {
                referencedContent.Add(standardOutputContentHash.Value);
            }

            if (isValidContentHash(standardErrorContentHash))
            {
                referencedContent.Add(standardErrorContentHash.Value);
            }

            referencedContent.AddRange(outputHashPairs.Select(p => p.Item2).Where(fileMaterializationInfo => fileMaterializationInfo.IsCacheable).Select(fileMaterializationInfo => fileMaterializationInfo.Hash));

            return new TwoPhaseCachingInfo(
                weakFingerprint,
                pathSetHash,
                strongFingerprint,
                new CacheEntry(metadataHash, "<Unspecified>", referencedContent.ToArray()));
        }

        /// <summary>
        /// Returns true if the output is required for caching and validation is not disabled.
        /// </summary>
        private static bool IsRequiredForCaching(FileArtifactWithAttributes output)
        {
            return output.MustExist();
        }

        /// <summary>
        /// Attempts to store an already-constructed cache entry. Metadata and content should have been stored already.
        /// On any failure, this logs warnings.
        /// </summary>
        private static async Task<StoreCacheEntryResult> StoreTwoPhaseCacheEntryAsync(
            OperationContext operationContext,
            Process process,
            CacheablePip pip,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            TwoPhaseCachingInfo cachingInfo)
        {
            Contract.Requires(cachingInfo != null);

            AssertContentHashValid("PathSetHash", cachingInfo.PathSetHash);
            AssertContentHashValid("CacheEntry.MetadataHash", cachingInfo.CacheEntry.MetadataHash);

            Possible<CacheEntryPublishResult> result =
                await environment.State.Cache.TryPublishCacheEntryAsync(
                    pip.UnderlyingPip,
                    cachingInfo.WeakFingerprint,
                    cachingInfo.PathSetHash,
                    cachingInfo.StrongFingerprint,
                    cachingInfo.CacheEntry);

            if (result.Succeeded)
            {
                if (result.Result.Status == CacheEntryPublishStatus.RejectedDueToConflictingEntry)
                {
                    Logger.Log.TwoPhaseCacheEntryConflict(
                        operationContext,
                        pip.Description,
                        cachingInfo.StrongFingerprint.ToString());

                    environment.Counters.IncrementCounter(PipExecutorCounter.ProcessPipDeterminismRecoveredFromCache);
                    environment.ReportCacheDescriptorHit(result.Result.ConflictingEntry.OriginatingCache);

                    CacheEntry conflictingEntry = result.Result.ConflictingEntry;
                    return await ConvergeFromCache(operationContext, pip, environment, state, cachingInfo, process, conflictingEntry);
                }

                Contract.Assert(result.Result.Status == CacheEntryPublishStatus.Published);
                Logger.Log.TwoPhaseCacheEntryPublished(
                    operationContext,
                    pip.Description,
                    cachingInfo.WeakFingerprint.ToString(),
                    cachingInfo.PathSetHash.ToHex(),
                    cachingInfo.StrongFingerprint.ToString());
            }
            else
            {
                // NOTE: We return success even though storing the strong fingerprint did not succeed.
                Logger.Log.TwoPhasePublishingCacheEntryFailedWarning(
                    operationContext,
                    pip.Description,
                    result.Failure.DescribeIncludingInnerFailures(),
                    cachingInfo.ToString());
            }

            return StoreCacheEntryResult.Succeeded;

            void AssertContentHashValid(string description, ContentHash hash)
            {
                if (!hash.IsValid)
                {
                    Contract.Assert(false,
                        $"Invalid '{description}' content hash for pip '{pip.Description}'. " +
                        $"Hash =  {{ type: {hash.HashType}, lenght: {hash.Length}, hex: {hash.ToHex()} }}");
                }
            }
        }

        private static async Task<StoreCacheEntryResult> ConvergeFromCache(
            OperationContext operationContext,
            CacheablePip pip,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            TwoPhaseCachingInfo cachingInfo,
            Process process,
            CacheEntry conflictingEntry)
        {
            BoxRef<PipCacheMissEventData> pipCacheMiss = new PipCacheMissEventData
            {
                PipId = pip.PipId,
                CacheMissType = PipCacheMissType.Invalid,
            };

            // Converge to the conflicting entry rather than ignoring and continuing.
            var usableDescriptor = await TryConvertToRunnableFromCacheResult(
                null,
                operationContext,
                environment,
                state,
                pip,
                PublishedEntryRefLocality.Converged,
                pip.Description,
                cachingInfo.WeakFingerprint,
                cachingInfo.PathSetHash,
                cachingInfo.StrongFingerprint,
                conflictingEntry,
                null,
                pipCacheMiss);

            if (usableDescriptor == null)
            {
                // Unable to retrieve cache descriptor for strong fingerprint
                // Do nothing (just log a warning message).
                Logger.Log.ConvertToRunnableFromCacheFailed(
                    operationContext,
                    pip.Description,
                    pipCacheMiss.Value.CacheMissType.ToString());

                // Didn't converge with cache because unable to get a usable descriptor
                // But the storage of the two phase descriptor is still considered successful
                // since there is a result in the cache for the strong fingerprint
                return StoreCacheEntryResult.Succeeded;
            }

            var runnableFromCacheResult = CreateRunnableFromCacheResult(
                usableDescriptor,
                environment,
                PublishedEntryRefLocality.Converged,
                null, // Don't pass observedInputProcessingResult since this function doesn't rely on the part of the output dependent on that.
                cachingInfo.WeakFingerprint);

            ExecutionResult convergedExecutionResult = GetCacheHitExecutionResult(operationContext, environment, process, runnableFromCacheResult);

            // In success case, return deployed from cache status to indicate that we converged with remote cache and that
            // reporting to environment has already happened.
            return StoreCacheEntryResult.CreateConvergedResult(convergedExecutionResult);
        }

        private static StringKeyedHash GetStringKeyedHash(IPipExecutionEnvironment environment, PipExecutionState.PipScopeState state, AbsolutePath path, ContentHash hash)
        {
            return new StringKeyedHash
            {
                Key = state.PathExpander.ExpandPath(environment.Context.PathTable, path),
                ContentHash = hash.ToBondContentHash(),
            };
        }

        private static EncodedStringKeyedHash GetOptionalEncodedStringKeyedHash(
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            Tuple<AbsolutePath, Encoding> encodedStandardConsoleStream,
            ContentHash? maybeHash)
        {
            Contract.Requires(encodedStandardConsoleStream == null || maybeHash.HasValue);

            if (encodedStandardConsoleStream == null)
            {
                return null;
            }

            return new EncodedStringKeyedHash
            {
                StringKeyedHash = GetStringKeyedHash(environment, state, encodedStandardConsoleStream.Item1, maybeHash.Value),
                EncodingName = encodedStandardConsoleStream.Item2.WebName,
            };
        }

        /// <summary>
        /// Hashes and stores the specified output artifact from a process.
        /// </summary>
        private static async Task<Possible<TrackedFileContentInfo>> StoreProcessOutputToCacheAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            Process process,
            FileArtifact outputFileArtifact,
            bool isSymlink = false)
        {
            Contract.Requires(environment != null);
            Contract.Requires(process != null);
            Contract.Requires(outputFileArtifact.IsOutputFile);

            var possiblyStored =
                await
                    environment.LocalDiskContentStore.TryStoreAsync(
                        environment.Cache.ArtifactContentCache,
                        GetFileRealizationMode(environment, process),
                        outputFileArtifact.Path,
                        tryFlushPageCacheToFileSystem: environment.Configuration.Sandbox.FlushPageCacheToFileSystemOnStoringOutputsToCache,
                        isSymlink: isSymlink);

            if (!possiblyStored.Succeeded)
            {
                Logger.Log.StorageCachePutContentFailed(
                    operationContext,
                    outputFileArtifact.Path.ToString(environment.Context.PathTable),
                    possiblyStored.Failure.DescribeIncludingInnerFailures());
            }

            return possiblyStored;
        }

        private static async Task<Possible<TrackedFileContentInfo>> TrackPipOutputAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            FileArtifact outputFileArtifact,
            bool createHandleWithSequentialScan = false,
            bool isSymlink = false)
        {
            Contract.Requires(environment != null);
            Contract.Requires(outputFileArtifact.IsOutputFile);
            // we cannot simply track rewritten files, we have to store them into cache
            // it's fine to just track rewritten symlinks though (all data required for
            // proper symlink materialization will be a part of cache metadata)
            Contract.Requires(isSymlink || !IsRewriteOutputFile(environment, outputFileArtifact));

            var possiblyTracked = await environment.LocalDiskContentStore.TryTrackAsync(
                outputFileArtifact,
                tryFlushPageCacheToFileSystem: environment.Configuration.Sandbox.FlushPageCacheToFileSystemOnStoringOutputsToCache,
                // In tracking file, LocalDiskContentStore will call TryDiscoverAsync to compute the content hash of the file.
                // TryDiscoverAsync uses FileContentTable to avoid re-hashing the file if the hash is already in the FileContentTable.
                // Moreover, FileContentTable can enable so-called path mapping optimization that allows one to avoid opening handles and by-passing checking
                // of the USN. However, here we are tracking a produced output. Thus, the known content hash should be ignored.
                ignoreKnownContentHashOnDiscoveringContent: true,
                createHandleWithSequentialScan: createHandleWithSequentialScan);

            if (!possiblyTracked.Succeeded)
            {
                Logger.Log.StorageTrackOutputFailed(
                    operationContext,
                    outputFileArtifact.Path.ToString(environment.Context.PathTable),
                    possiblyTracked.Failure.DescribeIncludingInnerFailures());
            }

            return possiblyTracked;
        }

        private static FileRealizationMode GetFileRealizationMode(IPipExecutionEnvironment environment)
        {
            return environment.Configuration.Engine.UseHardlinks
                ? FileRealizationMode.HardLinkOrCopy // Prefers hardlinks, but will fall back to copying when creating a hard link fails. (e.g. >1023 links)
                : FileRealizationMode.Copy;
        }

        private static FileRealizationMode GetFileRealizationMode(IPipExecutionEnvironment environment, Process process)
        {
            return (environment.Configuration.Engine.UseHardlinks && !process.OutputsMustRemainWritable)
                ? FileRealizationMode.HardLinkOrCopy // Prefers hardlinks, but will fall back to copying when creating a hard link fails. (e.g. >1023 links)
                : FileRealizationMode.Copy;
        }

        /// <summary>
        /// Returns an enumerable containing all mutually exclusive counters for different cache miss reasons
        /// </summary>
        public static IEnumerable<PipExecutorCounter> GetListOfCacheMissTypes()
        {
            // All mutually exclusive counters for cache miss reasons
            return new PipExecutorCounter[]
            {
                PipExecutorCounter.CacheMissesForDescriptorsDueToWeakFingerprints,
                PipExecutorCounter.CacheMissesForDescriptorsDueToStrongFingerprints,
                PipExecutorCounter.CacheMissesForDescriptorsDueToArtificialMissOptions,
                PipExecutorCounter.CacheMissesForCacheEntry,
                PipExecutorCounter.CacheMissesDueToInvalidDescriptors,
                PipExecutorCounter.CacheMissesForProcessMetadata,
                PipExecutorCounter.CacheMissesForProcessMetadataFromHistoricMetadata,
                PipExecutorCounter.CacheMissesForProcessOutputContent,
                PipExecutorCounter.CacheMissesForProcessConfiguredUncacheable,
            };
        }
    }
}
