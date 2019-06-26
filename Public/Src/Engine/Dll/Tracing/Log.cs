// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif
using static BuildXL.Utilities.FormattableStringEx;

#pragma warning disable 1591

namespace BuildXL.Engine.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    public abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log { get { return m_log; } }

        [GeneratedEvent(
            (ushort)LogEventId.FilterDetails,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            Message = "PipFilter IsEmpty:{filterStatistics.IsEmpty}, ValuesToSelectivelyEvaluate:{filterStatistics.ValuesToSelectivelyEvaluate}. PathsToSelectivelyEvaluate:{filterStatistics.PathsToSelectivelyEvaluate}. ModulesToSelectivelyEvaluate:{filterStatistics.ModulesToSelectivelyEvaluate}. Negation: Total:{filterStatistics.NegatingFilterCount}, OutputFile:{filterStatistics.OutputFileFilterCount}, PipId:{filterStatistics.PipIdFilterCount}, Spec:{filterStatistics.SpecFileFilterCount}, Tag:{filterStatistics.TagFilterCount}, Value:{filterStatistics.ValueFilterCount}, Module:{filterStatistics.ModuleFilterCount}")]
        public abstract void FilterDetails(LoggingContext context, FilterStatistics filterStatistics);

        [GeneratedEvent(
            (ushort)LogEventId.ParsePhaseStart,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.PhasePrefix + "Parsing files",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress))]
        public abstract void ParsePhaseStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.ParsePhaseComplete,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message = EventConstants.PhasePrefix + "Done parsing {parseStatistics.FileCount} files in {parseStatistics.ElapsedMilliseconds} ms.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance | Keywords.Progress))]
        public abstract void ParsePhaseComplete(LoggingContext context, ParseStatistics parseStatistics);

        [GeneratedEvent(
            (ushort)LogEventId.StartEvaluateValues,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.PhasePrefix + "Evaluating values",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress))]
        public abstract void EvaluatePhaseStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.EndEvaluateValues,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message = EventConstants.PhasePrefix + "Done evaluating values in {evaluateStatistics.ElapsedMilliseconds}ms. {evaluateStatistics.ValueCount} total values resolved, Full evaluation:{evaluateStatistics.FullEvaluation}.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance | Keywords.Progress))]
        public abstract void EvaluatePhaseComplete(LoggingContext context, EvaluateStatistics evaluateStatistics);

        [GeneratedEvent(
            (ushort)LogEventId.StartExecute,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.PhasePrefix + "Starting execution",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress))]
        public abstract void ExecutePhaseStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.EndExecute,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message = EventConstants.PhasePrefix + "Done executing pips in {executeStatistics.ElapsedMilliseconds} ms.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance | Keywords.Progress))]
        public abstract void ExecutePhaseComplete(LoggingContext context, ExecuteStatistics executeStatistics, Scheduler.ExecutionSampler.LimitingResourcePercentages limitingResourcePercentages);

        [GeneratedEvent(
            (ushort)LogEventId.StartCheckingForPipGraphReuse,
            EventGenerators = EventGenerators.LocalOnly,
            Message = EventConstants.PhasePrefix + "Checking for pip graph reuse",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress))]
        public abstract void CheckingForPipGraphReuseStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.InputTrackerHasMismatchedGraphFingerprint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage),
            Message = "Input tracker has mismatched fingerprint: Exact fingerprint: {exactFingerprintReason} | Compatible fingerprint: {compatibleFingerprintReason}")]
        public abstract void InputTrackerHasMismatchedGraphFingerprint(LoggingContext context, string exactFingerprintReason, string compatibleFingerprintReason);

        [GeneratedEvent(
           (ushort)LogEventId.InputTrackerHasUnaccountedDirectoryEnumeration,
           EventGenerators = EventGenerators.LocalOnly,
           EventLevel = Level.Verbose,
           EventTask = (ushort)Tasks.Engine,
           EventOpcode = (byte)EventOpcode.Start,
           Keywords = (int)(Keywords.UserMessage),
           Message = "Input tracker has an unaccounted directory enumeration")]
        public abstract void InputTrackerHasUnaccountedDirectoryEnumeration(LoggingContext context);

        [GeneratedEvent(
           (ushort)LogEventId.InputTrackerDetectedEnvironmentVariableChanged,
           EventGenerators = EventGenerators.LocalOnly,
           EventLevel = Level.Verbose,
           EventTask = (ushort)Tasks.Engine,
           EventOpcode = (byte)EventOpcode.Start,
           Keywords = (int)(Keywords.UserMessage),
           Message = "Input tracker detected first changed environment variable: Variable name: {variableName} | Recorded value: {recordedValue} | Current value: {currentValue}")]
        public abstract void InputTrackerDetectedEnvironmentVariableChanged(LoggingContext context, string variableName, string recordedValue, string currentValue);

        [GeneratedEvent(
           (ushort)LogEventId.InputTrackerDetectedMountChanged,
           EventGenerators = EventGenerators.LocalOnly,
           EventLevel = Level.Verbose,
           EventTask = (ushort)Tasks.Engine,
           EventOpcode = (byte)EventOpcode.Start,
           Keywords = (int)(Keywords.UserMessage),
           Message = "Input tracker detected first changed mount: Mount name: {mountName} | Recorded path: {recordedPath} | Current path: {currentPath}")]
        public abstract void InputTrackerDetectedMountChanged(LoggingContext context, string mountName, string recordedPath, string currentPath);

        [GeneratedEvent(
            (ushort)LogEventId.InputTrackerUnableToDetectChangedInputFileByCheckingContentHash,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage),
            Message = "Input tracker is unable to detect changed input file: File: {file} | Recorded content hash: {recordedHash} | Reason: {reason}")]
        public abstract void InputTrackerUnableToDetectChangedInputFileByCheckingContentHash(LoggingContext context, string file, string recordedHash, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.InputTrackerDetectedChangedInputFileByCheckingContentHash,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage),
            Message = "Input tracker detected changed input file: File: {file} | Actual content hash: {actualHash} | Recorded content hash: {recordedHash}")]
        public abstract void InputTrackerDetectedChangedInputFileByCheckingContentHash(LoggingContext context, string file, string actualHash, string recordedHash);

        [GeneratedEvent(
            (ushort)LogEventId.InputTrackerDetectedChangeInEnumeratedDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage),
            Message = "Input tracker detected first changed in enumerated directory: Directory: {directory} | Recorded membership fingerprint: {recordedFingerprint} | Current membership fingerprint: {currentFingerprint}")]
        public abstract void InputTrackerDetectedChangeInEnumeratedDirectory(LoggingContext context, string directory, string recordedFingerprint, string currentFingerprint);

        [GeneratedEvent(
            (ushort)LogEventId.InputTrackerUnableToDetectChangeInEnumeratedDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage),
            Message = "Input tracker is unable to detect changed in enumerated directory: Directory: {directory} | Reason: {reason}")]
        public abstract void InputTrackerUnableToDetectChangeInEnumeratedDirectory(LoggingContext context, string directory, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.StartVisitingSpecFiles,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress),
            Message = EventConstants.PhasePrefix + "Visiting spec files for graph reuse check")]
        public abstract void VisitingSpecFilesStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.EndVisitingSpecFiles,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance | Keywords.Progress),
            Message = EventConstants.PhasePrefix + "Done visiting {fileCount} spec files for graph reuse check in {elapsedMilliseconds}ms")]
        public abstract void VisitingSpecFilesComplete(LoggingContext context, int elapsedMilliseconds, int fileCount);

        [GeneratedEvent(
            (ushort)LogEventId.JournalDetectedNoInputChanges,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress),
            Message = EventConstants.PhasePrefix + "USN journal: No spec files are changed so graph in the engine cache is reusable")]
        public abstract void JournalDetectedNoInputChanges(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.JournalDetectedInputChanges,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress),
            Message = EventConstants.PhasePrefix + "USN journal: At least one spec file and/or one directory membership have changed: Spec file: {specFile} | Directory: {directory}")]
        public abstract void JournalDetectedInputChanges(LoggingContext context, string specFile, string directory);

        [GeneratedEvent(
            (ushort)LogEventId.JournalProcessingStatisticsForGraphReuseCheck,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress),
            Message = EventConstants.PhasePrefix + "USN journal statistics for graph reuse check: {message}")]
        public abstract void JournalProcessingStatisticsForGraphReuseCheck(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.GraphInputArtifactChangesTokensMismatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)Keywords.UserMessage,
            Message = "Input tracker cannot identify input artifact changes using USN journal due to mismatched tokens with file change tracker: Expected token: {expectedToken} | Actual token: {actualToken}")]
        public abstract void GraphInputArtifactChangesTokensMismatch(LoggingContext context, string expectedToken, string actualToken);

        [GeneratedEvent(
            (ushort)LogEventId.JournalProcessingStatisticsForGraphReuseCheckTelemetry,
            EventGenerators = EventGenerators.TelemetryOnly | Generators.Statistics,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress),
            Message = "USN journal statistics for graph reuse check")]
        public abstract void JournalProcessingStatisticsForGraphReuseCheckTelemetry(LoggingContext context, string scanningJournalStatus, IDictionary<string, long> stats);

        [GeneratedEvent(
            (ushort)LogEventId.EndCheckingForPipGraphReuse,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message = EventConstants.PhasePrefix + "Done checking for pip graph reuse in {graphCacheCheckStatistics.ElapsedMilliseconds}ms. ObjectDirectoryMissReason:{graphCacheCheckStatistics.ObjectDirectoryMissReasonAsString}, CacheMissReason:{graphCacheCheckStatistics.CacheMissReasonAsString} ",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance | Keywords.Progress))]
        public abstract void CheckingForPipGraphReuseComplete(LoggingContext context, GraphCacheCheckStatistics graphCacheCheckStatistics);

        [GeneratedEvent(
            (ushort)LogEventId.CheckingForPipGraphReuseStatus,
            EventGenerators = EventGenerators.LocalOnly,
            Message = EventConstants.PhasePrefix + "Checking input files for pip graph reuse. {done} done. {remaining} remaining.",
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance | Keywords.Progress | Keywords.Overwritable))]
        public abstract void CheckingForPipGraphReuseStatus(LoggingContext context, int done, int remaining);

        [GeneratedEvent(
            (ushort)LogEventId.StartDeserializingPipGraph,
            EventGenerators = EventGenerators.LocalOnly,
            Message = EventConstants.PhasePrefix + "Reloading pip graph from previous build.",
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable))]
        public abstract void ReloadingPipGraphStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.StartDeserializingEngineState,
            EventGenerators = EventGenerators.LocalOnly,
            Message = EventConstants.PhasePrefix + "Reloading engine state from previous build.",
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable))]
        public abstract void PartiallyReloadingEngineState(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.EndDeserializingEngineState,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message = EventConstants.PhasePrefix + "Done reloading engine state from previous build in {graphCacheReloadStatistics.ElapsedMilliseconds} ms.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance | Keywords.Progress))]
        public abstract void PartiallyReloadingEngineStateComplete(LoggingContext context, GraphCacheReloadStatistics graphCacheReloadStatistics);

        [GeneratedEvent(
            (ushort)LogEventId.EngineContextHeuristicOutcomeReuse,
            EventGenerators = EventGenerators.LocalOnly,
            Message = EventConstants.PhasePrefix + "Decided to reuse engine context gen-{generation}. Reason: {reason}",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance | Keywords.Progress))]
        public abstract void EngineContextHeuristicOutcomeReuse(LoggingContext context, int generation, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.EngineContextHeuristicOutcomeSkip,
            EventGenerators = EventGenerators.LocalOnly,
            Message = EventConstants.PhasePrefix + "Decided NOT to reuse engine context gen-{generation}. Reason: {reason}",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance | Keywords.Progress))]
        public abstract void EngineContextHeuristicOutcomeSkip(LoggingContext context, int generation, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.StartSerializingPipGraph,
            EventGenerators = EventGenerators.LocalOnly,
            Message = EventConstants.PhasePrefix + "Serializing pip graph for future reuse",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress))]
        public abstract void SerializingPipGraphStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.EndSerializingPipGraph,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message = EventConstants.PhasePrefix + "Done serializing pip graph for future reuse in {graphCacheSaveStatistics.ElapsedMilliseconds}ms. Serialization time {graphCacheSaveStatistics.SerializationMilliseconds}ms. Is success: {graphCacheSaveStatistics.Success}",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance | Keywords.Progress))]
        public abstract void SerializingPipGraphComplete(LoggingContext context, GraphCacheSaveStatistics graphCacheSaveStatistics);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToDuplicateGraphFile,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Failed to create a duplicate of {file} in {directory}. The execution log may not be usable. Error: {message}",
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Engine)]
        public abstract void FailedToDuplicateGraphFile(LoggingContext context, string file, string directory, string message);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToDuplicateOptionalGraphFile,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Failed to create a duplicate of optional {file} in {directory}. The execution log may not be usable. Error: {message}",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine)]
        public abstract void FailedToDuplicateOptionalGraphFile(LoggingContext context, string file, string directory, string message);

        [GeneratedEvent(
            (ushort)LogEventId.FallingBackOnGraphFileCopy,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Falling back on physically copying graph file '{source}' to '{target}' took '{copyTimeInMs}ms. This will have a detrimental impact on performance compared to hardlinking.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine)]
        public abstract void FallingBackOnGraphFileCopy(LoggingContext context, string source, string target, long copyTimeInMs);

        [GeneratedEvent(
            (ushort)LogEventId.FailedLoadIncrementalSchedulingState,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Failed to load incremental scheduling state: {reason}",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine)]
        public abstract void FailedLoadIncrementalSchedulingState(LoggingContext context, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.PipGraphIdentfier,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Pip Graph Fingerprint: '{identifierFingerprint}'",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void PipGraphIdentfier(LoggingContext context, string identifierFingerprint);

        [GeneratedEvent(
            (ushort)LogEventId.PipGraphByPathFailure,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Graph specified at path '{cachedGraphPath}' could not be loaded. If graph ID was intended, ensure it is specified as a 40 digit hex string (no delimiters).\nExample: ad2d42d2ec5d2ca0c0b7ad65402d07c7ef40b91e",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void PipGraphByPathFailure(LoggingContext context, string cachedGraphPath);

        [GeneratedEvent(
            (ushort)LogEventId.PipGraphByIdFailure,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Graph specified with ID '{identifierFingerprint}' could not be loaded.",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void PipGraphByIdFailure(LoggingContext context, string identifierFingerprint);

        [GeneratedEvent(
            (ushort)LogEventId.CacheInitialized,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Cache initialized with ID {cacheId}",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CacheInteraction,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.Diagnostics | Keywords.UserMessage))]
        public abstract void CacheInitialized(LoggingContext context, string cacheId);

        [GeneratedEvent(
            (ushort)LogEventId.CacheRecoverableError,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Cache reported recoverable error {error}",
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.CacheInteraction,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.Diagnostics | Keywords.UserMessage))]
        public abstract void CacheReportedRecoverableError(LoggingContext context, string error);

        #region Distribution

        [GeneratedEvent(
            (ushort)LogEventId.DistributionHostLog,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Proxy for service at {ipAddress}:{port} logged message: [{severity}] {message}")]
        public abstract void DistributionHostLog(LoggingContext context, string ipAddress, int port, string severity, string message);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionConnectedToWorker,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Connected to worker {ipAddress}:{port}")]
        public abstract void DistributionConnectedToWorker(LoggingContext context, string ipAddress, int port);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionWorkerChangedState,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Worker {ipAddress}:{port} changed status from {fromState} to {toState} by {caller}")]
        public abstract void DistributionWorkerChangedState(
            LoggingContext context,
            string ipAddress,
            int port,
            string fromState,
            string toState,
            string caller);

        public void DistributionSendBondCallFormat(
            LoggingContext context,
            RpcMachineData receiverData,
            string function,
            Guid callId,
            string formatMessage,
            params object[] messageArgs)
        {
            DistributionBondCall(
                context: context,
                receiverName: receiverData.ToString(),
                senderName: "SELF",
                function: function,
                callId: callId.ToString(),
                message: string.Format(CultureInfo.InvariantCulture, formatMessage, messageArgs));
        }

        public void DistributionReceiveBondCallFormat(
            LoggingContext context,
            RpcMachineData senderData,
            string function,
            Guid callId,
            string formatMessage,
            params object[] messageArgs)
        {
            DistributionBondCall(
                context: context,
                receiverName: "SELF",
                senderName: senderData.ToString(),
                function: function,
                callId: callId.ToString(),
                message: string.Format(CultureInfo.InvariantCulture, formatMessage, messageArgs));
        }

        [GeneratedEvent(
            (ushort)LogEventId.DistributionBondCall,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "[{senderName} -> {receiverName}] BONDCALL:{function}#{callId}: {message}.")]
        public abstract void DistributionBondCall(
            LoggingContext context,
            string receiverName,
            string senderName,
            string function,
            string callId,
            string message);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionFailedToCallWorker,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Failed call to worker {name}: Function='{function}' Failure='{errorMessage}'")]
        public abstract void DistributionFailedToCallWorker(
            LoggingContext context,
            string name,
            string function,
            string errorMessage);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionCallWorkerCodeException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Call to worker {name} raised exception: Function='{function}' Failure='{errorMessage}'")]
        public abstract void DistributionCallWorkerCodeException(
            LoggingContext context,
            string name,
            string function,
            string errorMessage);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionCallMasterCodeException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Call to master raised exception: Function='{function}' Failure='{errorMessage}'")]
        public abstract void DistributionCallMasterCodeException(
            LoggingContext context,
            string function,
            string errorMessage);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionFailedToStoreValidationContentToWorkerCacheWithException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            // This error is not forwarded to the master because doing so would cause the master to log an error when
            // no pip fails in the build from its perspective. The worker must still log this as an error because it
            // did fail.
            Keywords = (int)(Keywords.UserMessage | Keywords.NotForwardedToMaster),
            EventTask = (ushort)Tasks.Distribution,
            Message = "Could not load store content with hash '{contentHash}'."
            + " This may indicate that worker cache is incorrectly configured or is not properly shared.\n"
            + "Failure:\n{exceptionMessage}")]
        public abstract void DistributionFailedToStoreValidationContentToWorkerCacheWithException(
            LoggingContext context,
            string contentHash,
            string exceptionMessage);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionFailedToRetrieveValidationContentFromWorkerCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Worker {workerName}: Could not load content with hash '{contentHash}' to validate connection to worker cache. This may indicate that worker cache is incorrectly configured or is not properly shared.")]
        public abstract void DistributionFailedToRetrieveValidationContentFromWorkerCache(
            LoggingContext context,
            string workerName,
            string contentHash);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionFailedToRetrieveValidationContentFromWorkerCacheWithException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Worker {workerName}: Could not load content with hash '{contentHash}' to validate connection to worker cache."
            + " This may indicate that worker cache is incorrectly configured or is not properly shared.\n"
            + "Exception:\n{exceptionMessage}")]
        public abstract void DistributionFailedToRetrieveValidationContentFromWorkerCacheWithException(
            LoggingContext context,
            string workerName,
            string contentHash,
            string exceptionMessage);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionSuccessfulRetryCallToWorker,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Retried call to worker {name} succeeded: Function='{function}'")]
        public abstract void DistributionSuccessfulRetryCallToWorker(LoggingContext context, string name, string function);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionWorkerAttachTooSlow,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Attach completion notification from worker {name} is taking longer than expected ({time}). Ensure worker can connect to master.")]
        public abstract void DistributionWorkerAttachTooSlow(LoggingContext context, string name, string time);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionFailedToCallMaster,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Failed call to master: Function='{function}' Failure='{errorMessage}'")]
        public abstract void DistributionFailedToCallMaster(LoggingContext context, string function, string errorMessage);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionSuccessfulRetryCallToMaster,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Retried call to master succeeded: Function='{function}'")]
        public abstract void DistributionSuccessfulRetryCallToMaster(LoggingContext context, string function);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionInactiveMaster,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            // Treat this as a user error instead of an internal error. The master may very well still succeed when this happens.
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Distribution,
            Message = "There were no calls from master in the last {timeinMin} minutes. Assuming it is dead and exiting.")]
        public abstract void DistributionInactiveMaster(LoggingContext context, int timeinMin);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionWaitingForMasterAttached,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress),
            EventTask = (ushort)Tasks.Distribution,
            Message = "Waiting for master to attach")]
        public abstract void DistributionWaitingForMasterAttached(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionDisableServiceProxyInactive,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "The service proxy for service at '{name}::{port}' has been disabled due to exceeded allowed period of failed calls ({failureTime})")]
        public abstract void DistributionDisableServiceProxyInactive(LoggingContext context, string name, int port, string failureTime);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionStatistics,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Distribution-related times:\r\n{statistics}")]
        public abstract void DistributionStatistics(LoggingContext context, string statistics);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionExecutePipFailedNetworkFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage | (int)Keywords.InfrastructureError,
            EventTask = (ushort)Tasks.PipExecutor,
            Message = "[{pipDescription}] Failing pip step {step} because execution request could not be sent to worker {workerName}: {errorMessage}")]
        public abstract void DistributionExecutePipFailedNetworkFailure(LoggingContext context, string pipDescription, string workerName, string errorMessage, string step);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionExecutePipFailedNetworkFailureWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage | (int)Keywords.InfrastructureError,
            EventTask = (ushort)Tasks.PipExecutor,
            Message = "[{pipDescription}] Failing pip step {step} because execution request could not be sent to worker {workerName}: {errorMessage}")]
        public abstract void DistributionExecutePipFailedNetworkFailureWarning(LoggingContext context, string pipDescription, string workerName, string errorMessage, string step);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionAttachReceived,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress),
            EventTask = (ushort)Tasks.Distribution,
            Message = "Received attach request from the master. New session identifier: {sessionId}. Master Name: {masterName}.")]
        public abstract void DistributionAttachReceived(LoggingContext context, string sessionId, string masterName);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionExitReceived,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Received exit request from the master. Shutting down...")]
        public abstract void DistributionExitReceived(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionWorkerExitFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            // This isn't strictly a UserError, but we treat it as once since it's an error for
            // sake of forcing the worker's session to be an error. We don't want to mark it as an internal
            // or infrastructure error because that would cause the overall session to be infra/internal which
            // might not be the case. If the session already logged some other infra/internal error, that status
            // will still be preserved since they trump UserError.
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Distribution,
            Message = "Failing build on worker due to failure: {failure}")]
        public abstract void DistributionWorkerExitFailure(LoggingContext context, string failure);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionTryMaterializeInputsFailedRetry,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{pipDescription}] Failed to materialize inputs for pip. Number of remaining retries: {remainingRetryCount}.",
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Distribution,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void DistributionTryMaterializeInputsFailedRetry(LoggingContext context, long pipSemiStableHash, string pipDescription, int remainingRetryCount);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionTryMaterializeInputsSuccessfulRetry,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{pipDescription}] Successfully materialize inputs for pip on retry. Number of failed attempts: {numberOfFailedAttempts}.",
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Distribution,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void DistributionTryMaterializeInputsSuccessfulRetry(LoggingContext context, long pipSemiStableHash, string pipDescription, int numberOfFailedAttempts);

        [GeneratedEvent(
            (ushort)EventId.ErrorUnableToCacheGraphDistributedBuild,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Graph could not be cached. Distributed builds require graphs to be cached in order to send graph to workers.",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ErrorUnableToCacheGraphDistributedBuild(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionServiceInitializationError,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Distribution {role} service could not be initialized on port {port}. Verify that port is available.\nException:\n{exceptionMessage}",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.InfrastructureError))]
        public abstract void DistributionServiceInitializationError(LoggingContext context, string role, ushort port, string exceptionMessage);

        [GeneratedEvent(
            (ushort)EventId.ErrorCacheDisabledDistributedBuild,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Cache is not enabled. Distributed builds require caching to be enabled.",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ErrorCacheDisabledDistributedBuild(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.DistributionWorkerForwardedError,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Worker {workerForwardedEvent.WorkerName} logged error:\n{workerForwardedEvent.Text}",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Distribution,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void DistributionWorkerForwardedError(LoggingContext context, WorkerForwardedEvent workerForwardedEvent);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionWorkerForwardedWarning,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Worker {workerForwardedEvent.WorkerName} logged warning:\n{workerForwardedEvent.Text}",
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Distribution,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void DistributionWorkerForwardedWarning(LoggingContext context, WorkerForwardedEvent workerForwardedEvent);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionWorkerExecutePipRequest,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{pipDescription}] Executing distributed pip build request of step {step}",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Distribution,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void DistributionWorkerExecutePipRequest(LoggingContext context, long pipSemiStableHash, string pipDescription, string step);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionPipFailedOnWorker,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{pipDescription}] Failed pip execution of step {step} on worker {workerName}.",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Distribution,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void DistributionPipFailedOnWorker(LoggingContext context, long pipSemiStableHash, string pipDescription, string step, string workerName);

        [GeneratedEvent(
            (ushort)LogEventId.GrpcTrace,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Grpc: {message}.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Distribution,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void GrpcTrace(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionWorkerFinishedPipRequest,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[Pip{pipSemiStableHash:X16}] Finished distributed pip build request of step {step}.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Distribution,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void DistributionWorkerFinishedPipRequest(LoggingContext context, long pipSemiStableHash, string step);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionWorkerCouldNotLoadGraph,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Distributed build worker failed because it could not load a graph.",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Distribution,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void DistributionWorkerCouldNotLoadGraph(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionWorkerPipOutputContent,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{pipDescription}] Pip output '{filePath}' with hash '{hash}' reported to master.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Distribution,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void DistributionWorkerPipOutputContent(LoggingContext context, long pipSemiStableHash, string pipDescription, string filePath, string hash);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionWorkerStatus,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress),
            EventTask = (ushort)Tasks.Distribution,
            Message = "{pipsReported} reported, {pipsReporting} reporting, {pipsRecording} recording, {pipsPrepped} prepped, {pipsPrepping} prepping, {pipsQueued} queued")]
        internal abstract void DistributionWorkerStatus(
            LoggingContext loggingContext,
            int pipsQueued,
            int pipsPrepping,
            int pipsPrepped,
            int pipsRecording,
            int pipsReporting,
            int pipsReported);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionMasterStatus,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress),
            EventTask = (ushort)Tasks.Distribution,
            Message = "{pipsCompleted} completed, {pipsReported} reported, {pipsSent} sent, {pipsLocal} local, {pipsSending} sending,\n\t" +
            "{pipsLocatingInputs} locating inputs, {pipComputingContent} computing content,  {pipsAssigned} assigned, {pipsUnassigned} unassigned")]
        internal abstract void DistributionMasterStatus(
            LoggingContext loggingContext,
            int pipsUnassigned,
            int pipsAssigned,
            int pipsLocatingInputs,
            int pipComputingContent,
            int pipsSending,
            int pipsSent,
            int pipsLocal,
            int pipsReported,
            int pipsCompleted);

        [GeneratedEvent(
            (int)LogEventId.DistributionDebugMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "DDB_DEBUG: {message}")]
        internal abstract void DistributionDebugMessage(LoggingContext loggingContext, string message);

        #endregion

        [GeneratedEvent(
            (ushort)EventId.NonDeterministicPipOutput,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{nonDeterministicPipOutputData.PipDescription}] produced nondeterministic output '{nonDeterministicPipOutputData.OutputPath}' with hash '{nonDeterministicPipOutputData.OutputHash1}' on worker '{nonDeterministicPipOutputData.WorkerDescription1}' and hash '{nonDeterministicPipOutputData.OutputHash2}' on worker '{nonDeterministicPipOutputData.WorkerDescription2}'",
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void NonDeterministicPipOutput(LoggingContext context, NonDeterministicPipOutputData nonDeterministicPipOutputData);

        [GeneratedEvent(
            (ushort)EventId.NonDeterministicPipResult,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{nonDeterministicPipResultData.PipDescription}] has nondeterministic execution with result '{nonDeterministicPipResultData.Result1}' on worker '{nonDeterministicPipResultData.WorkerDescription1}' and result '{nonDeterministicPipResultData.Result2}' on worker '{nonDeterministicPipResultData.WorkerDescription2}'",
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void NonDeterministicPipResult(LoggingContext context, NonDeterministicPipResultData nonDeterministicPipResultData);

        [GeneratedEvent(
            (ushort)EventId.EnvironmentVariablesImpactingBuild,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Environment variables impacting build (Count = {effectiveEnvironmentVariables.Count})\nUsed (Count = {effectiveEnvironmentVariables.UsedCount}):\n{effectiveEnvironmentVariables.UsedVariables}\n\nUnused (Count = {effectiveEnvironmentVariables.UnusedCount}):\n{effectiveEnvironmentVariables.UnusedVariables}",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void EnvironmentVariablesImpactingBuild(LoggingContext context, EffectiveEnvironmentVariables effectiveEnvironmentVariables);

        [GeneratedEvent(
            (ushort)EventId.MountsImpactingBuild,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Mounts impacting build (Count = {effectiveMounts.Count}):\n{effectiveMounts.UsedMountsText}",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void MountsImpactingBuild(LoggingContext context, EffectiveMounts effectiveMounts);

        [GeneratedEvent(
            (ushort)EventId.PerformanceSample,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Perf: CPU:{machineProcessorTime}%, FreeMemory:{machineAvailableMemoryMegabytes}MB. Ready:{ready}, Running:{running}, Max:{maxRunning}, Limiting factor heuristic: {limitingResource}",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)Keywords.Performance)]
        public abstract void PerformanceSample(LoggingContext context, int machineProcessorTime, int machineAvailableMemoryMegabytes, long ready, long running, int maxRunning, string limitingResource);

        [GeneratedEvent(
            (ushort)EventId.WhitelistFileAccess,

            // This is no longer sent to telemetry because it is a relatively large amount of data and was never used.
            // Add | EventGenerators.TelemetryOnly to resume sending it to telemetry
            EventGenerators = Generators.Statistics,
            Message = "Whitelist usage")]
        public abstract void WhitelistFileAccess(LoggingContext context, IDictionary<string, int> entryMatches);

        [GeneratedEvent(
            (ushort)EventId.ConfigExportGraphRequiresScheduling,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "In order to export a build graph (/exportGraph), the schedule phase must be completed. Specify /phase:Schedule or /phase:Execute, or remove the /phase option altogether.")]
        public abstract void ConfigExportGraphRequiresScheduling(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigUnsafeIgnoringChangeJournal,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Usage of the NTFS / ReFS change journal has been disabled. This is an unsafe (diagnostic only) configuration since it limits incremental build performance and correctness. Consider removing the '/unsafe_UseUsnJournal-' switch.")]
        public abstract void ConfigUnsafeIgnoringChangeJournal(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.CacheIsStillBeingInitialized,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Keywords.UserMessage | Keywords.Overwritable),
            EventTask = (int)Tasks.Engine,
            Message = "Cache is still being initialized..")]
        public abstract void CacheIsStillBeingInitialized(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigUnsafeDisabledFileAccessMonitoring,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_MonitorFileAccesses disabled: File access monitoring has been disabled. This is an unsafe (diagnostic only) configuration since it removes all guarantees of build correctness.")]
        public abstract void ConfigUnsafeDisabledFileAccessMonitoring(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigIgnoreZwRenameFileInformation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_IgnoreZwRenameFileInformation enabled: {ShortProductName} is configured not to detour the ZwRenameInformation API for ZwSetFileInformation. This might lead to incorrect builds because some file accesses will not be enforced.")]
        public abstract void ConfigIgnoreZwRenameFileInformation(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigIgnoreZwOtherFileInformation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_IgnoreZwOtherFileInformation: {ShortProductName} is configured not to detour the ZwLinkInformation, ZwFileNameInformation, ZwDispositionInformation, ZwModeInformation APIs for SetFileInformation. This might lead to incorrect builds because some file accesses will not be enforced.")]
        public abstract void ConfigIgnoreZwOtherFileInformation(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigIgnoreValidateExistingFileAccessesForOutputs,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_IgnoreValidateExistingFileAccessesForOutputs: {ShortProductName} is configured not to validate file access messages for outputs produced. This might lead to incorrect builds and cathastrophic cache corruption because some PathSets will not have all the paths accessed.")]
        public abstract void ConfigIgnoreValidateExistingFileAccessesForOutputs(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigIgnoreNonCreateFileReparsePoints,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_IgnoreNonCreateFileReparsePoints enabled: {ShortProductName} is configured not to follow symlinks for other than CreateFile and NtCreate/OpenFile APIs. This might lead to incorrect builds because some file accesses will not be enforced.")]
        public abstract void ConfigIgnoreNonCreateFileReparsePoints(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigIgnoreSetFileInformationByHandle,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_IgnoreSetFileInformationByHandle enabled: {ShortProductName} is configured not to detour the SetFileInformationByHandle API. This might lead to incorrect builds because some file accesses will not be enforced.")]
        public abstract void ConfigIgnoreSetFileInformationByHandle(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigIgnoreReparsePoints,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_IgnoreReparsePoints enabled: {ShortProductName} is configured not to track reparse points. This might lead to incorrect builds because some file accesses will not be enforced.")]
        public abstract void ConfigIgnoreReparsePoints(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigIgnorePreloadedDlls,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_IgnorePreloadedDlls enabled: {ShortProductName} is configured not to track preloaded Dlls. This might lead to incorrect builds because some file accesses will not be enforced.")]
        public abstract void ConfigIgnorePreloadedDlls(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigDisableDetours,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_DisableDetours enabled: {ShortProductName} is configured not to detour any file access APIs. This might lead to incorrect builds because any file accesses will not be enforced.")]
        public abstract void ConfigDisableDetours(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigIgnoreGetFinalPathNameByHandle,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_IgnoreGetFinalPathNameByHandle enabled: {ShortProductName} is configured to use Subst without handling the GetFinalPathNameByHandle API. You may encounter errors.")]
        public abstract void ConfigIgnoreGetFinalPathNameByHandle(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigPreserveOutputs,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "unsafe_PreserveOutputs enabled: {ShortProductName} is configured to preserve the state of output files before running a process. This may lead to incorrect builds because a process' behavior may change if outputs are not deleted before running the process.")]
        public abstract void ConfigPreserveOutputs(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigUnsafeUnexpectedFileAccessesAsWarnings,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_UnexpectedFileAccessesAreErrors disabled: Invalid file accesses by tools are configured to trigger warnings instead of errors. This is an unsafe (diagnostic only) configuration since it removes all guarantees of build correctness.")]
        public abstract void ConfigUnsafeUnexpectedFileAccessesAsWarnings(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigUnsafeIgnoreUndeclaredAccessesUnderSharedOpaques,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_IgnoreUndeclaredAccessesUnderSharedOpaques enabled: Undeclared accesses under shared opaques will not be reported. This is an unsafe configuration since it removes all guarantees of build correctness.")]
        public abstract void ConfigUnsafeIgnoreUndeclaredAccessesUnderSharedOpaques(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigUnsafeLazySymlinkCreation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_LazySymlinkCreation enabled: {ShortProductName} is configured to create symlinks from symlink definition manifest lazily. This might lead to incorrect builds because symlinks may have not been created when a pip consumes (read or probe) it or enumerates its parent.")]
        public abstract void ConfigUnsafeLazySymlinkCreation(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigDebuggingAndProfilingCannotBeSpecifiedSimultaneously,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "Profiling and debugging cannot be specified simultaneously.")]
        public abstract void ConfigDebuggingAndProfilingCannotBeSpecifiedSimultaneously(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigUnsafeMonitorNtCreateFileOff,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_IgnoreNtCreateFile enabled: The monitoring of NTs CreateFile is turned off. This might lead to incorrect builds because some file accesses will not be enforced.")]
        public abstract void ConfigUnsafeMonitorNtCreateFileOff(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigUnsafeAllowMissingOutput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_AllowMissingOutput enabled: Errors for specified-but-not-produced output files have been suppressed. This is an unsafe (diagnostic only) configuration.")]
        public abstract void ConfigUnsafeAllowMissingOutput(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigUnsafeDisableCycleDetection,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_DisableCycleDetection enabled: Cycle detection during evaluation is disabled.")]
        public abstract void ConfigUnsafeDisableCycleDetection(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigUnsafeExistingDirectoryProbesAsEnumerations,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_ExistingDirectoryProbesasEnumerations enabled: {ShortProductName} is reporting existing directory probes as enumerations. This might lead to cases where pips will be executed even when there is no need for it.")]
        public abstract void ConfigUnsafeExistingDirectoryProbesAsEnumerations(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigUnsafeMonitorZwCreateOpenQueryFileOff,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "The monitoring of ZwOpenFile, ZwCreateFile, and ZwQueryDirectoryFile is turned off. This might lead to incorrect builds because some file accesses will not be enforced.")]
        public abstract void ConfigUnsafeMonitorZwCreateOpenQueryFileOff(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigArtificialCacheMissOptions,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Artificial cache misses will be injected at a rate of {missRate:P3}. To reproduce these same misses, specify /injectCacheMisses:{currentParameters} ; to instead inject misses for all other pips, specify /injectCacheMisses:{invertedParameters}")]
        public abstract void ConfigArtificialCacheMissOptions(LoggingContext context, double missRate, string currentParameters, string invertedParameters);

        [GeneratedEvent(
            (int)EventId.AssignProcessToJobObjectFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "AssignProcessToJobObject failed: Native Win32 Error: {0}")]
        public abstract void AssignProcessToJobObjectFailed(LoggingContext context, string nativeError);

        [GeneratedEvent(
            (ushort)EventId.CannotHonorLowPriority,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Unable to honor the /lowPriority option. This requires Win8/Server2012 or newer.")]
        public abstract void CannotEnforceLowPriority(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ConfigUsingExperimentalOptions,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "One or more experimental options have been enabled. These options may one day be supported, but may currently be unstable. Please report any issues to the {ShortProductName} team. Options enabled: {optionsEnabled}")]
        public abstract void ConfigUsingExperimentalOptions(LoggingContext context, string optionsEnabled);

        [GeneratedEvent(
            (ushort)EventId.ConfigIncompatibleIncrementalSchedulingDisabled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Option '{option}' is incompatible with incremental scheduling. Incremental scheduling has been disabled.")]
        public abstract void ConfigIncompatibleIncrementalSchedulingDisabled(LoggingContext context, string option);

        [GeneratedEvent(
            (ushort)EventId.ConfigIncompatibleOptionWithDistributedBuildError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Option {option} is incompatible with distributed builds.")]
        public abstract void ConfigIncompatibleOptionWithDistributedBuildError(LoggingContext context, string option);

        [GeneratedEvent(
            (ushort)EventId.ConfigIncompatibleOptionWithDistributedBuildWarn,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Option '{option}' with value '{currentValue}' is incompatible with distributed builds. The option has been set to '{newValue}'.")]
        public abstract void ConfigIncompatibleOptionWithDistributedBuildWarn(LoggingContext context, string option, string currentValue, string newValue);

        [GeneratedEvent(
            (ushort)EventId.WarnToNotUsePackagesButModules,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Deprecated configuration. Config file '{configFile}' declares a set of modules via the 'packages' field. This field will be deprecated soon. Please start using the field 'modules'. This field has identical semantics. If you can't update and need this feature after July 2018 please reach out to the {ShortProductName} team.")]
        public abstract void WarnToNotUsePackagesButModules(LoggingContext context, string configFile);


        [GeneratedEvent(
            (ushort)EventId.WarnToNotUseProjectsField,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = @"Deprecated configuration. Config file '{configFile}' declares a set of orphaned projects via the 'projects' field. This field will be deprecated soon. Please create a proper {ShortScriptName} module file and update the configuration with the following steps:

1. Create a new file  '{moduleFileName}'
2. Place the following contents in the file:
    module({{
        name: '{suggestedModuleName}',
        nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
        projects: [
            // Place the list of projects from your config file here.
        ]
    }});
3. Remove the top-level 'projects' field from '{configFile}'.
4. Add a top-level field called 'modules' and point to the module file:
    config({{
        modules: [
            f`{moduleFileName}`,
        ]
    }});

Note: the old project files were possibly in a {ShortScriptName} Legacy file format. We recommend updating to the latest syntax. If you need assistance with that feel free to reach out to the BuildXL team and we can help.

If you can't update and need this feature after July 2018 please reach out to the BuildXL team.
")]
        public abstract void WarnToNotUseProjectsField(LoggingContext context, string configFile, string moduleFileName, string suggestedModuleName);

        [GeneratedEvent(
            (ushort)EventId.SpecCacheDisabledForNoSeekPenalty,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Parser,
            Message = "Detected enlistment drive does not have a seek penalty. Spec file caching will be disabled.")]
        public abstract void SpecCacheDisabledForNoSeekPenalty(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.EngineCachePrefersLoadingInMemoryForSeekPenalty,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Detected engine cache drive does have a seek penalty. Engine Cache data will preferably be loaded into memory.")]
        public abstract void EngineCachePrefersLoadingInMemoryForSeekPenalty(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.EngineErrorSavingFileContentTable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "The file content table at '{file}' could not be updated. This will affect performance for the next session, since files may be re-hashed or replaced. Failure message: {exceptionDetails}")]
        public abstract void EngineErrorSavingFileContentTable(LoggingContext context, string file, string exceptionDetails);

        [GeneratedEvent(
            (ushort)EventId.EnvFreezing,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = EventConstants.PhasePrefix + "Freezing environment")]
        public abstract void EnvFreeze(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.CacheShutdownFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Cache shutdown failed. {reason}")]
        public abstract void CacheShutdownFailed(LoggingContext context, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.CouldNotCreateSystemMount,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Could not add system mount '{mountName}'. The mount did not resolve correctly. Resolved path was '{resolvedPath}'")]
        public abstract void CouldNotAddSystemMount(LoggingContext context, string mountName, string resolvedPath);

        [GeneratedEvent(
            (ushort)LogEventId.DirectoryMembershipFingerprinterRuleError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "Error in DirectoryMembershipFingerprinterRule '{name}' for root '{root}'. It must either set DisableFilesystemEnumeration or include at least one FileIgnoreWildcard, but not both.")]
        public abstract void DirectoryMembershipFingerprinterRuleError(LoggingContext context, string name, string root);

        [GeneratedEvent(
            (ushort)LogEventId.DuplicateDirectoryMembershipFingerprinterRule,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "{configPath}: The DirectoryMembershipFingerprinterRule '{newRule}' specified in this file shares the same root '{root}' with rule '{existingRule}' defined in this or another config. Only one rule may exist per root directory.")]
        public abstract void DuplicateDirectoryMembershipFingerprinterRule(LoggingContext context, string configPath, string existingRule, string newRule, string root);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidDirectoryTranslation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "Invalid directory translations specified by /translateDirectory: {reason}.")]
        public abstract void InvalidDirectoryTranslation(LoggingContext context, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.DirectoryTranslationsDoNotPassJunctionTest,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Directory translations do not pass junction test: {reason}")]
        public abstract void DirectoryTranslationsDoNotPassJunctionTest(LoggingContext context, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.FinishedCopyingGraphToLogDir,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Linked/Copied graph files to log directory: Files: {0} | Elapsed time: {1}ms")]
        public abstract void FinishedCopyingGraphToLogDir(LoggingContext context, string fileNames, long milliseconds);

        [GeneratedEvent(
            (int)EventId.CannotAddCreatePipsDuringConfigOrModuleEvaluation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "Pips are not allowed to be created from configuration files.")]
        public abstract void CannotAddCreatePipsDuringConfigOrModuleEvaluation(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.FileAccessManifestSummary,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.Scheduler,
            Message =
                "File access whitelist provided to scheduler for module {moduleName} has {cacheableEntryCount} cacheable and {unchacheableEntryCount} uncacheable entries.")]
        public abstract void FileAccessManifestSummary(
            LoggingContext context,
            string moduleName,
            int cacheableEntryCount,
            int unchacheableEntryCount);

        #region Graph Caching

        [GeneratedEvent(
             (int)LogEventId.FailedToDeserializePreviousInputs,
             EventGenerators = EventGenerators.LocalOnly,
             EventLevel = Level.Warning,
             Keywords = (int)Keywords.UserMessage,
             EventTask = (int)Tasks.Engine,
             Message = "Failed reading previous inputs file. Build will continue without cached graph. Error: {0}")]
        public abstract void FailedToDeserializePreviousInputs(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.FailedToDeserializePipGraph,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed reading pip graph file '{0}' from previous run. Build will continue without cached graph. Error: {1}")]
        public abstract void FailedToDeserializePipGraph(LoggingContext context, string file, string message);

        [GeneratedEvent(
            (int)LogEventId.FailedToDeserializeDueToFileNotFound,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed reading '{0}' from previous run because it was deleted in the EngineCache directory.")]
        public abstract void FailedToDeserializeDueToFileNotFound(LoggingContext context, string file);

        [GeneratedEvent(
            (int)LogEventId.FailedToAcquireDirectoryDeletionLock,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (int)Tasks.Engine,
            Message = "Failed to acquire a lock to prevent directory deletion. This may be due to invoking concurrent builds with overlapping directories. Error: {0}")]
        public abstract void FailedToAcquireDirectoryDeletionLock(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.FailedToAcquireDirectoryLock,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (int)Tasks.Engine,
            Message = "Failed to acquire a lock to prevent concurrent builds: {0}")]
        public abstract void FailedToAcquireDirectoryLock(LoggingContext context, string innerException);

        [GeneratedEvent(
            (int)LogEventId.CacheSessionCloseFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed to close the cache session: {0}")]
        public abstract void CacheSessionCloseFailed(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.FileAccessErrorsExist,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Build failed due to file access issues. The build was requested to continue building downstream pips on file access issues, so they are marked as warnings instead of errors. But their existance is the reason for the build failure. Check for DX0009. DX0051, DX0277, and DX0378 warnings.")]
        public abstract void FileAccessErrorsExist(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.FailedToSerializePipGraph,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed saving pip graph for future run. Future builds may not be able to use cached graph. Error: {0}")]
        public abstract void FailedToSerializePipGraph(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.GraphNotReusedDueToChangedInput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable),
            EventTask = (int)Tasks.Engine,
            Message = EventConstants.PhasePrefix + "Constructing pip graph. {0}")]
        public abstract void GraphNotReusedDueToChangedInput(LoggingContext context, string missReason, string additionalInformation);

        [GeneratedEvent(
            (int)LogEventId.SerializedFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Performance,
            EventTask = (int)Tasks.Engine,
            Message = "Serialized {0} in {1}ms")]
        public abstract void SerializedFile(LoggingContext context, string objectSerialized, long milliseconds);

        [GeneratedEvent(
            (int)LogEventId.DeserializedFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Performance,
            EventTask = (int)Tasks.Engine,
            Message = "Deserialized '{0}' in {1}ms")]
        public abstract void DeserializedFile(LoggingContext context, string file, long milliseconds);

        [GeneratedEvent(
            (int)LogEventId.FailedToSaveGraphToCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed to save the serialized pip graph to the cache. UsedUniqueFingerprint: {usedUniqueFingerprint}. Error: {message}")]
        public abstract void FailedToSaveGraphToCache(LoggingContext context, bool usedUniqueFingerprint, string message);

        [GeneratedEvent(
            (int)LogEventId.FailedToFetchGraphDescriptorFromCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed to fetch graph descriptor from cache. Error: {0}")]
        public abstract void FailedToFetchGraphDescriptorFromCache(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.GetPipGraphDescriptorFromCache,
            EventGenerators = EventGenerators.LocalAndTelemetryAndStatistic,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message =
                "Fetching pip graph descriptor from cache:\r\n\tCompatible fingerprint: [Status: {compatibleStatus} | Hop count: {compatibleHopCount} | Reason: {compatibleReason} | Elapsed time: {compatibleElapsed}ms (Hashing graph inputs: {compatibleHashingGraphInputsElapsedMs}ms, Fingerprint retrieval: {compatibleFingerprintRetrievalElapsedMs}ms)]\r\n{compatibleFingerprintChain}\r\n\tExact fingerprint: [Status: {exactStatus} | Hop count: {exactHopCount} | Reason: {exactReason} | Elapsed time: {exactElapsed}ms (Hashing graph inputs: {exactHashingGraphInputsElapsedMs}ms, Fingerprint retrieval: {exactFingerprintRetrievalElapsedMs}ms)]{exactFingerprintChain}")]
        public abstract void GetPipGraphDescriptorFromCache(
            LoggingContext context,
            string compatibleStatus,
            int compatibleHopCount,
            string compatibleReason,
            int compatibleElapsed,
            int compatibleHashingGraphInputsElapsedMs,
            int compatibleFingerprintRetrievalElapsedMs,
            string compatibleFingerprintChain,
            string exactStatus,
            int exactHopCount,
            string exactReason,
            int exactElapsed,
            int exactHashingGraphInputsElapsedMs,
            int exactFingerprintRetrievalElapsedMs,
            string exactFingerprintChain);

        [GeneratedEvent(
            (int) LogEventId.StorePipGraphCacheDescriptorToCache,
            EventGenerators = EventGenerators.LocalAndTelemetryAndStatistic,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Storing pip graph descriptor to cache: Status: {status} | Hop count: {hopCount} | Reason: {reason} | Elapsed time: {elapsed}ms (Hashing graph inputs: {hashingGraphInputsElapsed}ms, Storing fingerprint entry: {storingFingerprintEntryElapsedMs}ms, Loading and deserialize metadata: {loadingDeserializeElapsedMs}ms)\r\n{fingerprintChains}")]
        public abstract void StorePipGraphCacheDescriptorToCache(
            LoggingContext context,
            string status,
            int hopCount,
            string reason,
            int elapsed,
            int hashingGraphInputsElapsed,
            int storingFingerprintEntryElapsedMs,
            int loadingDeserializeElapsedMs,
            string fingerprintChains);

        [GeneratedEvent(
            (int)LogEventId.MismatchPathInGraphInputDescriptor,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "[Hop {hop}] Mismatch input path '{path}' ({kind}) in graph input descriptor: Given: '{givenHash}' | Actual: '{actualHash}'")]
        public abstract void MismatchPathInGraphInputDescriptor(LoggingContext context, int hop, string path, string kind, string givenHash, string actualHash);

        [GeneratedEvent(
            (int)LogEventId.FailedHashingGraphFileInput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "[Hop {hop}] Failed to hash '{path}' for graph input: {reason}")]
        public abstract void FailedHashingGraphFileInput(LoggingContext context, int hop, string path, string reason);

        [GeneratedEvent(
            (int)LogEventId.FailedComputingFingerprintGraphDirectoryInput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "[Hop {hop}] Failed to compute directory membership fingerprint of '{path}' for graph input: {reason}")]
        public abstract void FailedComputingFingerprintGraphDirectoryInput(LoggingContext context, int hop, string path, string reason);

        [GeneratedEvent(
            (int)LogEventId.MismatchEnvironmentInGraphInputDescriptor,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "[Hop {hop}] Mismatch input environment variable '{name}' in graph input descriptor: Given: '{givenValue}' | Actual: '{actualValue}'")]
        public abstract void MismatchEnvironmentInGraphInputDescriptor(LoggingContext context, int hop, string name, string givenValue, string actualValue);

        [GeneratedEvent(
            (int)LogEventId.MismatchMountInGraphInputDescriptor,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "[Hop {hop}] Mismatch input mount '{name}' in graph input descriptor: Given: '{givenValue}' | Actual: '{actualValue}'")]
        public abstract void MismatchMountInGraphInputDescriptor(LoggingContext context, int hop, string name, string givenValue, string actualValue);

        [GeneratedEvent(
            (int)LogEventId.FailedToFetchSerializedGraphFromCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed to fetch serialized graph from cache. Build will proceed without cached graph. Error: {0}")]
        public abstract void FailedToFetchSerializedGraphFromCache(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.FetchedSerializedGraphFromCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Successfully fetched the serialized graph from content cache.")]
        public abstract void FetchedSerializedGraphFromCache(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.FailedToComputeGraphFingerprint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed to hash input file to create input fingerprint for graph caching. {0}")]
        public abstract void FailedToComputeGraphFingerprint(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.MatchedCompatibleGraphFingerprint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Matched the graph fingerprint from a compatible (fully evaluated) graph.")]
        public abstract void MatchedCompatibleGraphFingerprint(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.MatchedExactGraphFingerprint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Matched the graph fingerprint from an exact (partially evaluated) graph.")]
        public abstract void MatchedExactGraphFingerprint(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.FailedToComputeHashFromDeploymentManifest,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed to read deployment manifest to compute engine version for graph caching. Graph caching will be disabled.")]
        public abstract void FailedToComputeHashFromDeploymentManifest(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.ElementsOfConfigurationFingerprint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Fingerprint of configuration: {fingerprint}\n{elements}")]
        public abstract void ElementsOfConfigurationFingerprint(LoggingContext context, string fingerprint, string elements);

        [GeneratedEvent(
            (int)LogEventId.FailedToComputeHashFromDeploymentManifestReason,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed to read deployment manifest because: {message}.")]
        public abstract void FailedToComputeHashFromDeploymentManifestReason(LoggingContext context, string message);

        #endregion

        #region Symlink file

        [GeneratedEvent(
            (int)LogEventId.FailedStoreSymlinkFileToCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed to store symlink file '{file}' to cache: {message}.")]
        public abstract void FailedStoreSymlinkFileToCache(LoggingContext context, string file, string message);

        [GeneratedEvent(
            (int)LogEventId.FailedLoadSymlinkFileFromCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed to load symlink file from cache: {message}.")]
        public abstract void FailedLoadSymlinkFileFromCache(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.FailedMaterializeSymlinkFileFromCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed to materialize symlink file from cache to '{file}': {message}.")]
        public abstract void FailedMaterializeSymlinkFileFromCache(LoggingContext context, string file, string message);

        #endregion

        [GeneratedEvent(
            (int)EventId.ErrorSavingSnapshot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Error saving to snapshot file '{0}': {1}")]
        public abstract void ErrorSavingSnapshot(LoggingContext context, string file, string exceptionDetails);

        [GeneratedEvent(
            (int)EventId.GenericSnapshotError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (int)Tasks.Engine,
            Keywords = (int)Keywords.UserMessage,
            Message = "Snapshot error: {0}")]
        public abstract void GenericSnapshotError(LoggingContext context, string message);

        [GeneratedEvent(
            (int)EventId.ErrorCaseSensitiveFileSystemDetected,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (int)Tasks.Engine,
            Keywords = (int)Keywords.UserMessage,
            Message = "{ShortProductName} failed to run because only 'case-insensitive' file-systems are currently supported on non-windows hosts.")]
        public abstract void ErrorCaseSensitiveFileSystemDetected(LoggingContext context);

        public void BusyOrUnavailableOutputDirectories(LoggingContext context, string objectDirectoryPath, string exception)
        {
            BusyOrUnavailableOutputDirectories(context, objectDirectoryPath);
            BusyOrUnavailableOutputDirectoriesException(context, objectDirectoryPath, exception);
        }

        [GeneratedEvent(
            (int)EventId.BusyOrUnavailableOutputDirectories,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (int)Tasks.Engine,
            Message = "Concurrent builds using the same output directories are not supported. Directory already in use or not reachable {0}.")]
        public abstract void BusyOrUnavailableOutputDirectories(LoggingContext context, string objectDirectoryPath);

        [GeneratedEvent(
            (int)LogEventId.BusyOrUnavailableOutputDirectoriesException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Directory {0} lock exception: {1}")]
        public abstract void BusyOrUnavailableOutputDirectoriesException(LoggingContext context, string objectDirectoryPath, string exception);

        [GeneratedEvent(
            (int)EventId.BusyOrUnavailableOutputDirectoriesRetry,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message =
                "Build queued. Waiting {0} mins for a concurrent build, using path: '{1}' to finish.")]

        public abstract void BusyOrUnavailableOutputDirectoriesRetry(LoggingContext context, int buildLockWaitTimeMins, string directoryPath);

        #region Scrubbing/Cleaning
        [GeneratedEvent(
            (int)EventId.ScrubbingExternalFileOrDirectoryFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Scrubber failed to remove file or directory '{0}'. {1}")]
        public abstract void ScrubbingExternalFileOrDirectoryFailed(LoggingContext context, string path, string error);

        [GeneratedEvent(
            (int)EventId.ScrubbingFailedToEnumerateDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Scrubber failed to enumerate directory '{0}'. {1}")]
        public abstract void ScrubbingFailedToEnumerateDirectory(LoggingContext context, string path, string error);

        [GeneratedEvent(
            (int)EventId.ScrubbingFailedToEnumerateMissingDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics),
            EventTask = (int)Tasks.Engine,
            Message = "Scrubber failed to enumerate missing directory '{0}'.")]
        public abstract void ScrubbingFailedToEnumerateMissingDirectory(LoggingContext context, string path);

        [GeneratedEvent(
            (int)EventId.ScrubbingFailedBecauseDirectoryIsNotScrubbable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Scrubber failed because directory '{0}' is not under a scrubbable mount. To scrub the directory, the mount '{1}' ('{2}') must be made scrubbable.")]
        public abstract void ScrubbingFailedBecauseDirectoryIsNotScrubbable(LoggingContext context, string path, string mountName, string mountPath);

        [GeneratedEvent(
            (int)EventId.ScrubbingDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Scrubber will scrub directory '{0}'.")]
        public abstract void ScrubbingDirectory(LoggingContext context, string path);

        [GeneratedEvent(
            (int)EventId.ScrubbingDeleteDirectoryContents,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Scrubber deletes the contents of directory '{0}'.")]
        public abstract void ScrubbingDeleteDirectoryContents(LoggingContext context, string path);

        [GeneratedEvent(
            (int)EventId.ScrubbingFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Scrubber deletes file '{0}'.")]
        public abstract void ScrubbingFile(LoggingContext context, string path);

        private const string ScrubbingStatusPrefix = "Scrubbing files extraneous to this build.";

        [GeneratedEvent(
            (ushort)EventId.ConfigUnsafeSharedOpaqueEmptyDirectoryScrubbingDisabled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_DisableSharedOpaqueEmptyDirectoryScrubbing: removal of empty directories within shared opaques has been disabled. This is an unsafe configuration since it may work in detriment of build correctness.")]
        public abstract void ConfigUnsafeDisableSharedOpaqueEmptyDirectoryScrubbing(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.ScrubbingStarted,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Keywords.UserMessage | Keywords.Overwritable),
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Message = EventConstants.PhasePrefix + ScrubbingStatusPrefix)]
        public abstract void ScrubbingStarted(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.ScrubbingSharedOpaquesStarted,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Keywords.UserMessage | Keywords.Overwritable),
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Message = EventConstants.PhasePrefix + "Scrubbing content from shared opaque directories.")]
        public abstract void ScrubbingSharedOpaquesStarted(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.ScrubbingFinished,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = EventConstants.PhasePrefix + "Scrubbing finished. Traversed {0} directories and checked {1} files. {2} files were deleted. {3} directories were deleted recursively.")]
        public abstract void ScrubbingFinished(LoggingContext context, int directoryCount, int totalFiles, int deletedFiles, int deletedDirectoriesRecursively);

        [GeneratedEvent(
            (int)EventId.ScrubbingStatus,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Keywords.UserMessage | Keywords.Overwritable),
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = EventConstants.PhasePrefix + ScrubbingStatusPrefix + " Files processed: {0} ")]
        public abstract void ScrubbingStatus(LoggingContext context, int filesCompleteCount);

        [GeneratedEvent(
            (int)EventId.ScrubbableMountsMayOnlyContainScrubbableMounts,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "Mount named '{childMountName}' rooted at '{childMountRoot}' is not marked with IsScrubbable, but it is nested within mount '{parentMountName}' rooted at'{parentMountRoot}' which is. Scrubbable mounts may only contain child mounts that are also scrubbable. See related location for parent mount.")]
        public abstract void ScrubbableMountsMayOnlyContainScrubbableMounts(LoggingContext context, string file, int line, int column, string childMountName, string childMountRoot, string parentMountName, string parentMountRoot);

        [GeneratedEvent(
            (int)LogEventId.NonReadableConfigMountsMayNotContainReadableModuleMounts,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "Module mount named '{childMountName}' rooted at '{childMountRoot}' is marked as readable, but it is nested within configuration mount '{parentMountName}' rooted at'{parentMountRoot}' which is not readable. Non-readable configuration mounts may not contain readable module mounts. See related location of configuration mount.")]
        public abstract void NonReadableConfigMountsMayNotContainReadableModuleMounts(LoggingContext context, string file, int line, int column, string childMountName, string childMountRoot, string parentMountName, string parentMountRoot);

        [GeneratedEvent(
            (int)LogEventId.NonWritableConfigMountsMayNotContainWritableModuleMounts,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "Module mount named '{childMountName}' rooted at '{childMountRoot}' is marked as writable, but it is nested within configuration mount '{parentMountName}' rooted at'{parentMountRoot}' which is not writable. Non-writable configuration mounts may not contain writable module mounts. See related location of configuration mount.")]
        public abstract void NonWritableConfigMountsMayNotContainWritableModuleMounts(LoggingContext context, string file, int line, int column, string childMountName, string childMountRoot, string parentMountName, string parentMountRoot);

        [GeneratedEvent(
            (int)LogEventId.ModuleMountsWithSameNameAsConfigMountsMustHaveSamePath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "Module mount named '{childMountName}' rooted at '{childMountRoot}' has same name as configuration mount '{parentMountName}' rooted at'{parentMountRoot}' which has a different root path. Ensure module mounts with same name as configuration mounts refer to the same path. See related location of configuration mount.")]
        public abstract void ModuleMountsWithSameNameAsConfigMountsMustHaveSamePath(LoggingContext context, string file, int line, int column, string childMountName, string childMountRoot, string parentMountName, string parentMountRoot);

        [GeneratedEvent(
            (int)LogEventId.ModuleMountsWithSamePathAsConfigMountsMustHaveSameName,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "Module mount named '{childMountName}' rooted at '{childMountRoot}' has same root path as configuration mount '{parentMountName}' rooted at'{parentMountRoot}' which has a different name. Ensure module mounts with same root path as configuration mounts have the same name. See related location of configuration mount.")]
        public abstract void ModuleMountsWithSamePathAsConfigMountsMustHaveSameName(LoggingContext context, string file, int line, int column, string childMountName, string childMountRoot, string parentMountName, string parentMountRoot);

        [GeneratedEvent(
            (int)LogEventId.MountHasInvalidName,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = EventConstants.ProvenancePrefix + "Mount has invalid name.")]
        public abstract void MountHasInvalidName(LoggingContext context, string file, int line, int column);

        [GeneratedEvent(
            (int)LogEventId.MountHasInvalidPath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = EventConstants.ProvenancePrefix + "Mount '{3}' has invalid path.")]
        public abstract void MountHasInvalidPath(LoggingContext context, string file, int line, int column, string name);

        [GeneratedEvent(
            (int)EventId.CleaningStarted,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Message = EventConstants.PhasePrefix + "Cleaning build output files only. No pips will be executed in this build.")]
        public abstract void CleaningStarted(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.CleaningFinished,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = "Finished cleaning build output files. {0} successfully deleted. {1} failed.")]
        public abstract void CleaningFinished(LoggingContext context, int successCount, int failCount);

        [GeneratedEvent(
            (int)EventId.CleaningFileFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed to clean build output file '{0}'. {1}")]
        public abstract void CleaningFileFailed(LoggingContext context, string path, string message);

        [GeneratedEvent(
            (int)EventId.CleaningDirectoryFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Failed to clean build output directory '{0}'. {1}")]
        public abstract void CleaningDirectoryFailed(LoggingContext context, string path, string message);

        [GeneratedEvent(
            (int)EventId.CleaningOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Cleaning output file '{0}'.")]
        public abstract void CleaningOutputFile(LoggingContext context, string path);
        #endregion

        #region Running Time and Critical Path Suggestions
        [GeneratedEvent(
            (int)EventId.StartRehydratingConfigurationWithNewPathTable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Message = EventConstants.PhasePrefix + "Rehydrating configuration with new PathTable")]
        public abstract void StartRehydratingConfigurationWithNewPathTable(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.EndRehydratingConfigurationWithNewPathTable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Performance,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = EventConstants.PhasePrefix + "Done rehydrating configuration with new PathTable")]
        public abstract void EndRehydratingConfigurationWithNewPathTable(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.StartLoadingRunningTimes,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Message = EventConstants.PhasePrefix + "Loading running times")]
        public abstract void StartLoadingRunningTimes(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.EndLoadingRunningTimes,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Performance,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = EventConstants.PhasePrefix + "Done loading running times")]
        public abstract void EndLoadingRunningTimes(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.StartSavingRunningTimes,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Message = EventConstants.PhasePrefix + "Saving updated running times")]
        public abstract void StartSavingRunningTimes(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.EndSavingRunningTimes,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Performance,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = EventConstants.PhasePrefix + "Done saving updated running times")]
        public abstract void EndSavingRunningTimes(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.FailedToResolveHistoricDataFileName,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.Engine,
            Message = "Unable to resolve historic data filename. Error: {0}")]
        public abstract void FailedToResolveHistoricDataFileName(LoggingContext context, string errorMessage);

        [GeneratedEvent(
            (int)EventId.LoadingRunningTimesFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.Engine,
            Message = "Unable to load historic performance data file {0}: {1}")]
        public abstract void LoadingRunningTimesFailed(LoggingContext context, string fileName, string errorMessage);

        [GeneratedEvent(
            (int)EventId.RunningTimesLoaded,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Historic performance data loaded: {0} entries")]
        public abstract void RunningTimesLoaded(LoggingContext context, int size);

        [GeneratedEvent(
            (int)EventId.SavingRunningTimesFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.Engine,
            Message = "Unable to save historic performance data file {0}: {1}")]
        public abstract void SavingRunningTimesFailed(LoggingContext context, string fileName, string errorMessage);

        [GeneratedEvent(
            (int)EventId.RunningTimesSaved,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Historic performance data saved: {0} entries")]
        public abstract void RunningTimesSaved(LoggingContext context, int size);

        [GeneratedEvent(
            (int)EventId.FailedToResolveHistoricMetadataCacheFileName,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.Engine,
            Message = "Unable to resolve historic metadata cache filename. Error: {0}")]
        public abstract void FailedToResolveHistoricMetadataCacheFileName(LoggingContext context, string errorMessage);

        [GeneratedEvent(
            (int)EventId.LoadingHistoricMetadataCacheFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.Engine,
            Message = "Unable to load historic metadata cache file {0}: {1}")]
        public abstract void LoadingHistoricMetadataCacheFailed(LoggingContext context, string fileName, string errorMessage);

        [GeneratedEvent(
            (int)EventId.HistoricMetadataCacheLoaded,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Historic metadata cache loaded: {0} metadata entries, {1} pathset entries")]
        public abstract void HistoricMetadataCacheLoaded(LoggingContext context, int metadataCount, int pathSetCount);

        [GeneratedEvent(
            (int)EventId.SavingHistoricMetadataCacheFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.Engine,
            Message = "Unable to save historic metadata cache file {0}: {1}")]
        public abstract void SavingHistoricMetadataCacheFailed(LoggingContext context, string fileName, string errorMessage);

        [GeneratedEvent(
            (int)EventId.HistoricMetadataCacheSaved,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Historic metadata cache saved: {0} metadata entries, {1} pathset entries")]
        public abstract void HistoricMetadataCacheSaved(LoggingContext context, int metadataCount, int pathSetCount);

        #endregion

        #region Configuration

        [GeneratedEvent(
            (int)EventId.ConfigFailedParsingCommandLinePipFilter,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message =
                "Error at position {1} of command line pip filter {0}. {3} {2}")]
        public abstract void ConfigFailedParsingCommandLinePipFilter(LoggingContext context, string rawFilter, int position, string filterWithPointer, string error);

        [GeneratedEvent(
            (int)EventId.ConfigFailedParsingDefaultPipFilter,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message =
                "Error at position {1} of default pip filter from config file {0}. {3} {2}")]
        public abstract void ConfigFailedParsingDefaultPipFilter(LoggingContext context, string rawFilter, int position, string filterWithPointer, string error);

        [GeneratedEvent(
            (int)EventId.ConfigUsingPipFilter,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = EventConstants.PhasePrefix +
                "Using pip filter: {0}")]
        public abstract void ConfigUsingPipFilter(LoggingContext context, string rawFilter);

        [GeneratedEvent(
            (int)EventId.ConfigFilterAndPathImplicitNotSupported,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message =
                "Implicit path filters, '{0}', may not be specified on the command line in conjunction with the /filter: option. Add the paths to the filter expression with output='path' or spec = 'path'.")]
        public abstract void ConfigFilterAndPathImplicitNotSupported(LoggingContext context, string filters);

        #endregion

        [GeneratedEvent(
            (int)EventId.StartInitializingCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Message = EventConstants.PhasePrefix + "Initializing the cache")]
        public abstract void StartInitializingCache(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.EndInitializingCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Performance,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = EventConstants.PhasePrefix + "Machine-wide cache lock acquired")]
        public abstract void EndInitializingCache(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.SynchronouslyWaitedForCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Performance,
            EventTask = (int)Tasks.Engine,
            Message = "Synchronously waited {0}ms for cache to finish initializing. {1}ms of cache initialization overlapped other processing")]
        public abstract void SynchronouslyWaitedForCache(LoggingContext context, int waitTimeMs, int overlappedTimeMs);

        [GeneratedEvent(
            (int)EventId.StartParseConfig,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Message = EventConstants.PhasePrefix + "Parsing configuration files")]
        public abstract void StartParseConfig(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.EndParseConfig,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Performance,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = EventConstants.PhasePrefix + "Done parsing configuration files")]
        public abstract void EndParseConfig(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.StorageCacheStartupError,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Storage,
            Message = "Starting up the cache resulted in an error: {0}")]
        public abstract void StorageCacheStartupError(LoggingContext context, string errorMessage);

        [GeneratedEvent(
            (int)EventId.StatsBanner,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Engine Statistics")]
        public abstract void StatsBanner(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.FrontEndStatsBanner,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Front End Statistics")]
        public abstract void FrontEndStatsBanner(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.GCStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "  Garbage Collections: Gen0 {0}, Gen1 {1}, Gen2 {2}")]
        public abstract void GCStats(LoggingContext context, long gen0CollectionCount, long gen1CollectionCount, long gen2CollectionCount);

        [GeneratedEvent(
            (int)EventId.ObjectPoolStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "  {0} object pool: {1} entries, {2} uses, {3} factory invocations")]
        public abstract void ObjectPoolStats(LoggingContext context, string pool, int entryCount, long useCount, long factoryInvocations);

        [GeneratedEvent(
            (int)EventId.PipWriterStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "  Serialized {0}: {1} entries, {2} total bytes, {3} bytes/entry")]
        public abstract void PipWriterStats(LoggingContext context, string category, long entries, long totalBytes, int bytesPerEntry);

        [GeneratedEvent(
            (int)EventId.InterningStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "  {0}: {1} entries, {2} bytes of heap, {3} skipped allocated entries")]
        public abstract void InterningStats(LoggingContext context, string table, int entryCount, long sizeInBytes, int skippedEntries = 0);

        [GeneratedEvent(
            (int)EventId.ObjectCacheStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "  {0}: {1} hits, {2} misses")]
        public abstract void ObjectCacheStats(LoggingContext context, string table, long hits, long misses);

        [GeneratedEvent(
            (int)EventId.EnvironmentValueForTempDisallowed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message =
                EventConstants.ProvenancePrefix +
                "The environment variable '{3}' cannot be read within a configuration file as it specifies a location for temporary files. Since BuildXL redirects these temporary directories, this value would not be useful.")]
        public abstract void EnvironmentValueForTempDisallowed(
            LoggingContext context,
            string file,
            int line,
            int column,
            string environmentVariableName);

        [GeneratedEvent(
            (int)EventId.FileAccessWhitelistEntryHasInvalidRegex,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message =
                EventConstants.ProvenancePrefix +
                "Unable to create file access whitelist entry.  Failed to construct an ECMAScript regex object with error '{3}'.")]
        public abstract void FileAccessWhitelistEntryHasInvalidRegex(
            LoggingContext context,
            string file,
            int line,
            int column,
            string constructorExceptionMessage);

        [GeneratedEvent(
            (ushort)EventId.JournalRequiredOnVolumeError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "The volume, '{drive}' does not have an enabled change journal. Change journaling is required for volumes containing sources, build outputs, and the build cache. Please open an elevated command prompt and run:\n {command}")]
        public abstract void JournalRequiredOnVolumeError(LoggingContext context, string drive, string command);

        [GeneratedEvent(
            (int)EventId.StartEngineRun,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.Diagnostics | (int)Keywords.Progress,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Message = EventConstants.PhasePrefix + "Run engine")]
        public abstract void StartEngineRun(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.EndEngineRun,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.Diagnostics | (int)Keywords.Performance | (int)Keywords.Progress,
            EventTask = (int)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = EventConstants.PhasePrefix + "Done running engine")]
        public abstract void EndEngineRun(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.FileAccessWhitelistCouldNotCreateIdentifier,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message =
                EventConstants.ProvenancePrefix +
                "Unable to create file access whitelist entry for requested value '{3}'. Value names only allow a-z, 0-9, and '.' separating pars.")]
        public abstract void FileAccessWhitelistCouldNotCreateIdentifier(
            LoggingContext context,
            string file,
            int line,
            int column,
            string environmentVariableName);

        [GeneratedEvent(
            (int)EventId.SchedulerExportFailedSchedulerNotInitialized,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Failed to exporting pip graph, fingerprints, or incremental scheduling state because the scheduler was not initialized.",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void SchedulerExportFailedSchedulerNotInitialized(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.PipTableStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage

                // Computing the parameters to log this is a bit expensive (~50ms). So make it diagnostic
                | (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.Engine,
            Message = "  PipTable created {0} streams occupying {1} bytes, using {2} bytes; {3} entries, {4} entries written (in {7} ms), {5} entries read (in {8} ms), {6} entries alive")]
        public abstract void PipTableStats(LoggingContext context, int streams, long size, long used, int count, int writes, long reads, int alive, long writingTime, long readingTime);

        [GeneratedEvent(
            (int)EventId.PipTableDeserializationContext,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage | (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.Engine,
            Message = "  PipTable deserialized {1} pips because of {0}")]
        public abstract void PipTableDeserializationContext(LoggingContext context, string name, int count);

      [GeneratedEvent(
            (int)LogEventId.VirusScanEnabledForPath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Virus scanning software is enabled for '{path}' which is used in this build. Build performance is potentially greatly impacted unless a directory exclusion is configured.")]
        public abstract void VirusScanEnabledForPath(LoggingContext context, string path);

        [GeneratedEvent(
            (int)LogEventId.EmitSpotlightIndexingWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Spotlight indexing for artifact directories [ {directories} ] has not been disabled, consider adding the '.noindex' suffix to the specified directories. Build performance may be impacted!")]
        public abstract void EmitSpotlightIndexingWarningForArtifactDirectory(LoggingContext context, string directories);

        [GeneratedEvent(
            (ushort)LogEventId.PreserveOutputsNotAllowedInDistributedBuild,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Scheduler,
            Message = "PreserveOutputs is not allowed for distributed builds.")]
        internal abstract void PreserveOutputsNotAllowedInDistributedBuild(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)LogEventId.PreserveOutputsWithNewSalt,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "Using a new PreserveOutputs salt of: {preserveOutputsSalt}")]
        internal abstract void PreserveOutputsWithNewSalt(LoggingContext loggingContext, string preserveOutputsSalt);

        [GeneratedEvent(
            (ushort)LogEventId.PreserveOutputsWithExistingSalt,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "Using an existing PreserveOutputs salt of: {preserveOutputsSalt}")]
        internal abstract void PreserveOutputsWithExistingSalt(LoggingContext loggingContext, string preserveOutputsSalt);

        [GeneratedEvent(
            (ushort)LogEventId.PreserveOutputsFailedToInitializeSalt,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "Failed to initialize the PreserveOutputs salt file: {error}")]
        internal abstract void PreserveOutputsFailedToInitializeSalt(LoggingContext loggingContext, string error);

        [GeneratedEvent(
            (ushort)LogEventId.WrittenBuildInvocationToUserFolder,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "Wrote build invocation '{buildInvocationLine}' to '{path}'")]
        internal abstract void WrittenBuildInvocationToUserFolder(LoggingContext loggingContext, string buildInvocationLine, string path);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToWriteBuildInvocationToUserFolder,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "Failed to write the build invocation '{buildInvocationLine}' to '{path}': {error}")]
        internal abstract void FailedToWriteBuildInvocationToUserFolder(LoggingContext loggingContext, string buildInvocationLine, string path, string error);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToReadBuildInvocationToUserFolder,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "Failed to read the build invocation list from '{path}': {error}")]
        internal abstract void FailedToReadBuildInvocationToUserFolder(LoggingContext loggingContext, string path, string error);

        [GeneratedEvent(
            (ushort)LogEventId.FailureLaunchingBuildExplorerFileNotFound,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "Failed to launch build explorer. File '{bxpPath}' does not exist.")]
        internal abstract void FailureLaunchingBuildExplorerFileNotFound(LoggingContext loggingContext, string bxpPath);

        [GeneratedEvent(
            (ushort)LogEventId.FailureLaunchingBuildExplorerException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "Failed to launch build explorer. {error}")]
        internal abstract void FailureLaunchingBuildExplorerException(LoggingContext loggingContext, string error);



        [GeneratedEvent(
            (ushort)LogEventId.FailedToInitalizeFileAccessWhitelist,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "Failed to initialize the FileAccess whitelist: {error}")]
        internal abstract void FailedToInitializeFileAccessWhitelist(LoggingContext loggingContext, string error);

        [GeneratedEvent(
            (ushort)LogEventId.ForceSkipDependenciesOrDistributedBuildOverrideIncrementalScheduling,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "Incremental scheduling is disabled because /unsafe_forceSkipDeps or distributed build is enabled")]
        internal abstract void ForceSkipDependenciesOrDistributedBuildOverrideIncrementalScheduling(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)LogEventId.ForceSkipDependenciesEnabled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "/unsafe_forceSkipDeps Enabled: {ShortProductName} skips the dependencies of processes as long as all inputs are present on disk.")]
        internal abstract void ForceSkipDependenciesEnabled(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)LogEventId.ReusedEngineState,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "Reused previous engine state to load the graph.")]
        internal abstract void ReusedEngineState(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.DisposedEngineStateDueToGraphId,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "Disposed the EngineState because the engine cache dir and the EngineState object are out-of-sync.")]
        internal abstract void DisposedEngineStateDueToGraphId(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.UsingPatchableGraphBuilder,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = @"Using patchable graph builder.")]
        internal abstract void UsingPatchableGraphBuilder(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToRecoverFailure,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "Failed to recover from failure '{action}': {reason}")]
        internal abstract void FailedToRecoverFailure(LoggingContext context, string action, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToMarkFailure,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "Failed to mark failure '{action}': {reason}")]
        internal abstract void FailedToMarkFailure(LoggingContext context, string action, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.SuccessfulFailureRecovery,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "Successfully recovery from failure '{action}'")]
        internal abstract void SuccessfulFailureRecovery(LoggingContext context, string action);

        [GeneratedEvent(
            (ushort)LogEventId.SuccessfulMarkFailure,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "Successfully mark failure '{action}'")]
        internal abstract void SuccessfulMarkFailure(LoggingContext context, string action);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorRelatedLocation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Parser,
            Message = EventConstants.ProvenancePrefix + "Related location of error in file '{3}' on line '{4}' at column '{5}'.")]
        public abstract void ErrorRelatedLocation(LoggingContext context, string file, int line, int column, string originalFile, int originalLine, int originalColumn);

        [GeneratedEvent(
            (ushort)EventId.ConfigIgnoreDynamicWritesOnAbsentProbes,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "/unsafe_IgnoreDynamicWritesOnAbsentProbes enabled: {ShortProductName} will not flag as violations absent path probes that are later written under output directories.")]
        public abstract void ConfigIgnoreDynamicWritesOnAbsentProbes(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.LogAndRemoveEngineStateOnBuildFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            Message = "Build failed with error(s). Attempting to remove and move engine state to log directory.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void LogAndRemoveEngineStateOnCatastrophicFailure(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.UsingRedirectedUserProfile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            Message = "User profile has been redirected ('{redirectedUserProfile}' -> '{currentUserProfile}')\r\n{redirectedPaths}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void UsingRedirectedUserProfile(LoggingContext context, string currentUserProfile, string redirectedUserProfile, string redirectedPaths);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToRedirectUserProfile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            Message = "Failed to redirect user profile. {detailedErrorMessage}",
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureError))]
        public abstract void FailedToRedirectUserProfile(LoggingContext context, string detailedErrorMessage);

        [GeneratedEvent(
            (ushort)LogEventId.ResourceBasedCancellationIsEnabledWithSharedOpaquesPresent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.PipExecutor,
            Message = "Scheduler has been configured to cancel/re-run pips due to resource exhaustion. There is at least one pip that produces a shared opaque directory ('{sharedOpaquePath}'). "
                      + "Resource based cancellation and shared opaque directories are not compatible. Please use /disableProcessRetryOnResourceExhaustion+ argument to disable resource based cancellation.")]
        internal abstract void ResourceBasedCancellationIsEnabledWithSharedOpaquesPresent(LoggingContext loggingContext, string sharedOpaquePath);

        [GeneratedEvent(
            (ushort)LogEventId.GrpcSettings,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Distribution,
            Message = "Grpc settings: ThreadPoolSize {threadPoolSize}, HandlerInlining {handlerInlining}, CallTimeoutMin {callTimeoutMin}, InactiveTimeoutMin {inactiveTimeoutMin}")]
        internal abstract void GrpcSettings(LoggingContext context, int threadPoolSize, bool handlerInlining, int callTimeoutMin, int inactiveTimeoutMin);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToGetJournalAccessor,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "Change journal cannot be accessed directly. The build may still proceed but without use of change journal scanning. {errorMessage}")]
        internal abstract void FailedToGetJournalAccessor(LoggingContext context, string errorMessage);

        [GeneratedEvent(
            (ushort)LogEventId.StartInitializingVm,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "Start initializing VM: {message}")]
        internal abstract void StartInitializingVm(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.EndInitializingVm,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "End initializing VM: {message}")]
        internal abstract void EndInitializingVm(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.InitializingVm,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "Initializing VM: {message}")]
        internal abstract void InitializingVm(LoggingContext context, string message);
    }

    /// <summary>
    /// Statistics about the parse phase
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct ParseStatistics : IHasEndTime
    {
        /// <summary>
        /// Type of files parsed
        /// </summary>
        public string ParseType;

        /// <summary>
        /// Number of files parsed
        /// </summary>
        public int FileCount;

        /// <inheritdoc/>
        public int ElapsedMilliseconds { get; set; }
    }

    /// <summary>
    /// Empty struct for functions that have no arguments
    /// </summary>
    public readonly struct EmptyStruct
    {
    }

    /// <summary>
    /// Statistics about the parse phase
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct EvaluateStatistics : IHasEndTime
    {
        /// <inheritdoc/>
        public int ElapsedMilliseconds { get; set; }

        /// <summary>
        /// Number of values evaluated
        /// </summary>
        public long ValueCount { get; set; }

        /// <summary>
        /// True if the full graph was evaluated
        /// </summary>
        public bool FullEvaluation { get; set; }
    }

    /// <summary>
    /// Statistics about executing pips
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct ExecuteStatistics : IHasEndTime
    {
        /// <inheritdoc/>
        public int ElapsedMilliseconds { get; set; }
    }

    /// <summary>
    /// Statistics about checking if a cached graph could be reused
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct GraphCacheCheckStatistics : IHasEndTime
    {
        /// <summary>
        /// Whether there was a hit
        /// </summary>
        public bool WasHit { get; set; }

        /// <summary>
        /// Whether the hit was from the object directory (immediately previous build)
        /// </summary>
        public bool ObjectDirectoryHit { get; set; }

        /// <summary>
        /// Whether the hit was from the object directory (immediately previous build)
        /// </summary>
        public GraphCacheMissReason ObjectDirectoryMissReason { get; set; }

        /// <summary>
        /// Reason for there being a miss from the object directory
        /// </summary>
        /// <remarks>
        /// TODO: This is only here as a way for the string to get to the log file. EventSource encodes enums as int
        /// which means the event message would not have the string description. If we move away from using event listeners
        /// for console and file logging, this will no longer be necessary.
        /// </remarks>
        public string ObjectDirectoryMissReasonAsString => ObjectDirectoryMissReason.ToString();

        /// <summary>
        /// Overall miss reason. This will always have a value, either coming from the ObjectDirectoryMissReason
        /// or the CacheMissReason, whichever is deemed most relevant
        /// </summary>
        public GraphCacheMissReason MissReason { get; set; }

        /// <summary>
        /// Detailed description for miss. Generally the name of the first changed spec file or environment variable
        /// </summary>
        public string MissDescription { get; set; }

        /// <summary>
        /// Reason for there being a miss from the cache
        /// </summary>
        /// <remarks>
        /// TODO: This is only here as a way for the string to get to the log file. EventSource encodes enums as int
        /// which means the event message would not have the string description. If we move away from using event listeners
        /// for console and file logging, this will no longer be necessary.
        /// </remarks>
        public GraphCacheMissReason CacheMissReason { get; set; }

        /// <summary>
        /// Reason for there being a miss from the cache
        /// </summary>
        public string CacheMissReasonAsString => CacheMissReason.ToString();

        /// <summary>
        /// The number of input files checked
        /// </summary>
        public int InputFilesChecked { get; set; }

        /// <inheritdoc/>
        public int ElapsedMilliseconds { get; set; }

        /// <summary>
        /// Whether this was a hit due to the build machine being a distributed build worker
        /// </summary>
        public bool WorkerHit { get; set; }

        /// <summary>
        /// Whether journal is enabled to detect changes
        /// </summary>
        public bool JournalEnabled { get; set; }

        /// <summary>
        /// Message describing the miss to be displayed on the console
        /// </summary>
        public string MissMessageForConsole
        {
            get
            {
                switch (MissReason)
                {
                    case GraphCacheMissReason.BuildEngineChanged:
                        return "Build engine changed from previous run.";
                    case GraphCacheMissReason.ConfigFileChanged:
                        return "Config file changed from previous run.";
                    case GraphCacheMissReason.QualifierChanged:
                        return "Qualifier changed from previous build.";
                    case GraphCacheMissReason.EvaluationFilterChanged:
                        return "Evaluation filter changed from previous build.";
                    case GraphCacheMissReason.FingerprintChanged:
                        return "Graph fingerprint changed from previous run.";
                    case GraphCacheMissReason.EnvironmentVariableChanged:
                        return "First environment variable changed from previous run: " + MissDescription;
                    case GraphCacheMissReason.MountChanged:
                        return "A mount definition has changed from previous run: " + MissDescription;
                    case GraphCacheMissReason.SpecFileChanges:
                        return "First file changed from previous run: " + MissDescription;
                    case GraphCacheMissReason.DirectoryChanged:
                        return "Detected changes in a directory where globbing was used: " + MissDescription;
                    case GraphCacheMissReason.GlobbingRequiresJournalEnabled:
                        return "Scanning change journal is required for graph caching of {ShortScriptName} based builds.";
                    case GraphCacheMissReason.GlobbingRequiresJournalScan:
                        return "Non-failed journal scan is required for graph caching of {ShortScriptName} based builds.";
                    case GraphCacheMissReason.NoPreviousRunToCheck:
                        return "Information from previous run could not be found. The object directory may have been cleaned.";
                    case GraphCacheMissReason.CheckFailed:
                        return "Previous run information was corrupt and could not be used.";
                    case GraphCacheMissReason.NotChecked:
                        return "Not checked";
                    case GraphCacheMissReason.NoFingerprintFromMaster:
                        return "Was not able to get a fingerprint from the master";
                    case GraphCacheMissReason.ForcedMiss:
                        return "Miss was forced for debugging via environment variable: " + InputTracker.ForceInvalidateCachedGraphVariable;
                    case GraphCacheMissReason.NotAllDirectoryEnumerationsAreAccounted:
                        return "Not all directory enumerations were accounted";
                    case GraphCacheMissReason.JournalScanFailed:
                        return "Journal scanning failed.";
                    case GraphCacheMissReason.CacheFailure:
                        return "Cache failure";
                    default:
                        Contract.Assume(MissReason == GraphCacheMissReason.NoMiss, "Unexpected value for MissReason: " + MissReason);
                        return "No Miss";
                }
            }
        }
    }

    /// <summary>
    /// The reason there was a graph cache miss
    /// </summary>
    /// <remarks>
    /// Items are ordered in the order checks are performed
    /// </remarks>
    public enum GraphCacheMissReason
    {
        /// <summary>
        /// No check was performed
        /// </summary>
        NotChecked = 0,

        /// <summary>
        /// There was no previous run to check against
        /// </summary>
        NoPreviousRunToCheck,

        /// <summary>
        /// Previous run information existed but it could not be checked due to an error
        /// </summary>
        CheckFailed,

        /// <summary>
        /// There was no miss
        /// </summary>
        NoMiss,

        /// <summary>
        /// The build engine changed
        /// </summary>
        BuildEngineChanged,

        /// <summary>
        /// A config file changed
        /// </summary>
        ConfigFileChanged,

        /// <summary>
        /// A qualifier changed
        /// </summary>
        QualifierChanged,

        /// <summary>
        /// Evaluation filter changed
        /// </summary>
        EvaluationFilterChanged,

        /// <summary>
        /// The graph fingerprint changed. Precise information about what component that caused the fingerprint to
        /// change is unknown
        /// </summary>
        FingerprintChanged,

        /// <summary>
        /// One or more environment variables changed
        /// </summary>
        EnvironmentVariableChanged,

        /// <summary>
        /// One or more mounts or mount paths changed
        /// </summary>
        MountChanged,

        /// <summary>
        /// One or more spec files changed
        /// </summary>
        SpecFileChanges,

        /// <summary>
        /// The worker machine failed to fetch a graph fingerprint from the master machine
        /// </summary>
        NoFingerprintFromMaster,

        /// <summary>
        /// A miss was forced. Used for debugging
        /// </summary>
        ForcedMiss,

        /// <summary>
        /// There is a change in the directory where globbing was used
        /// </summary>
        DirectoryChanged,

        /// <summary>
        /// Journal is disabled but detecting changes in the directories requires journal
        /// </summary>
        GlobbingRequiresJournalEnabled,

        /// <summary>
        /// Journal could not be scanned but detecting changes in the directories requires journal
        /// </summary>
        GlobbingRequiresJournalScan,

        /// <summary>
        /// Journal scanning failed.
        /// </summary>
        JournalScanFailed,

        /// <summary>
        /// Not all directory enumerations are accounted.
        /// </summary>
        NotAllDirectoryEnumerationsAreAccounted,

        /// <summary>
        /// Cache failure.
        /// </summary>
        CacheFailure
    }

    /// <summary>
    /// Statistics about reloading a cached graph
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct GraphCacheReloadStatistics : IHasEndTime
    {
        /// <summary>
        /// Whether reloading the cached graph was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Combined size of serialized files
        /// </summary>
        public long SerializedFileSizeBytes { get; set; }

        /// <inheritdoc/>
        public int ElapsedMilliseconds { get; set; }
    }

    /// <summary>
    /// Statistics about saving a cached graph
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct GraphCacheSaveStatistics : IHasEndTime
    {
        /// <summary>
        /// Whether saving the cached graph was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Combined size of serialized files
        /// </summary>
        public long SerializedFileSizeBytes { get; set; }

        /// <summary>
        /// The fingerprint that uniquely identifies the cached graph
        /// </summary>
        public string IdentifierFingerprint { get; set; }

        /// <summary>
        /// Time spent serializing structures
        /// </summary>
        public int SerializationMilliseconds { get; set; }

        /// <inheritdoc/>
        public int ElapsedMilliseconds { get; set; }
    }

    /// <summary>
    /// Represents the effective environment variables for a build
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct EffectiveEnvironmentVariables
    {
        /// <summary>
        /// The number of effective environment variables
        /// </summary>
        public int Count
        {
            get
            {
                return UsedCount + UnusedCount;
            }
        }

        /// <summary>
        /// The number of used effective environment variables
        /// </summary>
        public int UsedCount;

        /// <summary>
        /// The string representation of the used environment variables (a new line
        /// delimited list of {Key}={Value} entries.
        /// </summary>
        public string UsedVariables;

        /// <summary>
        /// The number of unused effective environment variables
        /// </summary>
        public int UnusedCount;

        /// <summary>
        /// The string representation of the unused environment variables (a new line
        /// delimited list of {Key}={Value} entries.
        /// </summary>
        public string UnusedVariables;

        /// <summary>
        /// Creates an effective environment variables from the given environment variables collection
        /// </summary>
        internal static EffectiveEnvironmentVariables Create(FrontEndEngineImplementation frontEndEngineAbstraction)
        {
            EffectiveEnvironmentVariables effectiveEnvVars = default;
            GetVariablesText(frontEndEngineAbstraction.ComputeEnvironmentVariablesImpactingBuild(), out effectiveEnvVars.UsedVariables, out effectiveEnvVars.UsedCount);
            GetVariablesText(frontEndEngineAbstraction.ComputeUnusedAllowedEnvironmentVariables(), out effectiveEnvVars.UnusedVariables, out effectiveEnvVars.UnusedCount);
            return effectiveEnvVars;
        }

        private static void GetVariablesText(IReadOnlyDictionary<string, string> environmentVariables, out string text, out int count)
        {
            count = 0;
            using (var pooledStringBuilder = Pools.StringBuilderPool.GetInstance())
            {
                var stringBuilder = pooledStringBuilder.Instance;
                foreach (var environmentVariable in environmentVariables)
                {
                    if (count != 0)
                    {
                        stringBuilder.AppendLine();
                    }

                    stringBuilder.AppendFormat("{0}={1}", environmentVariable.Key, environmentVariable.Value);
                    count++;
                }

                text = stringBuilder.ToString();
            }
        }
    }

    /// <summary>
    /// Mounts used by frontend.
    /// </summary>
    public struct EffectiveMounts
    {
        /// <summary>
        /// Number of used mounts.
        /// </summary>
        public int Count;

        /// <summary>
        /// Used mounts.
        /// </summary>
        public string UsedMountsText;

        internal static EffectiveMounts Create(FrontEndEngineImplementation frontEndEngineAbstraction)
        {
            EffectiveMounts effectiveMounts = default;
            GetText(frontEndEngineAbstraction.PathTable, frontEndEngineAbstraction.ComputeEffectiveMounts(), out effectiveMounts.UsedMountsText, out effectiveMounts.Count);
            return effectiveMounts;
        }

        private static void GetText(PathTable pathTable, IReadOnlyDictionary<string, IMount> usedMounts, out string text, out int count)
        {
            count = 0;
            using (var pooledStringBuilder = Pools.StringBuilderPool.GetInstance())
            {
                var stringBuilder = pooledStringBuilder.Instance;
                foreach (var usedMount in usedMounts)
                {
                    if (count != 0)
                    {
                        stringBuilder.AppendLine();
                    }

                    stringBuilder.AppendFormat("{0}={1}", usedMount.Key, usedMount.Value?.Path.ToString(pathTable));
                    count++;
                }

                text = stringBuilder.ToString();
            }
        }
    }

    /// <summary>
    /// Represents an event forwarded from a worker
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct WorkerForwardedEvent
    {
        /// <summary>
        /// The worker name
        /// </summary>
        public string WorkerName;

        /// <summary>
        /// The message of the worker event
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// The ID of the original event
        /// </summary>
        public int EventId { get; set; }

        /// <summary>
        /// The name of the original event
        /// </summary>
        public string EventName { get; set; }

        /// <summary>
        /// The keywords of the original event
        /// </summary>
        public long EventKeywords { get; set; }
    }

    /// <summary>
    /// Provenance information about machine for rpc call
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct RpcMachineData
    {
        /// <summary>
        /// The machine name
        /// </summary>
        public string MachineName;

        /// <summary>
        /// The machine id
        /// </summary>
        public string MachineId;

        /// <summary>
        /// The build id
        /// </summary>
        public string BuildId;

        /// <inheritdoc />
        public override string ToString()
        {
            if (MachineId == null)
            {
                return MachineName;
            }

            return I($"{MachineName} [{MachineId}] [{BuildId}]");
        }
    }

    /// <summary>
    /// Represents an data for event for non-deterministic pip result
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct NonDeterministicPipResultData
    {
        /// <summary>
        /// The semi stable hash of the pip
        /// </summary>
        public long PipSemiStableHash { get; set; }

        /// <summary>
        /// The description of the pip
        /// </summary>
        public string PipDescription { get; set; }

        /// <summary>
        /// The worker id of the first worker to execute the pip
        /// </summary>
        public string WorkerDescription1;

        /// <summary>
        /// The worker description of the second worker to execute the pip
        /// </summary>
        public string WorkerDescription2;

        /// <summary>
        /// The pip result on the first worker
        /// </summary>
        public string Result1 { get; set; }

        /// <summary>
        /// The pip result on the second worker
        /// </summary>
        public string Result2 { get; set; }
    }

    /// <summary>
    /// Represents an data for event for non-deterministic pip output
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct NonDeterministicPipOutputData
    {
        /// <summary>
        /// The semi stable hash of the pip
        /// </summary>
        public long PipSemiStableHash { get; set; }

        /// <summary>
        /// The description of the pip
        /// </summary>
        public string PipDescription { get; set; }

        /// <summary>
        /// The worker id of the first worker to execute the pip
        /// </summary>
        public string WorkerDescription1;

        /// <summary>
        /// The worker description of the second worker to execute the pip
        /// </summary>
        public string WorkerDescription2;

        /// <summary>
        /// The path to the output
        /// </summary>
        public string OutputPath { get; set; }

        /// <summary>
        /// The hash of the produced file on the first worker
        /// </summary>
        public string OutputHash1 { get; set; }

        /// <summary>
        /// The hash of the produced file on the second worker
        /// </summary>
        public string OutputHash2 { get; set; }
    }
}
