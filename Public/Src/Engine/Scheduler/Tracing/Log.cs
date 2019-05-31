// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Events.Keywords))]
    [EventTasksType(typeof(Events.Tasks))]
    public abstract partial class Logger : LoggerBase
    {
        private bool m_preserveLogEvents;

        private readonly ConcurrentQueue<Diagnostic> m_capturedDiagnostics = new ConcurrentQueue<Diagnostic>();

        internal Logger()
        {
        }

        /// <summary>
        /// Factory method that creates instances of the logger.
        /// </summary>
        /// <param name="preserveLogEvents">When specified all logged events would be stored in the internal data structure.</param>
        public static Logger CreateLogger(bool preserveLogEvents = false)
        {
            return new LoggerImpl() { m_preserveLogEvents = preserveLogEvents };
        }

        /// <summary>
        /// Provides diagnostics captured by the logger.
        /// Would be non-empty only when preserveLogEvents flag was specified in the <see cref="Logger.CreateLogger" /> factory method.
        /// </summary>
        public IReadOnlyList<Diagnostic> CapturedDiagnostics => m_capturedDiagnostics.ToList();

        /// <inheritdoc />
        public override bool InspectMessageEnabled => m_preserveLogEvents;

        /// <inheritdoc />
        protected override void InspectMessage(int logEventId, EventLevel level, string message, Location? location = null)
        {
            m_capturedDiagnostics.Enqueue(new Diagnostic(logEventId, level, message, location));
        }

        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        /// <summary>
        /// Prefix used to indicate dependency analysis results specific to a pip.
        /// </summary>
        /// <remarks>
        /// Why this extra prefix? Text filtering. There's a corresponding ETW keyword, but today people lean mostly on the text logs.
        /// </remarks>
        public const string PipDependencyAnalysisPrefix = "Detected dependency violation: [{1}] ";

        /// <summary>
        /// Prefix used to indicate dependency analysis results specific to a pip, the spec file that generated it, and the working directory
        /// </summary>
        /// <remarks>
        /// Why this extra prefix? Text filtering. There's a corresponding ETW keyword, but today people lean mostly on the text logs.
        /// </remarks>
        public const string PipSpecDependencyAnalysisPrefix = "Detected dependency violation: [{1}, {2}, {3}] ";
        
        private const string AbsentPathProbeUnderOpaqueDirectoryMessage = "Absent path probe under opaque directory: This pip probed path '{2}' that does not exist. The path is under an output directory that the pip does not depend on. " +
                           "The probe is not guaranteed to always be absent and may introduce non-deterministic behaviors in the build if the pip is incrementally skipped. " +
                           "Please declare an explicit dependency between this pip and the producer of the output directory so the probe always happens after the directory is finalized. ";

        #region PipExecutor

        internal void PipWriteFileFailed(LoggingContext loggingContext, string pipDescription, string path, BuildXLException ex)
        {
            PipWriteFileFailed(loggingContext, pipDescription, path, ex.LogEventErrorCode, ex.LogEventMessage);
        }

        internal static string PipWriteFileFailedMessage(string pipDescription, string path, BuildXLException ex)
        {
            return I($"[{pipDescription}] Write file '{path}' failed with error code {ex.LogEventErrorCode:X8}: {ex.LogEventMessage}");
        }

        [GeneratedEvent(
            (ushort)EventId.PipWriteFileFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Write file '{path}' failed with error code {errorCode:X8}: {message}")]
        internal abstract void PipWriteFileFailed(LoggingContext loggingContext, string pipDescription, string path, int errorCode, string message);

        internal void PipCopyFileFailed(
            LoggingContext loggingContext,
            string pipDescription,
            string source,
            string destination,
            BuildXLException ex)
        {
            PipCopyFileFailed(loggingContext, pipDescription, source, destination, ex.LogEventErrorCode, ex.LogEventMessage);
        }

        [GeneratedEvent(
            (ushort)EventId.PipCopyFileFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Copy file '{source}' to '{destination}' failed with error code {errorCode:X8}: {message}")]
        internal abstract void PipCopyFileFailed(
            LoggingContext loggingContext,
            string pipDescription,
            string source,
            string destination,
            int errorCode,
            string message);

        [GeneratedEvent(
            (ushort)EventId.PipIpcFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "IPC operation '{operation}' could not be executed via IPC moniker '{moniker}'.  Reason: {reason}. Error: {message}")]
        internal abstract void PipIpcFailed(
            LoggingContext loggingContext,
            string operation,
            string moniker,
            string reason,
            string message);

        [GeneratedEvent(
            (ushort)EventId.PipIpcFailedDueToInvalidInput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "IPC operation '{operation}' could not be executed via IPC moniker '{moniker}'.  IPC operation input is invalid. Error: {message}")]
        internal abstract void PipIpcFailedDueToInvalidInput(
            LoggingContext loggingContext,
            string operation,
            string moniker,
            string message);

        [GeneratedEvent(
            (ushort)EventId.PipCopyFileFromUntrackableDir,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Copy file '{source}' to '{destination}' failed because the source file is under a mountpoint that is configured with 'TrackSourceFileChanges == false'")]
        internal abstract void PipCopyFileFromUntrackableDir(
            LoggingContext loggingContext,
            string pipDescription,
            string source,
            string destination);

        [GeneratedEvent(
            (ushort)EventId.PipCopyFileSourceFileDoesNotExist,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Copy file '{source}' to '{destination}' failed because '{source}' does not exist")]
        internal abstract void PipCopyFileSourceFileDoesNotExist(
            LoggingContext loggingContext,
            string pipDescription,
            string source,
            string destination);

        [GeneratedEvent(
            (ushort)EventId.StorageCachePutContentFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Putting '{path}' into the cache, resulted in error: {errorMessage}")]
        internal abstract void StorageCachePutContentFailed(LoggingContext loggingContext, string path, string errorMessage);

        [GeneratedEvent(
            (ushort)EventId.StorageTrackOutputFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Tracking output '{path}' resulted in error: {errorMessage}")]
        internal abstract void StorageTrackOutputFailed(LoggingContext loggingContext, string path, string errorMessage);

        [GeneratedEvent(
            (ushort)EventId.PipOutputProduced,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Produced output '{fileName}' hash: '{contentHash}'. {reparsePointInfo}.")]
        internal abstract void SchedulePipOutputProduced(
            LoggingContext loggingContext,
            string pipDescription,
            string fileName,
            string contentHash,
            string reparsePointInfo);

        [GeneratedEvent(
            (ushort)EventId.PipOutputUpToDate,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Pip output for '{fileName}' is already up to date. (hash: '{contentHash}'). {reparsePointInfo}.")]
        internal abstract void SchedulePipOutputUpToDate(
            LoggingContext loggingContext,
            string pipDescription,
            string fileName,
            string contentHash,
            string reparsePointInfo);

        [GeneratedEvent(
            (ushort)EventId.PipOutputNotMaterialized,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Pip output for '{fileName}' is not materialized (hash: '{contentHash}'). {reparsePointInfo}.")]
        internal abstract void SchedulePipOutputNotMaterialized(
            LoggingContext loggingContext,
            string pipDescription,
            string fileName,
            string contentHash,
            string reparsePointInfo);

        [GeneratedEvent(
            (ushort)EventId.PipOutputDeployedFromCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Deploying cached pip output to '{fileName}' (hash: '{contentHash}'). {reparsePointInfo}.")]
        internal abstract void SchedulePipOutputDeployedFromCache(
            LoggingContext loggingContext,
            string pipDescription,
            string fileName,
            string contentHash,
            string reparsePointInfo);
        
        [GeneratedEvent(
            (ushort)EventId.PipWarningsFromCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Found cached warnings: {numberOfWarnings}")]
        internal abstract void PipWarningsFromCache(LoggingContext loggingContext, string pipDescription, int numberOfWarnings);

        [GeneratedEvent(
            (ushort)EventId.ProcessPipCacheMiss,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "[{pipDescription}] Cache miss (fingerprint '{fingerprint}'): Process will be executed.")]
        internal abstract void ScheduleProcessPipCacheMiss(LoggingContext loggingContext, string pipDescription, string fingerprint);

        [GeneratedEvent(
            (ushort)EventId.ProcessPipProcessWeight,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "[{pipDescription}] Executing process with process weight: {weight}.")]
        internal abstract void ProcessPipProcessWeight(LoggingContext loggingContext, string pipDescription, int weight);

        [GeneratedEvent(
            (ushort)EventId.ProcessPipCacheHit,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "[{pipDescription}] Cache hit (fingerprint '{fingerprint}'; unique ID {uniqueId:X}): Process outputs will be deployed from cache.")]
        internal abstract void ScheduleProcessPipCacheHit(LoggingContext loggingContext, string pipDescription, string fingerprint, ulong uniqueId);

        [GeneratedEvent(
            (ushort)EventId.PipFailedDueToServicesFailedToRun,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Pip failed to execute because its requested services could not be started.")]
        internal abstract void PipFailedDueToServicesFailedToRun(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.PipMaterializeDependenciesFailureUnrelatedToCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Failed to materialize pip dependencies for reason unrelated to cache. Materialization result: {artifactMaterializationResult}, Error: {errorMessage}")]
        internal abstract void PipMaterializeDependenciesFailureUnrelatedToCache(LoggingContext loggingContext, string pipDescription, string artifactMaterializationResult, string errorMessage);

        [GeneratedEvent(
            (ushort)EventId.PipMaterializeDependenciesFromCacheFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Failed to materialize pip dependencies content from cache: {errorMessage}")]
        internal abstract void PipMaterializeDependenciesFromCacheFailure(LoggingContext loggingContext, string pipDescription, string errorMessage);

        [GeneratedEvent(
            (ushort)LogEventId.PipFailedDueToDependenciesCannotBeHashed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Pip failed to execute because its dependencies cannot be hashed.")]
        internal abstract void PipFailedDueToDependenciesCannotBeHashed(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)LogEventId.PipFailedDueToSourceDependenciesCannotBeHashed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Pip failed to execute because its source dependencies cannot be hashed.")]
        internal abstract void PipFailedDueToSourceDependenciesCannotBeHashed(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)LogEventId.PipFailedDueToOutputsCannotBeHashed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Pip failed to execute because its outputs cannot be hashed.")]
        internal abstract void PipFailedDueToOutputsCannotBeHashed(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)LogEventId.PipIsMarkedClean,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Pip is marked as clean.")]
        internal abstract void PipIsMarkedClean(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)LogEventId.PipIsIncrementallySkippedDueToCleanMaterialized,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Pip is incrementally skipped because it is marked as clean and materialized.")]
        internal abstract void PipIsIncrementallySkippedDueToCleanMaterialized(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)LogEventId.PipIsMarkedMaterialized,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Pip is marked as materialized because it has materialized its outputs.")]
        internal abstract void PipIsMarkedMaterialized(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)LogEventId.PipIsPerpetuallyDirty,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Pip is perpetually dirty.")]
        internal abstract void PipIsPerpetuallyDirty(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)LogEventId.PipFingerprintData,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "Pip Fingerprint Version: '{fingerprintVersion}', Salt: '{fingerprintSalt}'")]
        internal abstract void PipFingerprintData(LoggingContext loggingContext, int fingerprintVersion, string fingerprintSalt);

        [GeneratedEvent(
            (ushort)EventId.StorageCacheIngressFallbackContentToMakePrivateError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Failed to copy the content with hash {contentHash} (from '{fallbackPath}') into the build cache. This is needed in order to provide a private, writable copy at the same location. Error: {errorMessage}")]
        internal abstract void StorageCacheIngressFallbackContentToMakePrivateError(LoggingContext loggingContext, string contentHash, string fallbackPath, string errorMessage);
        
        [GeneratedEvent(
            (ushort)EventId.ProcessDescendantOfUncacheable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Depends on pip a pip which had file monitoring violations that made it uncacheable.")]
        internal abstract void ProcessDescendantOfUncacheable(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Pip completed successfully, but with file monitoring violations. It will not be stored to the cache, since its declared inputs or outputs may be inaccurate.")]
        internal abstract void ScheduleProcessNotStoredToCacheDueToFileMonitoringViolations(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)LogEventId.ScheduleProcessNotStoredToWarningsUnderWarnAsError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Pip completed with warnings which were flagged as errors due to /warnaserror. It will not be stored to the cache, but downstream pips will continue to be executed.")]
        internal abstract void ScheduleProcessNotStoredToWarningsUnderWarnAsError(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.ProcessNotStoredToCachedDueToItsInherentUncacheability,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Pip completed successfully, but will not be stored to the cache, since it was explicitly declared as uncacheable.")]
        internal abstract void ScheduleProcessNotStoredToCacheDueToInherentUncacheability(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.ContentMissAfterContentFingerprintCacheDescriptorHit,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Matching content was not found for all hashes in the pip cache descriptor for content fingerprint '{contentFingerprint}' (unique ID: {uniqueId:X}). The descriptor must be ignored.")]
        internal abstract void ScheduleContentMissAfterContentFingerprintCacheDescriptorHit(LoggingContext loggingContext, string pipDescription, string contentFingerprint, ulong uniqueId);

        [GeneratedEvent(
            (ushort)EventId.PipFailedToMaterializeItsOutputs,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "[{pipDescription}] Pip failed to materialize its outputs: {errorMessage}")]
        internal abstract void PipFailedToMaterializeItsOutputs(LoggingContext loggingContext, string pipDescription, string errorMessage);

        [GeneratedEvent(
            (ushort)EventId.ScheduleArtificialCacheMiss,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Pip will execute due to an artificial cache miss (cache lookup skipped).")]
        internal abstract void ScheduleArtificialCacheMiss(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.ScheduleProcessConfiguredUncacheable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Pip configured to be uncacheable. No cache lookup will be performed.")]
        internal abstract void ScheduleProcessConfiguredUncacheable(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.CacheDescriptorMissForContentFingerprint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Weak fingerprint miss: A pip cache descriptor was not found for content fingerprint '{contentFingerprint}'.")]
        internal abstract void TwoPhaseCacheDescriptorMissDueToWeakFingerprint(LoggingContext loggingContext, string pipDescription, string contentFingerprint);

        [GeneratedEvent(
            (ushort)EventId.InvalidCacheDescriptorForContentFingerprint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] The pip cache descriptor for content fingerprint '{contentFingerprint}' from cache depth {cacheDepth} was invalid and so must be ignored. {error}")]
        internal abstract void ScheduleInvalidCacheDescriptorForContentFingerprint(LoggingContext loggingContext, string pipDescription, string contentFingerprint, int cacheDepth, string error);

        [GeneratedEvent(
            (ushort)EventId.CacheDescriptorHitForContentFingerprint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] A pip cache descriptor was found for content fingerprint '{contentFingerprint}' (unique ID: {uniqueId:X}) from cache depth {cacheDepth}, indicating that an equivalent pip previously ran with these inputs.")]
        internal abstract void ScheduleCacheDescriptorHitForContentFingerprint(LoggingContext loggingContext, string pipDescription, string contentFingerprint, ulong uniqueId, int cacheDepth);
        
        [GeneratedEvent(
            (ushort)EventId.DisallowedFileAccessInSealedDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] When accessing files under a sealed directory, a pip must declare a dependency on one or more views of that directory (partial or full) that contain those files. " +
                      "Although this pip contains a dependency on a view of a containing directory, it accessed the following existent file that is not a part of it: '{path}'. ")]
        internal abstract void ScheduleDisallowedFileAccessInSealedDirectory(LoggingContext loggingContext, string pipDescription, string path);

        [GeneratedEvent(
            (ushort)EventId.DisallowedFileAccessInTopOnlySourceSealedDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] This pip accessed file under '{path}' nested deeply within a top only source sealed directory.")]
        internal abstract void DisallowedFileAccessInTopOnlySourceSealedDirectory(LoggingContext loggingContext, string pipDescription, string path);

        [GeneratedEvent(
            (ushort)EventId.PipInputAssertion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.PipInputAssertions,
            Message = "[{pipDescription}] Pip input assertion for content {contentHash} at path {inputAssersionPath}")]
        internal abstract void TracePipInputAssertion(
            LoggingContext loggingContext,
            string pipDescription,
            string inputAssersionPath,
            string contentHash);

        [GeneratedEvent(
            (ushort)LogEventId.AbortObservedInputProcessorBecauseFileUntracked,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Processing observed input is aborted because failure in computing the hash of '{path}'. The file is possibly untracked and under mount '{mount}' with hashing disabled.")]
        internal abstract void AbortObservedInputProcessorBecauseFileUntracked(LoggingContext loggingContext, string pipDescription, string path, string mount);

        [GeneratedEvent(
            (ushort)EventId.FileAccessCheckProbeFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Access to the path '{path}' would be allowed so long as that path is nonexistent or is a directory. However, the existence and type of that path could not be determined: {error}")]
        internal abstract void ScheduleFileAccessCheckProbeFailed(LoggingContext loggingContext, string pipDescription, string path, string error);

        [GeneratedEvent(
            (ushort)EventId.PipDirectoryMembershipAssertion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.PipInputAssertions,
            Message = "[{pipDescription}] Pip input assertion for directory membership (fingerprint {fingerprint}) at path {inputAssersionPath}")]
        internal abstract void PipDirectoryMembershipAssertion(
            LoggingContext loggingContext,
            string pipDescription,
            string inputAssersionPath,
            string fingerprint);

        [GeneratedEvent(
            (ushort)EventId.PipDirectoryMembershipFingerprintingError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Computing a fingerprint for the membership of directory '{path}' failed. A fingerprint for this directory is needed to store or use a cached result for this process.")]
        internal abstract void PipDirectoryMembershipFingerprintingError(LoggingContext loggingContext, string pipDescription, string path);

        [GeneratedEvent(
            (ushort)EventId.TryBringContentToLocalCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics | (int)Events.Keywords.Performance,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "[{pipDescription}] Try bring content to local cache.")]
        internal abstract void ScheduleTryBringContentToLocalCache(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.ProcessingPipOutputFileFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Failed to process output file '{path}'. {message}")]
        internal abstract void ProcessingPipOutputFileFailed(LoggingContext loggingContext, string pipDescription, string path, string message);

        [GeneratedEvent(
            (ushort)EventId.ProcessingPipOutputDirectoryFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Failed to process output directory '{path}'. {message}")]
        internal abstract void ProcessingPipOutputDirectoryFailed(LoggingContext loggingContext, string pipDescription, string path, string message);

        [GeneratedEvent(
            (ushort)LogEventId.StorageCacheCleanDirectoryOutputError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Cleaning output directory '{destinationPath}' for pip {pipDescription} resulted in error: {errorMessage}")]
        public abstract void StorageCacheCleanDirectoryOutputError(LoggingContext loggingContext, string pipDescription, string destinationPath, string errorMessage);

        [GeneratedEvent(
            (ushort)LogEventId.StorageSymlinkDirInOutputDirectoryWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "[{pipDescription}] Pip produced a directory symlink or junction'{symlinkPath}', which is not supported. The pip will not be cached.")]
        public abstract void StorageSymlinkDirInOutputDirectoryWarning(LoggingContext loggingContext, string pipDescription, string symlinkPath);

        [GeneratedEvent(
            (ushort)LogEventId.StorageRemoveAbsentFileOutputWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "[{pipDescription}] Removing absent file '{destinationPath}' resulted in error: {errorMessage}")]
        public abstract void StorageRemoveAbsentFileOutputWarning(LoggingContext loggingContext, string pipDescription, string destinationPath, string errorMessage);

        [GeneratedEvent(
             (ushort)LogEventId.PipInputVerificationMismatch,
             EventGenerators = EventGenerators.LocalOnly,
             Message = "Pip input '{filePath}' has hash '{actualHash}' which does not match expected hash '{expectedHash}' from master. Ensure that source files are properly replicated from the master.",
             EventLevel = Level.Error,
             EventTask = (ushort)Events.Tasks.Distribution,
             Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.InfrastructureError))]
        public abstract void PipInputVerificationMismatch(LoggingContext context, string actualHash, string expectedHash, string filePath);

        [GeneratedEvent(
            (ushort)LogEventId.PipInputVerificationMismatchExpectedExistence,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Pip input '{filePath}' not found locally, but exists on the master. Ensure that source files are properly replicated from the master.",
            EventLevel = Level.Error,
            EventTask = (ushort)Events.Tasks.Distribution,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.InfrastructureError))]
        public abstract void PipInputVerificationMismatchExpectedExistence(LoggingContext context, string filePath);

        [GeneratedEvent(
            (ushort)LogEventId.PipInputVerificationMismatchExpectedNonExistence,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Pip input '{filePath}' found locally, but does NOT exist on the master. Ensure that old files are cleaned up and source files are properly replicated from the master.",
            EventLevel = Level.Error,
            EventTask = (ushort)Events.Tasks.Distribution,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.InfrastructureError))]
        public abstract void PipInputVerificationMismatchExpectedNonExistence(LoggingContext context, string filePath);

        [GeneratedEvent(
            (ushort)LogEventId.PipInputVerificationUntrackedInput,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{pipDescription}] Pip input '{filePath}' is not tracked and cannot be verified on the worker.",
            EventLevel = Level.Warning,
            EventTask = (ushort)Events.Tasks.Distribution,
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void PipInputVerificationUntrackedInput(LoggingContext context, long pipSemiStableHash, string pipDescription, string filePath);

        [GeneratedEvent(
            (ushort)LogEventId.PipInputVerificationMismatchRecovery,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{pipDescription}] Pip input '{filePath}' has hash '{actualHash}' which does not match expected hash '{expectedHash}' from master. Attempting to materialize file from cache.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Distribution,
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void PipInputVerificationMismatchRecovery(LoggingContext context, long pipSemiStableHash, string pipDescription, string actualHash, string expectedHash, string filePath);

        [GeneratedEvent(
            (ushort)LogEventId.PipInputVerificationMismatchRecoveryExpectedExistence,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{pipDescription}] Pip input '{filePath}' not found locally, but exists on the master. Attempting to materialize file from cache.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Distribution,
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void PipInputVerificationMismatchRecoveryExpectedExistence(LoggingContext context, long pipSemiStableHash, string pipDescription, string filePath);

        [GeneratedEvent(
            (ushort)LogEventId.PipInputVerificationMismatchRecoveryExpectedNonExistence,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{pipDescription}] Pip input '{filePath}' found locally, but does NOT exist on the master. File will be deleted.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Distribution,
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void PipInputVerificationMismatchRecoveryExpectedNonExistence(LoggingContext context, long pipSemiStableHash, string pipDescription, string filePath);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionExecutePipRequest,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{pipDescription}] Requesting pip execution of step {step} on worker {workerName}",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Distribution,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void DistributionExecutePipRequest(LoggingContext context, long pipSemiStableHash, string pipDescription, string workerName, string step);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionFinishedPipRequest,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{pipDescription}] Finished pip execution of step {step} on worker {workerName}",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Distribution,
            EventOpcode = (byte)EventOpcode.Info,
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void DistributionFinishedPipRequest(LoggingContext context, long pipSemiStableHash, string pipDescription, string workerName, string step);

        [GeneratedEvent(
            (ushort)LogEventId.DistributionMasterWorkerProcessOutputContent,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "[{pipDescription}] Pip output '{filePath}' with hash '{hash} reported from worker '{workerName}'. {reparsePointInfo}.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Distribution,
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void DistributionMasterWorkerProcessOutputContent(LoggingContext context, long pipSemiStableHash, string pipDescription, string filePath, string hash, string reparsePointInfo, string workerName);

        [GeneratedEvent(
            (ushort)EventId.StorageCacheGetContentError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Placing the content with hash {contentHash} to '{destinationPath}' resulted in error: {errorMessage}")]
        public abstract void StorageCacheGetContentError(LoggingContext loggingContext, string contentHash, string destinationPath, string errorMessage);

        [GeneratedEvent(
            (ushort)EventId.StorageCacheGetContentWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "[{pipDescription}] Placing the content with hash {contentHash} to '{destinationPath}' resulted in error: {errorMessage}")]
        public abstract void StorageCacheGetContentWarning(LoggingContext loggingContext, string pipDescription, string contentHash, string destinationPath, string errorMessage);

        [GeneratedEvent(
            (ushort)EventId.CopyingPipOutputToLocalStorage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.Performance,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "[{pipDescription}] Ensured pip output (hash: '{contentHash}') is available for local materialization: Result: {result} | Target location up-to-date: {targetLocationUpToDate} | Remotely copied bytes: {remotelyCopyBytes}")]
        public abstract void ScheduleCopyingPipOutputToLocalStorage(
            LoggingContext loggingContext,
            string pipDescription,
            string contentHash,
            bool result,
            string targetLocationUpToDate,
            long remotelyCopyBytes);

        [GeneratedEvent(
            (int)EventId.CopyingPipInputToLocalStorage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.Performance,
            EventTask = (int)Events.Tasks.Storage,
            Message = "[{pipDescription}] Ensured pip input (hash: '{contentHash}') is available for local materialization: Result: {result} | Target location up-to-date: {targetLocationUpToDate} | Remotely copied bytes: {remotelyCopyBytes}")]
        public abstract void ScheduleCopyingPipInputToLocalStorage(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string contentHash,
            bool result,
            string targetLocationUpToDate,
            long remotelyCopyBytes);

        [GeneratedEvent(
            (ushort)EventId.StorageBringProcessContentLocalWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "[{pipDescription}] An unexpected failure occurred in retrieving content for prior process outputs (the process cannot be completed from cache): {errorMessage}")]
        public abstract void StorageBringProcessContentLocalWarning(LoggingContext loggingContext, string pipDescription, string errorMessage);

        [GeneratedEvent(
            (ushort)EventId.FailedToMaterializeFileWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "[{pipDescription}] Failed to pin file content with hash {contentHash} and intended destination '{destinationPath}'. Search for content hash in cache logging.")]
        public abstract void FailedToLoadFileContentWarning(LoggingContext loggingContext, string pipDescription, string contentHash, string destinationPath);

        [GeneratedEvent(
            (ushort)EventId.MaterializeFilePipProducerNotFound,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Failed to find pip producer for file {filePath}.")]
        public abstract void MaterializeFilePipProducerNotFound(LoggingContext loggingContext, string filePath);

        #endregion

        #region Two-phase fingerprinting

        [GeneratedEvent(
            (int)EventId.TwoPhaseFailureQueryingWeakFingerprint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Querying for a batch of prior executions (for weak fingerprint {weakFingerprint}) failed: {errorMessage}. Since some cached results may be unavailable, this process may have to re-run.")]
        internal abstract void TwoPhaseFailureQueryingWeakFingerprint(LoggingContext loggingContext, string pipDescription, string weakFingerprint, string errorMessage);

        [GeneratedEvent(
            (int)EventId.TwoPhaseCacheDescriptorMissDueToStrongFingerprints,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Strong fingerprint miss: One or more pip cache descriptor were found for weak fingerprint '{contentFingerprint}'; however, no available strong fingerprints matched.")]
        internal abstract void TwoPhaseCacheDescriptorMissDueToStrongFingerprints(LoggingContext loggingContext, string pipDescription, string contentFingerprint);

        [GeneratedEvent(
            (int)EventId.TwoPhaseStrongFingerprintComputedForPathSet,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Computed strong fingerprint {strongFingerprint} for path set {pathSetHash} and weak fingerprint {weakFingerprint}.")]
        internal abstract void TwoPhaseStrongFingerprintComputedForPathSet(LoggingContext loggingContext, string pipDescription, string weakFingerprint, string pathSetHash, string strongFingerprint);

        [GeneratedEvent(
            (int)EventId.TwoPhaseStrongFingerprintMatched,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] A prior cache entry has been found for strong fingerprint {strongFingerprint} in cache {strongFingerprintCacheId}")]
        internal abstract void TwoPhaseStrongFingerprintMatched(LoggingContext loggingContext, string pipDescription, string strongFingerprint, string strongFingerprintCacheId);

        [GeneratedEvent(
            (int)EventId.TwoPhaseStrongFingerprintRejected,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Rejecting a prior cache entry for path set {pathSetHash}: Entry strong fingerprint {rejectedStrongFingerprint} does not match {availableStrongFingerprint}")]
        internal abstract void TwoPhaseStrongFingerprintRejected(LoggingContext loggingContext, string pipDescription, string pathSetHash, string rejectedStrongFingerprint, string availableStrongFingerprint);

        [GeneratedEvent(
            (int)EventId.TwoPhaseStrongFingerprintUnavailableForPathSet,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Unable to compute a strong fingerprint for path set {pathSetHash} and weak fingerprint {weakFingerprint} (maybe this pip is no longer allowed to access some of the mentioned paths).")]
        internal abstract void TwoPhaseStrongFingerprintUnavailableForPathSet(LoggingContext loggingContext, string pipDescription, string weakFingerprint, string pathSetHash);

        [GeneratedEvent(
            (int)EventId.TwoPhaseCacheEntryMissing,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] The cache entry for strong fingerprint {strongFingerprint} could not be found, but the cache listed it as available for weak fingerprint {weakFingerprint}. " +
                      "This is an unexpected cache inconsistency, and will result in a cache-miss for this pip.")]
        internal abstract void TwoPhaseCacheEntryMissing(LoggingContext loggingContext, string pipDescription, string weakFingerprint, string strongFingerprint);

        [GeneratedEvent(
            (int)EventId.TwoPhaseFetchingCacheEntryFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Failed to retrieve the cache entry for strong fingerprint {strongFingerprint}. This is an unexpected cache inconsistency, and will result in a cache-miss for this pip. Failure: {failure}")]
        internal abstract void TwoPhaseFetchingCacheEntryFailed(LoggingContext loggingContext, string pipDescription, string strongFingerprint, string failure);

        [GeneratedEvent(
            (int)EventId.TwoPhaseMissingMetadataForCacheEntry,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] The cache entry for strong fingerprint {strongFingerprint} has missing metadata (content hash {metadataHash}). " +
                      "This is an unexpected cache inconsistency, and will result in a cache-miss for this pip.")]
        internal abstract void TwoPhaseMissingMetadataForCacheEntry(LoggingContext loggingContext, string pipDescription, string strongFingerprint, string metadataHash);

        [GeneratedEvent(
            (int)EventId.TwoPhaseFetchingMetadataForCacheEntryFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Failed to retrieve metadata (content hash {metadataHash}) for the cache entry with strong fingerprint {strongFingerprint}. This is an unexpected cache inconsistency, and will result in a cache-miss for this pip. Failure: {failure}")]
        internal abstract void TwoPhaseFetchingMetadataForCacheEntryFailed(LoggingContext loggingContext, string pipDescription, string strongFingerprint, string metadataHash, string failure);

        [GeneratedEvent(
            (int)EventId.TwoPhaseLoadingPathSetFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Failed to retrieve a path set (content hash {pathSetHash}) relevant to this pip (weak fingerprint {weakFingerprint}). This is an unexpected cache inconsistency. Failure: {failure}")]
        internal abstract void TwoPhaseLoadingPathSetFailed(LoggingContext loggingContext, string pipDescription, string weakFingerprint, string pathSetHash, string failure);

        [GeneratedEvent(
            (int)EventId.TwoPhasePathSetInvalid,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Failed to parse a prior path set (content hash {pathSetHash}) relevant to this pip (weak fingerprint {weakFingerprint}). This may result in a cache-miss for this pip.  Failure: {failure}")]
        internal abstract void TwoPhasePathSetInvalid(LoggingContext loggingContext, string pipDescription, string weakFingerprint, string pathSetHash, string failure);

        [GeneratedEvent(
            (int)EventId.TwoPhasePublishingCacheEntryFailedWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Failed to publish a cache entry for this pip's execution. Failure: {failure} Caching info: {cachingInfo}")]
        internal abstract void TwoPhasePublishingCacheEntryFailedWarning(LoggingContext loggingContext, string pipDescription, string failure, string cachingInfo);

        [GeneratedEvent(
            (int)EventId.TwoPhasePublishingCacheEntryFailedError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Failed to publish a cache entry for this pip's execution. Failure: {failure} Caching info: {cachingInfo}")]
        internal abstract void TwoPhasePublishingCacheEntryFailedError(LoggingContext loggingContext, string pipDescription, string failure, string cachingInfo);

        [GeneratedEvent(
            (int)EventId.ConvertToRunnableFromCacheFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Failed creating a runnable from cache entry: {cacheMissType}. Note that subsequent pips may now be divergent from the cache (but the next build will reconverge).")]
        internal abstract void ConvertToRunnableFromCacheFailed(LoggingContext loggingContext, string pipDescription, string cacheMissType);

        [GeneratedEvent(
            (int)EventId.TwoPhaseCacheEntryConflict,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] While trying to store a cache entry for this pip's execution, the cache indicated that a conflicting entry already exists (strong fingerprint: {strongFingerprint}). " +
                      "This may occur if a concurrent build is storing entries to the cache and won the race of placing the content")]
        internal abstract void TwoPhaseCacheEntryConflict(LoggingContext loggingContext, string pipDescription, string strongFingerprint);

        [GeneratedEvent(
            (int)EventId.TwoPhaseFailedToStoreMetadataForCacheEntry,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Failed to store supporting metadata for a cache entry. Failure: {failure}")]
        internal abstract void TwoPhaseFailedToStoreMetadataForCacheEntry(LoggingContext loggingContext, string pipDescription, string failure);

        [GeneratedEvent(
            (int)EventId.TwoPhaseCacheEntryPublished,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Stored a new cache entry for strong fingerprint {strongFingerprint} (reachable via weak fingerprint {weakFingerprint} and path-set {pathSetHash}).")]
        internal abstract void TwoPhaseCacheEntryPublished(LoggingContext loggingContext, string pipDescription, string weakFingerprint, string pathSetHash, string strongFingerprint);

        [GeneratedEvent(
            (ushort)EventId.CacheFingerprintHitSources,
            EventGenerators = EventGenerators.TelemetryOnly | Generators.Statistics,
            Message = "Cache Fingerprint Hit Sources")]
        public abstract void CacheFingerprintHitSources(LoggingContext context, IDictionary<string, int> entryMatches);

        [GeneratedEvent(
            (ushort)EventId.StorageCacheContentHitSources,
            EventGenerators = EventGenerators.TelemetryOnly | Generators.Statistics,
            Message = "Cache Content Hit Sources")]
        public abstract void StorageCacheContentHitSources(LoggingContext context, IDictionary<string, int> entryMatches);

        [GeneratedEvent(
            (int)LogEventId.PipTwoPhaseCacheGetCacheEntry,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] PipTwoPhaseCache.GetCacheEntry: Weak fingerprint: {weakFingerprint} | Path-set hash: {pathSetHash} | Strong fingerprint: {strongFingerprint} | Metadata hash: {metadataHash}")]
        internal abstract void PipTwoPhaseCacheGetCacheEntry(LoggingContext loggingContext, string pipDescription, string weakFingerprint, string pathSetHash, string strongFingerprint, string metadataHash);

        [GeneratedEvent(
            (int)LogEventId.PipTwoPhaseCachePublishCacheEntry,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] PipTwoPhaseCache.PublishCacheEntry: Weak fingerprint: {weakFingerprint} | Path-set hash: {pathSetHash} | Strong fingerprint: {strongFingerprint} | Given metadata hash: {givenMetadataHash} => Status: {status} | Published metadata hash: {publishedMetadataHash}")]
        internal abstract void PipTwoPhaseCachePublishCacheEntry(LoggingContext loggingContext, string pipDescription, string weakFingerprint, string pathSetHash, string strongFingerprint, string givenMetadataHash, string status, string publishedMetadataHash);

        #endregion

        #region EngineScheduler

        #region Stats

        [GeneratedEvent(
            (ushort)EventId.IncrementalBuildSavingsSummary,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Events.Keywords.Performance | Events.Keywords.UserMessage),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = Events.PhasePrefix + "Cache savings: {cacheRate:P} of {totalProcesses} included processes. {ignoredProcesses} excluded via filtering.")]
        internal abstract void IncrementalBuildSavingsSummary(LoggingContext loggingContext, double cacheRate, long totalProcesses, long ignoredProcesses);

        [GeneratedEvent(
            (ushort)EventId.IncrementalBuildSharedCacheSavingsSummary,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Events.Keywords.Performance | Events.Keywords.UserMessage),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = Events.PhasePrefix + "Shared cache usage: Downloaded {remoteProcesses} processes [{relativeCacheRate:P} of cache hits] and {contentDownloaded} of outputs.")]
        internal abstract void IncrementalBuildSharedCacheSavingsSummary(LoggingContext loggingContext, double relativeCacheRate, long remoteProcesses, string contentDownloaded);

        [GeneratedEvent(
            (ushort)EventId.SchedulerDidNotConverge,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Events.Keywords.Performance | Events.Keywords.UserMessage),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "This build did not converge with the remote. Run the cache miss analyzer against the remote build to see why.\r\n\r\n{executionAnalyzerPath} /mode:cacheMiss /xl:[REPACE_WITH_REMOTE_XLG] /xl:{executionLogPath} /o:{outputFilePath}")]
        internal abstract void SchedulerDidNotConverge(LoggingContext loggingContext, string executionLogPath, string executionAnalyzerPath, string outputFilePath);

        [GeneratedEvent(
            (ushort)EventId.RemoteCacheHitsGreaterThanTotalCacheHits,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)(Events.Keywords.Performance | Events.Keywords.UserMessage),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = Events.PhasePrefix + "Inconsistent cache hit statistics: number of remote cache hits ({remoteHits}) greater than number of total cache hits ({totalHits}).")]
        internal abstract void RemoteCacheHitsGreaterThanTotalCacheHits(LoggingContext loggingContext, long remoteHits, long totalHits);

        [GeneratedEvent(
            (ushort)EventId.PipsSucceededStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.Performance | Events.Keywords.UserMessage),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "  Pips successfully executed: {numberOfPips}")]
        internal abstract void PipsSucceededStats(LoggingContext loggingContext, long numberOfPips);

        [GeneratedEvent(
            (ushort)EventId.PipsFailedStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "  Pips that failed: {numberOfPips}")]
        internal abstract void PipsFailedStats(LoggingContext loggingContext, long numberOfPips);

        [GeneratedEvent(
            (ushort)EventId.PipDetailedStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "  PipStats Type: {pipType}, successful: {success}, failed: {fail}, skipped: {skipped} ignored: {ignored}, total: {total}")]
        internal abstract void PipDetailedStats(LoggingContext loggingContext, string pipType, long success, long fail, long skipped, long ignored, long total);

        [GeneratedEvent(
            (ushort)EventId.ProcessesCacheMissStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "  Processes that were launched: {numberOfProcesses}")]
        internal abstract void ProcessesCacheMissStats(LoggingContext loggingContext, long numberOfProcesses);

        [GeneratedEvent(
            (ushort)EventId.ProcessesCacheHitStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "  Processes that were skipped due to cache hit: {numberOfProcesses}")]
        internal abstract void ProcessesCacheHitStats(LoggingContext loggingContext, long numberOfProcesses);

        [GeneratedEvent(
            (ushort)EventId.ProcessesSemaphoreQueuedStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "  Processes that got delayed because of semaphore constraints: {numberOfProcesses}")]
        internal abstract void ProcessesSemaphoreQueuedStats(LoggingContext loggingContext, long numberOfProcesses);

        [GeneratedEvent(
            (ushort)EventId.SourceFileHashingStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "  Source files: {sourceFilesHashed} changed | {sourceFilesUnchanged} unchanged | {sourceFilesUntracked} untracked | {sourceFilesAbsent} absent")]
        internal abstract void SourceFileHashingStats(LoggingContext loggingContext, long sourceFilesHashed, long sourceFilesUnchanged, long sourceFilesUntracked, long sourceFilesAbsent);

        [GeneratedEvent(
            (ushort)EventId.OutputFileHashingStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "  Output files: {outputFilesHashed} changed | {outputFilesUnchanged} unchanged")]
        internal abstract void OutputFileHashingStats(LoggingContext loggingContext, long outputFilesHashed, long outputFilesUnchanged);

        [GeneratedEvent(
            (ushort)EventId.OutputFileStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "  Output files: {outputFilesNewlyCreated} produced | {outputFilesDeployed} copied from cache | {outputFilesUpToDate} up-to-date")]
        internal abstract void OutputFileStats(LoggingContext loggingContext, long outputFilesNewlyCreated, long outputFilesDeployed, long outputFilesUpToDate);

        [GeneratedEvent(
            (ushort)EventId.WarningStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "  Tool warnings: {pipsWithWarnings} pip runs caused {warnings} warnings | {pipsWithWarningsFromCache} cached pips caused {warningsFromCache} cached warnings")]
        internal abstract void WarningStats(LoggingContext loggingContext, int pipsWithWarnings, long warnings, int pipsWithWarningsFromCache, long warningsFromCache);

        [GeneratedEvent(
            (ushort)EventId.CacheTransferStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "  Attempts at bringing content to local cache: {tryBringContentToLocalCacheCounts} | Number of artifacts brought to local cache: {artifactsBroughtToLocalCacheCounts} | Total size of artifacts brought to local cache {totalSizeArtifactsBroughtToLocalCache} Mb")]
        internal abstract void CacheTransferStats(
            LoggingContext loggingContext,
            long tryBringContentToLocalCacheCounts,
            long artifactsBroughtToLocalCacheCounts,
            double totalSizeArtifactsBroughtToLocalCache);

        #endregion

        [GeneratedEvent(
            (ushort)LogEventId.PreserveOutputsFailedToMakeOutputPrivate,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Failed to create a private, writeable copy of output file '{file}' from a previous invocation: {error}; the file will be deleted if it exists")]
        internal abstract void PreserveOutputsFailedToMakeOutputPrivate(LoggingContext loggingContext, string pipDescription, string file, string error);

        [GeneratedEvent(
            (ushort)LogEventId.StoppingProcessExecutionDueToResourceExhaustion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "Stopping further process execution due to low remaining RAM: (available RAM MB: {availableRam} < {minimumAvailableRam})" +
            " && (used RAM percentage: {ramUtilization} > {maximumRamUtilization}) ")]
        internal abstract void StoppingProcessExecutionDueToResourceExhaustion(
            LoggingContext loggingContext,
            long availableRam,
            long minimumAvailableRam,
            long ramUtilization,
            long maximumRamUtilization);

        [GeneratedEvent(
            (ushort)LogEventId.CancellingProcessPipExecutionDueToResourceExhaustion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Cancelled process execution due to exceeding resource threshold. Elapsed execution time: {elapsedMs} ms. Peak memory: {peakMemoryMb} MB. Expected memory: {expectedMemoryMb} MB. Cancel time (ms): {cancelMilliseconds}")]
        internal abstract void CancellingProcessPipExecutionDueToResourceExhaustion(LoggingContext loggingContext, string pipDescription, long elapsedMs, int peakMemoryMb, int expectedMemoryMb, int cancelMilliseconds);

        [GeneratedEvent(
            (ushort)LogEventId.StartCancellingProcessPipExecutionDueToResourceExhaustion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Attempting to cancel process execution due to exceeding resource threshold. Elapsed execution time: {elapsedMs} ms. Peak memory: {peakMemoryMb} MB. Expected memory: {expectedMemoryMb} MB.")]
        internal abstract void StartCancellingProcessPipExecutionDueToResourceExhaustion(LoggingContext loggingContext, string pipDescription, long elapsedMs, int peakMemoryMb, int expectedMemoryMb);

        [GeneratedEvent(
            (int)EventId.LogMismatchedDetoursErrorCount,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "The number of messages sent by detoured processes did not match the number received by the {MainExecutableName} process. Refer to the {ShortProductName} log for more information.")]
        public abstract void LogMismatchedDetoursErrorCount(LoggingContext context, long pipSemiStableHash, string pipDescription);

        [GeneratedEvent(
            (int)EventId.FailPipOutputWithNoAccessed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message =
                Events.PipPrefix + "A pip produced outputs with no file access message. The problem persisted after multiple retries. Refer to the {ShortProductName} log for more information. This is an inconsistency in (and detected by) BuildXL Detours. Please retry the build.")]
        public abstract void FailPipOutputWithNoAccessed(LoggingContext context, long pipSemiStableHash, string pipDescription);

        [GeneratedEvent(
            (int)LogEventId.PipCacheMetadataBelongToAnotherPip,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Pip cache metadata belongs to another pip: {details}")]
        public abstract void PipCacheMetadataBelongToAnotherPip(LoggingContext context, long pipSemiStableHash, string pipDescription, string details);

        [GeneratedEvent(
            (int)EventId.PipWillBeRetriedDueToExitCode,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message =
                Events.PipPrefix + "Process is going to be retried due to exiting with exit code '{exitCode}' (remaining retries is {remainingRetries})")]
        public abstract void PipWillBeRetriedDueToExitCode(LoggingContext context, long pipSemiStableHash, string pipDescription, int exitCode, int remainingRetries);

        [GeneratedEvent(
            (ushort)LogEventId.ResumingProcessExecutionAfterSufficientResources,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "Resuming process execution because available RAM is above required limit.")]
        internal abstract void ResumingProcessExecutionAfterSufficientResources(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)LogEventId.ProcessStatus,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Progress),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "Processes: {pipsSucceeded} succeeded, {pipsFailed} failed, {pipsSkippedDueToFailedDependencies} skipped, {pipsRunning} running, {pipsReady} ready, {pipsWaiting} waiting ({pipsWaitingOnSemaphore} on semaphore)")]
        internal abstract void ProcessStatus(
            LoggingContext loggingContext,
            long pipsSucceeded,
            long pipsFailed,
            long pipsSkippedDueToFailedDependencies,
            long pipsRunning,
            long pipsReady,
            long pipsWaiting,
            long pipsWaitingOnSemaphore);

        [GeneratedEvent(
            (ushort)EventId.TerminatingDueToPipFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] The execution schedule is being terminated due to the failure of a pip.")]
        internal abstract void ScheduleTerminatingDueToPipFailure(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.PipSemaphoreQueued,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Postponed because of exhausted semaphore resources")]
        internal abstract void PipSemaphoreQueued(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.PipSemaphoreDequeued,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Reconsidered because previously exhausted semaphore resources became available")]
        internal abstract void PipSemaphoreDequeued(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.IgnoringPipSinceScheduleIsTerminating,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] A pip has become ready, but will not be scheduled. The scheduler is terminating due to a pip failure or cancellation request.")]
        internal abstract void ScheduleIgnoringPipSinceScheduleIsTerminating(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.CancelingPipSinceScheduleIsTerminating,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] A pip's execution has been canceled. The scheduler is terminating due to a pip failure or cancellation request.")]
        internal abstract void ScheduleCancelingPipSinceScheduleIsTerminating(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.PipFailedDueToFailedPrerequisite,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "{file}({line},{column}): [{pipDescription}] has become ready, but will be skipped due to a failed prerequisite pip.")]
        internal abstract void SchedulePipFailedDueToFailedPrerequisite(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (ushort)EventId.StartAssigningPriorities,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Message = "-- Calculating pip priorities")]
        internal abstract void StartAssigningPriorities(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)EventId.EndAssigningPriorities,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = "-- Done calculating pip priorities")]
        internal abstract void EndAssigningPriorities(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)EventId.StartSettingPipStates,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Message = "-- Setting pip states")]
        internal abstract void StartSettingPipStates(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)EventId.EndSettingPipStates,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = "-- Done setting pip states")]
        internal abstract void EndSettingPipStates(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)EventId.HashedSourceFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "Hash '{hash}' computed for source file '{relativeSourceFilePath}'")]
        internal abstract void ScheduleHashedSourceFile(LoggingContext loggingContext, string relativeSourceFilePath, string hash);

        [GeneratedEvent(
            (ushort)LogEventId.ScheduleHashedOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Hash '{hash}' computed for prior output file '{relativeSourceFilePath}'")]
        internal abstract void ScheduleHashedOutputFile(LoggingContext loggingContext, string pipDescription, string relativeSourceFilePath, string hash);

        internal void FailedToHashInputFile(LoggingContext loggingContext, string pipDescription, string path, BuildXLException ex)
        {
            FailedToHashInputFile(loggingContext, pipDescription, path, ex.LogEventErrorCode, ex.LogEventMessage);
        }

        [GeneratedEvent(
            (ushort)EventId.FailedToHashInputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Hash file '{path}' failed with error code {errorCode:X8}: {message}")]
        internal abstract void FailedToHashInputFile(LoggingContext loggingContext, string pipDescription, string path, int errorCode, string message);

        [GeneratedEvent(
            (ushort)EventId.FailedToHashInputFileDueToFailedExistenceCheck,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Unable to determine existence of the source file '{path}': {message}")]
        internal abstract void FailedToHashInputFileDueToFailedExistenceCheck(LoggingContext loggingContext, string pipDescription, string path, string message);

        [GeneratedEvent(
            (ushort)EventId.FailedToHashInputFileBecauseTheFileIsDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Unable to hash the source file '{path}' because the file is actually a directory")]
        internal abstract void FailedToHashInputFileBecauseTheFileIsDirectory(LoggingContext loggingContext, string pipDescription, string path);

        [GeneratedEvent(
            (ushort)EventId.StorageUsingKnownHashForSourceFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "The file '{sourceFilePath}' was unchanged from a previous run, and has a known hash of '{hash}'.")]
        internal abstract void StorageUsingKnownHashForSourceFile(LoggingContext loggingContext, string sourceFilePath, string hash);

        [GeneratedEvent(
            (ushort)EventId.StorageHashedSourceFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "The file '{sourceFilePath}' was hashed since its contents were not known from a previous run. It is now known to have hash '{hash}'.")]
        internal abstract void StorageHashedSourceFile(LoggingContext loggingContext, string sourceFilePath, string hash);

        [GeneratedEvent(
            (ushort)EventId.IgnoringUntrackedSourceFileNotUnderMount,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "The file '{untrackedFileFullPath}' is being used as a source file, but is not under a defined mountpoint. This file is thus 'untracked', and changes to it will not impact incremental builds.")]
        internal abstract void ScheduleIgnoringUntrackedSourceFileNotUnderMount(LoggingContext loggingContext, string untrackedFileFullPath);

        [GeneratedEvent(
            (ushort)EventId.IgnoringUntrackedSourceFileUnderMountWithHashingDisabled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "The file '{untrackedFileFullPath}' is being used as a source file, but is under the mountpoint '{mountPoint}' which has hashing disabled. This file is thus 'untracked', and changes to it will not impact incremental builds.")]
        internal abstract void ScheduleIgnoringUntrackedSourceFileUnderMountWithHashingDisabled(LoggingContext loggingContext, string untrackedFileFullPath, string mountPoint);

        #region Pip Start/End

        private const string PipStartMessage = "{file}({line},{column}): [{pipDescription}] Start Processing";
        private const string PipEndMessage = "[{pipDescription}] Finish Processing";

        [GeneratedEvent(
            (ushort)EventId.ProcessStart,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Events.Keywords.Diagnostics | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.PipExecutor,
            EventOpcode = (byte)EventOpcode.Start,
            Message = PipStartMessage)]
        internal abstract void ProcessStart(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDescription,
            string pipValueId,
            string executable,
            string executableHash);

        [GeneratedEvent(
            (ushort)EventId.ProcessEnd,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Events.Keywords.Diagnostics | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.PipExecutor,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = PipEndMessage)]
        internal abstract void ProcessEnd(
            LoggingContext loggingContext,
            string pipDescription,
            string pipValueId,
            int status,
            long ticks,
            string executableHash);

        [GeneratedEvent(
            (ushort)EventId.CopyFileStart,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Events.Keywords.Diagnostics | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.PipExecutor,
            EventOpcode = (byte)EventOpcode.Start,
            Message = PipStartMessage)]
        internal abstract void CopyFileStart(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (ushort)EventId.CopyFileEnd,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Events.Keywords.Diagnostics | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.PipExecutor,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = PipEndMessage)]
        internal abstract void CopyFileEnd(LoggingContext loggingContext, string pipDescription, string pipValueId, int status, long ticks);

        [GeneratedEvent(
            (ushort)EventId.WriteFileStart,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Events.Keywords.Diagnostics | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.PipExecutor,
            EventOpcode = (byte)EventOpcode.Start,
            Message = PipStartMessage)]
        internal abstract void WriteFileStart(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (ushort)EventId.WriteFileEnd,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Events.Keywords.Diagnostics | Events.Keywords.Performance),
            EventTask = (ushort)Events.Tasks.PipExecutor,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = PipEndMessage)]
        internal abstract void WriteFileEnd(LoggingContext loggingContext, string pipDescription, string pipValueId, int status, long ticks);

        #endregion

        [GeneratedEvent(
            (ushort)EventId.StartSchedulingPipsWithFilter,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            EventOpcode = (byte)EventOpcode.Start,
            Message = Events.PhasePrefix + "Scheduling pips with filtering")]
        internal abstract void StartSchedulingPipsWithFilter(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)EventId.EndSchedulingPipsWithFilter,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = Events.PhasePrefix + "Done scheduling pips with filtering")]
        internal abstract void EndSchedulingPipsWithFilter(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)EventId.StartComputingPipFingerprints,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "-- Start bottom-up computations of pip fingerprints")]
        internal abstract void ScheduleStartComputingPipFingerprints(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)EventId.StartMaterializingPipOutputs,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "-- Start top-down materializations of pips' outputs")]
        internal abstract void ScheduleStartMaterializingPipOutputs(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)EventId.StartMarkingInvalidPipOutputs,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "-- Start marking invalid pip outputs")]
        internal abstract void ScheduleStartMarkingInvalidPipOutputs(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)EventId.StartExecutingPips,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "-- Start bottom-up pip executions")]
        internal abstract void ScheduleStartExecutingPips(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)EventId.TopDownPipForMaterializingOutputs,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "{file}({line},{column}): [{pipDescription}] is a starting pip of top-down traversal for materializing pip outputs.")]
        internal abstract void ScheduleTopDownPipForMaterializingOutputs(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (ushort)EventId.InvalidatedDoneMaterializingOutputPip,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "{file}({line},{column}): [{pipDescription}] has materialized its outputs, but the outputs have to be invalidated because the pip may get re-run later.")]
        internal abstract void ScheduleInvalidatedDoneMaterializingOutputPip(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (ushort)EventId.PossiblyInvalidatingPip,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "Zig-Zag scheduling: Pip '{pipDescriptionInvalidator}' possibly invalidating pip '{pipDescriptionInvalidated}'.")]
        internal abstract void SchedulePossiblyInvalidatingPip(LoggingContext loggingContext, string pipDescriptionInvalidator, string pipDescriptionInvalidated);

        [GeneratedEvent(
            (ushort)EventId.BottomUpPipForPipExecutions,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "{file}({line},{column}): [{pipDescription}] is a starting pip of bottom-up traversal for pip executions.")]
        internal abstract void ScheduleBottomUpPipForPipExecutions(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (int)EventId.StorageCacheGetContentUsingFallback,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Placing content {0}: Trying ingress of fallback path '{1}' since content not in cache.")]
        public abstract void StorageCacheGetContentUsingFallback(LoggingContext context, string contentHash, string fallbackPath);

        #endregion

        #region Status updating

        /// <summary>
        /// We have 2 versions of this message for the sake of letting one be overwriteable and the other not.
        /// Other than they should always stay identical. So to enforce that we have them reference the same
        /// set of attribute arguments and go through the same method
        /// </summary>
        internal void LogPipStatus(
            LoggingContext loggingContext,
            long pipsSucceeded,
            long pipsFailed,
            long pipsSkippedDueToFailedDependencies,
            long pipsRunning,
            long pipsReady,
            long pipsWaiting,
            long pipsWaitingOnSemaphore,
            long servicePipsRunning,
            string perfInfoForConsole,
            long pipsWaitingOnResources,
            long procsExecuting,
            long procsSucceeded,
            long procsFailed,
            long procsSkippedDueToFailedDependencies,
            long procsPending,
            long procsWaiting,
            long procsCacheHit,
            long procsNotIgnored,
            string limitingResource,
            string perfInfoForLog,
            bool overwriteable,
            long copyFileDone,
            long copyFileNotDone,
            long writeFileDone,
            long writeFileNotDone)
        {
            // Noop if no process information is included. This can happen for the last status event in a build using
            // incremental scheduling if it goes through the codepath where zero files changed. All other codepaths
            // compute the actual process count and can be logged
            if (procsExecuting + procsSucceeded + procsFailed + procsSkippedDueToFailedDependencies + procsPending + procsWaiting + procsCacheHit == 0)
            {
                return;
            }

            if (overwriteable)
            {
                PipStatus(
                    loggingContext,
                    pipsSucceeded,
                    pipsFailed,
                    pipsSkippedDueToFailedDependencies,
                    pipsRunning,
                    pipsReady,
                    pipsWaiting,
                    pipsWaitingOnSemaphore,
                    servicePipsRunning,
                    perfInfoForConsole,
                    pipsWaitingOnResources,
                    procsExecuting,
                    procsSucceeded,
                    procsFailed,
                    procsSkippedDueToFailedDependencies,
                    procsPending,
                    procsWaiting,
                    procsCacheHit,
                    procsNotIgnored,
                    limitingResource,
                    perfInfoForLog,
                    copyFileDone,
                    copyFileNotDone,
                    writeFileDone,
                    writeFileNotDone);
            }
            else
            {
                PipStatusNonOverwriteable(
                    loggingContext,
                    pipsSucceeded,
                    pipsFailed,
                    pipsSkippedDueToFailedDependencies,
                    pipsRunning,
                    pipsReady,
                    pipsWaiting,
                    pipsWaitingOnSemaphore,
                    servicePipsRunning,
                    perfInfoForConsole,
                    pipsWaitingOnResources,
                    procsExecuting,
                    procsSucceeded,
                    procsFailed,
                    procsSkippedDueToFailedDependencies,
                    procsPending,
                    procsWaiting,
                    procsCacheHit,
                    procsNotIgnored,
                    limitingResource,
                    perfInfoForLog,
                    copyFileDone,
                    copyFileNotDone,
                    writeFileDone,
                    writeFileNotDone);
            }
        }

        /// <summary>
        /// Generally we feel that reporting the number of processes is the most externally useful data. The count
        /// of all pips is included for more detailed diagnosis.
        ///
        /// The names in the message don't match up 1:1 with the internal states in the scheduler since that's too much
        /// detail for end users.
        ///  executing - Externally running child processes
        ///  pending - Processes that are ready to run if there were more parallelism. Technically cache lookups fall into
        ///             this status as well so that description isn't totally correct, but it's a good approximation for sake of simplifying
        ///  waiting - Waiting for upstream processes to finish
        /// </summary>
        private const string StatusMessage =
            "Procs: {procsSucceeded} succeeded ({procsCacheHit}) hit, {procsFailed} failed, {procsSkippedDueToFailedDependencies} skipped, {procsExecuting} executing, " +
            "{procsPending} pending, {procsWaiting} waiting {procsNotIgnored} total. | " +
            "All:{pipsSucceeded} succeeded, {pipsFailed} failed, {pipsSkippedDueToFailedDependencies} skipped,  {pipsRunning} running, {pipsReady} ready," +
            " {pipsWaiting} waiting ({pipsWaitingOnSemaphore} on semaphore), {pipsWaitingOnResources} resource paused. Services: {servicePipsRunning}." +
            " LimitingResource:{limitingResource}. {perfInfoForLog}";

        private const Generators StatusGenerators = EventGenerators.LocalOnly;
        private const Level StatusLevel = Level.Informational;
        private const EventKeywords StatusKeywords = Events.Keywords.UserMessage | Events.Keywords.Progress;
        private const EventTask StatusTask = Events.Tasks.Scheduler;

        [GeneratedEvent(
            (ushort)EventId.PipStatus,
            EventGenerators = StatusGenerators,
            EventLevel = StatusLevel,
            Keywords = (int)(StatusKeywords | Events.Keywords.Overwritable),
            EventTask = (ushort)StatusTask,
            Message = StatusMessage)]
        internal abstract void PipStatus(
            LoggingContext loggingContext,
            long pipsSucceeded,
            long pipsFailed,
            long pipsSkippedDueToFailedDependencies,
            long pipsRunning,
            long pipsReady,
            long pipsWaiting,
            long pipsWaitingOnSemaphore,
            long servicePipsRunning,
            string perfInfoForConsole,
            long pipsWaitingOnResources,
            long procsExecuting,
            long procsSucceeded,
            long procsFailed,
            long procsSkippedDueToFailedDependencies,
            long procsPending,
            long procsWaiting,
            long procsCacheHit,
            long procsNotIgnored,
            string limitingResource,
            string perfInfoForLog,
            long copyFileDone,
            long copyFileNotDone,
            long writeFileDone,
            long writeFileNotDone);

        [GeneratedEvent(
            (ushort)LogEventId.PipStatusNonOverwriteable,
            EventGenerators = StatusGenerators,
            EventLevel = StatusLevel,
            Keywords = (int)StatusKeywords,
            EventTask = (ushort)StatusTask,
            Message = StatusMessage)]
        internal abstract void PipStatusNonOverwriteable(
            LoggingContext loggingContext,
            long pipsSucceeded,
            long pipsFailed,
            long pipsSkippedDueToFailedDependencies,
            long pipsRunning,
            long pipsReady,
            long pipsWaiting,
            long pipsWaitingOnSemaphore,
            long servicePipsRunning,
            string perfInfoForConsole,
            long pipsWaitingOnResources,
            long procsExecuting,
            long procsSucceeded,
            long procsFailed,
            long procsSkippedDueToFailedDependencies,
            long procsPending,
            long procsWaiting,
            long procsCacheHit,
            long procsNotIgnored,
            string limitingResource,
            string perfInfoForLog,
            long copyFileDone,
            long copyFileNotDone,
            long writeFileDone,
            long writeFileNotDone);
        #endregion

        [GeneratedEvent(
          (int)EventId.FileMonitoringError,
          EventGenerators = EventGenerators.LocalOnly,
          EventLevel = Level.Error,
          Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
          EventTask = (int)Events.Tasks.PipExecutor,
          Message = Events.PipPrefix + "- Disallowed file accesses were detected (R = read, W = write):\r\n{2}")]
        public abstract void FileMonitoringError(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string paths);

        [GeneratedEvent(
            (int)EventId.FileMonitoringWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "- Disallowed file accesses were detected (R = read, W = write):\r\n{2}")]
        public abstract void FileMonitoringWarning(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string paths);

        #region DependencyViolation

        [GeneratedEvent(
             (int)LogEventId.DependencyViolationDoubleWrite,
             EventGenerators = EventGenerators.LocalOnly,
             EventLevel = Level.Verbose,
             Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
             EventTask = (int)Events.Tasks.Scheduler,
             Message =
                 PipDependencyAnalysisPrefix +
                 "Double write: This pip wrote to the path '{2}', which could have been produced earlier by the pip [{3}]. " +
                 "This can result in unreliable builds and incorrect caching, since consumers of that path may see the wrong content.")]
        public abstract void DependencyViolationDoubleWrite(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            string producingPipDescription);

        [GeneratedEvent(
             (int)LogEventId.AllowedSameContentDoubleWrite,
             EventGenerators = EventGenerators.LocalOnly,
             EventLevel = Level.Verbose,
             Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
             EventTask = (int)Events.Tasks.Scheduler,
             Message =
                 PipDependencyAnalysisPrefix +
                 "Allowed double write: This pip wrote to the path '{2}', which could have been produced earlier by the pip [{3}]. " +
                 "However, the content produced was the same for both and the configured double write policy allows for it.")]
        public abstract void AllowedSameContentDoubleWrite(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            string producingPipDescription);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationReadRace,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipSpecDependencyAnalysisPrefix +
                "Read race: This pip read from the path '{4}', which could have been written at the same time by the pip [{5}]. " +
                "This can result in unreliable builds since this pip could have failed to access the path (due to the concurrent write). " +
                "Consider declaring a dependency on the correct producer of this path to prevent that race and allow caching of this pip.")]
        public abstract void DependencyViolationReadRace(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string path,
            string producingPipDescription);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationUndeclaredOrderedRead,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipSpecDependencyAnalysisPrefix +
                "Undeclared ordered read: This pip read from the path '{4}', which is written to earlier by pip [{5}] (order is constrained by declared dependencies). " +
                "This pip did not declare an input dependency on this path, and so the produced file may not be materialized on disk when needed. " +
                "Consider declaring a dependency on the correct producer of this path.")]
        public abstract void DependencyViolationUndeclaredOrderedRead(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string path,
            string producingPipDescription);

        // TODO:[340919]: Unused / disabled for perf reasons. Re-enable at some point.
        [GeneratedEvent(
            (int)LogEventId.DependencyViolationMissingSourceDependencyWithValueSuggestion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipDependencyAnalysisPrefix +
                "Missing source dependency: This pip read from the path '{2}' which is a source file. " +
                "However, this pip did not declare an input dependency on this path, and so the produced file may not be materialized on disk when needed. " +
                "Consider declaring a dependency on the correct producer of this path, which may be {3}.")]
        public abstract void DependencyViolationMissingSourceDependencyWithValueSuggestion(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            string suggestedValue);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationMissingSourceDependency,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipDependencyAnalysisPrefix +
                "Missing source dependency: This pip read from the path '{2}' which is a source file. " +
                "However, this pip did not declare an input dependency on this path, and so it will not be considered a pip input. " +
                "Consider declaring a dependency on this file.")]
        public abstract void DependencyViolationMissingSourceDependency(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationUndeclaredReadCycle,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipDependencyAnalysisPrefix +
                "Undeclared read cycle: This pip read from the path '{2}', which is written to by pip [{3}], which has a dependency on this pip. " +
                "Resolve the cycle and declare a dependency on the correct producer of this path.")]
        public abstract void DependencyViolationUndeclaredReadCycle(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            string producingPipDescription);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationReadUndeclaredOutput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipDependencyAnalysisPrefix +
                "Read undeclared output: This pip read from the path '{path}', which was written by pip [{producingPipDescription}] (file was not declared as an output). " +
                "This pip did not declare an input dependency on this path, and so the produced file may not be materialized on disk when needed. " +
                "Consider declaring a dependency on the correct producer of this path.")]
        public abstract void DependencyViolationReadUndeclaredOutput(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string path,
            string producingPipDescription);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationWriteInSourceSealDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipDependencyAnalysisPrefix +
                "Write under a source sealed directory: This pip writes to path '{4}', which is under the source sealed directory '{5}'. " +
                "Writes are not allowed under a source sealed directory, consider declaring inputs individually instead of sealing.")]
        public abstract void DependencyViolationWriteInSourceSealDirectory(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string path,
            string producingPipDescription);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationWriteInUndeclaredSourceRead,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipDependencyAnalysisPrefix +
                "Allowed undeclared access on an output file: This pip accesses path '{4}', but '{5}' writes into it. " +
                "Even though the undeclared access is allowed, it should only happen on a source file.")]
        public abstract void DependencyViolationWriteInUndeclaredSourceRead(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string path,
            string producingPipDescription);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationWriteOnAbsentPathProbe,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipDependencyAnalysisPrefix +
                "Write on an absent path probe: This pip writes path '{4}', but '{5}' probed it when the path was not yet created. " +
                "However, the probe is not guaranteed to always be absent and may introduce non-deterministic behaviors in the build. " +
                "Please declare an explicit dependency between these pips so the probe always happens after the path is written.")]
        public abstract void DependencyViolationWriteOnAbsentPathProbe(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string path,
            string producingPipDescription);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationAbsentPathProbeInsideUndeclaredOpaqueDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipDependencyAnalysisPrefix +
                AbsentPathProbeUnderOpaqueDirectoryMessage +
                "This pip is configured to fail if such a probe occurs. " )]
        public abstract void DependencyViolationAbsentPathProbeInsideUndeclaredOpaqueDirectory(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path);

        [GeneratedEvent(
            (int)LogEventId.AbsentPathProbeInsideUndeclaredOpaqueDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                "[{1}]"+
                AbsentPathProbeUnderOpaqueDirectoryMessage + 
                "If the pip is configured to run in Relaxed mode (AbsentPathProbeInUndeclaredOpaquesMode), this pip will not be incrementally skipped which might cause perf degradation. " )]
        public abstract void AbsentPathProbeInsideUndeclaredOpaqueDirectory(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationGenericWithRelatedPip,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipDependencyAnalysisPrefix +
                "{2} due to {3}-level access to path '{4}' (related pip [{5}])")]
        public abstract void DependencyViolationGenericWithRelatedPip(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string violationType,
            string accessLevel,
            string path,
            string relatedPipDescription);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationGeneric,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipDependencyAnalysisPrefix +
               "{2} due to {3}-level access to path '{4}'")]
        public abstract void DependencyViolationGeneric(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string violationType,
            string accessLevel,
            string path);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationUndeclaredOutput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipDependencyAnalysisPrefix +
                "Missing output declaration: This pip wrote an unexpected output to path '{2}'. " +
                "Declare this file as an output of the pip.")]
        public abstract void DependencyViolationUndeclaredOutput(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path);

        [GeneratedEvent(
            (int)LogEventId.DependencyViolationSharedOpaqueWriteInTempDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage | (int)Events.Keywords.DependencyAnalysis,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                PipDependencyAnalysisPrefix +
                "Write under a temporary directory that is inside a shared opaque: This pip writes to path '{sharedOpaqueWritePath}', which is under the temp directory '{tempPath}' " +
                "declared in Pip ['{pipWithTempPathDescription}', {sharedOpaqueConfigFile.File} ({sharedOpaqueConfigFile.Line}, {sharedOpaqueConfigFile.Position})]. " +
                "Shared Opaque writes are not allowed under a temp directory, consider declaring a temp directory outside of the shared opaque.")]
        public abstract void DependencyViolationSharedOpaqueWriteInTempDirectory(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string sharedOpaqueWritePath,
            Location sharedOpaqueConfigFile,
            string pipWithTempPathDescription,
            string tempPath);

        #endregion

        [GeneratedEvent(
            (ushort)EventId.PipFailedTempDirectoryCleanup,
            EventLevel = Level.Verbose,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Failed to clean temp directory at '{0}'. Reason: {1}")]
        public abstract void PipFailedTempDirectoryCleanup(LoggingContext context, string directory, string exceptionMessage);

        [GeneratedEvent(
            (ushort)EventId.PipFailedTempFileCleanup,
            EventLevel = Level.Verbose,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Failed to clean temp file at '{0}'. Reason: {1}")]
        public abstract void PipFailedTempFileCleanup(LoggingContext context, string file, string exceptionMessage);

        [GeneratedEvent(
            (ushort)EventId.PipTempCleanerThreadSummary,
            EventLevel = Level.Verbose,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = "Temp cleaner thread exited with {0} cleaned, {1} remaining and {2} failed temp directories, {3} cleaned, {4} remaining and {5} failed temp files")]
        public abstract void PipTempCleanerSummary(LoggingContext context, long cleanedDirs, long remainingDirs, long failedDirs, long cleanedFiles, long remainingFiles, long failedFiles);

        [GeneratedEvent(
            (int)EventId.RunningTimeAdded,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.CriticalPaths,
            Message = "[Pip{0:X16}] Running time added: {1}ms")]
        public abstract void RunningTimeAdded(LoggingContext context, long semiStableHash, uint milliseconds);

        [GeneratedEvent(
            (int)EventId.RunningTimeUpdated,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.CriticalPaths,
            Message = "[Pip{0:X16}] Running time updated: {1}ms from {2}ms, relative deviation {3}%")]
        public abstract void RunningTimeUpdated(LoggingContext context, long semiStableHash, uint milliseconds, uint oldMilliseconds, int relativeDeviation);

        [GeneratedEvent(
            (int)EventId.RunningTimeStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Performance | (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "  Running times: {0} hits | {1} misses | {2} added | {3} updated | {4}% average relative process runtime deviation where critical path suggestions were available")]
        public abstract void RunningTimeStats(LoggingContext context, long hits, long misses, long added, long updated, int averageRuntimeDeviation);

        [GeneratedEvent(
            (int)EventId.PipQueueConcurrency,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = "Initialized PipQueue with concurrencies: IO:{0}, ChooseWorkerCacheLookup:{1}, CacheLookup:{2}, ChooseWorkerCpu:{3}, CPU:{4}, Materialize:{5}, Light:{6}, MasterCacheLookupMultiplier: {7}, MasterCpuMultiplier: {8}")]
        public abstract void PipQueueConcurrency(LoggingContext context, int io, int chooseWorkerCacheLookup, int cacheLookup, int chooseWorkerCpu, int cpu, int materialize, int light, string masterCacheLookupMultiplier, string masterCpuMultiplier);

        [GeneratedEvent(
            (int)EventId.NoPipsMatchedFilter,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "No pips match this filter: {0}")]
        public abstract void NoPipsMatchedFilter(LoggingContext context, string pipFilter);

        [GeneratedEvent(
            (int)EventId.InvalidSealDirectoryContentSinceNotUnderRoot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot seal the file '{rewrittenFile}' as part of directory '{sealedDirectoryPath}' since that file is not a descendant. "
                + "When sealing a directory, all files under that directory must be specified (but no others outside of it).")]
        public abstract void ScheduleFailAddPipInvalidSealDirectoryContentSinceNotUnderRoot(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile,
            string sealedDirectoryPath);

        [GeneratedEvent(
            (int)EventId.InvalidSealDirectorySourceNotUnderMount,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Source directory '{sealedDirectoryPath}' (created via '{pipValueId}') cannot be sealed. This directory is not under a mount. Source sealed directories must be under a readable mount.")]
        public abstract void ScheduleFailAddPipInvalidSealDirectorySourceNotUnderMount(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string sealedDirectoryPath);

        [GeneratedEvent(
            (int)EventId.InvalidSealDirectorySourceNotUnderReadableMount,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Source directory '{sealedDirectoryPath}' (created via '{pipValueId}') cannot be sealed. This directory is under mount '{mountName}' with folder '{mountPath}' which is not declared as readable by the configuration.")]
        public abstract void ScheduleFailAddPipInvalidSealDirectorySourceNotUnderReadableMount(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string sealedDirectoryPath,
            string mountPath,
            string mountName);

        [GeneratedEvent(
            (int)EventId.ScheduleFailAddPipInvalidComposedSealDirectoryNotUnderRoot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Composite directory '{compositeSealedDirectoryPath}' (created via '{pipValueId}') cannot be sealed. Directory '{sealDirectoryMemberPath}' is not nested within the composite directory root.")]
        public abstract void ScheduleFailAddPipInvalidComposedSealDirectoryNotUnderRoot(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string compositeSealedDirectoryPath,
            string sealDirectoryMemberPath);

        [GeneratedEvent(
            (int)EventId.ScheduleFailAddPipInvalidComposedSealDirectoryIsNotSharedOpaque,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Composite directory '{compositeSealedDirectoryPath}' (created via '{pipValueId}') cannot be sealed. Directory '{sealDirectoryMemberPath}' is not a shared opaque.")]
        public abstract void ScheduleFailAddPipInvalidComposedSealDirectoryIsNotSharedOpaque(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string compositeSealedDirectoryPath,
            string sealDirectoryMemberPath);

        [GeneratedEvent(
            (int)EventId.PipStaticFingerprint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                "Static fingerprint of {pipDescription} is '{staticFingerprint}':\r\n{fingerprintText}.")]
        public abstract void PipStaticFingerprint(LoggingContext context, string pipDescription, string staticFingerprint, string fingerprintText);

        [GeneratedEvent(
            (int)EventId.InvalidInputDueToMultipleConflictingRewriteCounts,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' could not be added because it depends on multiple versions (different rewrite counts) of file '{dependencyFile}'.")]
        public abstract void ScheduleFailAddPipInvalidInputDueToMultipleConflictingRewriteCounts(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string dependencyFile);

        [GeneratedEvent(
            (int)EventId.InvalidProcessPipDueToNoOutputArtifacts,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The process pip '{pipDescription}' could not be added because it does not specify any output file or opaque directory in a non-temp location. At least one output file or opaque directory is required.")]
        public abstract void ScheduleFailAddProcessPipProcessDueToNoOutputArtifacts(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (int)EventId.UnableToCreateExecutionLogFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Execution Log file '{executionLogFile}' could not be created. Error: {exception}")]
        public abstract void UnableToCreateLogFile(
            LoggingContext context,
            string executionLogFile,
            string exception);

        [GeneratedEvent(
            (int)LogEventId.RocksDbException,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "RocksDb encountered an exception:\r\nException: {exception}.")]
        public abstract void RocksDbException(
            LoggingContext context,
            string exception);

        [GeneratedEvent(
            (int)LogEventId.FingerprintStoreUnableToCreateDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Fingerprint store: Directory '{fingerprintStoreDirectory}' could not be created. Error: {exception}.")]
        public abstract void FingerprintStoreUnableToCreateDirectory(
            LoggingContext context,
            string fingerprintStoreDirectory,
            string exception);

        [GeneratedEvent(
            (int)LogEventId.FingerprintStoreUnableToOpen,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Fingerprint store: Could not open fingerprint store. Error: {failure}.")]
        public abstract void FingerprintStoreUnableToOpen(
            LoggingContext context,
            string failure);

        [GeneratedEvent(
            (int)LogEventId.FingerprintStoreUnableToHardLinkLogFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Fingerprint store: Could not create hardlink for logs from '{storeFile}' to '{logFile}'. Error: {exception}. FingerprintStore files will be copied.")]
        public abstract void FingerprintStoreUnableToHardLinkLogFile(
            LoggingContext context,
            string logFile,
            string storeFile,
            string exception);

        [GeneratedEvent(
            (int)LogEventId.FingerprintStoreUnableToCopyOnWriteLogFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Fingerprint store: Could not create a copy-on-write for logs from '{storeFile}' to '{logFile}'. Error: {exception}. FingerprintStore files will be copied.")]
        public abstract void FingerprintStoreUnableToCopyOnWriteLogFile(
            LoggingContext context,
            string logFile,
            string storeFile,
            string exception);

        [GeneratedEvent(
            (int)LogEventId.FingerprintStoreSnapshotException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Fingerprint store: Snapshot failed with error. Error: {exception}. The FingerprintStore logs may be missing for post-build {ShortProductName} execution analyzers.")]
        public abstract void FingerprintStoreSnapshotException(
            LoggingContext context,
            string exception);

        [GeneratedEvent(
            (int)LogEventId.FingerprintStoreFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Fingerprint store: Operation failed with error. Error: {failure}. The FingerprintStore logs may be missing for post-build {ShortProductName} execution analyzers.")]
        public abstract void FingerprintStoreFailure(
            LoggingContext context,
            string failure);

        [GeneratedEvent(
            (int)LogEventId.FingerprintStoreGarbageCollectCanceled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Fingerprint store: Garbage collect for column {columnName} canceled after {timeLimit}. Garbage collection will resume on next build.")]
        public abstract void FingerprintStoreGarbageCollectCanceled(
            LoggingContext context,
            string columnName,
            string timeLimit);

        [GeneratedEvent(
            (int)LogEventId.MovingCorruptFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "A corrupt {ShortProductName} file was detected. Removing it and saving it to the logs file. File: {file}, Logs: {destination}")]
        public abstract void MovingCorruptFile(
            LoggingContext context,
            string file,
            string destination);

        [GeneratedEvent(
            (int)LogEventId.FailedToMoveCorruptFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Failed to move corrupt {ShortProductName} file to logs directory. File: {file}, Destination: {destination}, Error: {exception}")]
        public abstract void FailedToMoveCorruptFile(
            LoggingContext context,
            string file,
            string destination,
            string exception);

        [GeneratedEvent(
            (int)LogEventId.FailedToDeleteCorruptFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Failed to delete corrupt {ShortProductName} file. This could cause subsequent build issues. File: {file}, Error: {exception}")]
            public abstract void FailedToDeleteCorruptFile(
                LoggingContext context,
                string file,
                string exception);

        [GeneratedEvent(
            (int)EventId.InvalidOutputDueToMultipleConflictingRewriteCounts,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The process pip '{pipDescription}' could not be added because it outputs multiple versions (different rewrite counts) of file '{outputFile}'.")]
        public abstract void ScheduleFailAddPipInvalidOutputDueToMultipleConflictingRewriteCounts(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile);

        [GeneratedEvent(
            (int)EventId.InvalidProcessPipDueToExplicitArtifactsInOpaqueDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "The process pip '{pipDescription}' could not be added because it has explicit output '{outputFile}' in  opaque directory '{opaqueDirectoryPath}').")]
        public abstract void ScheduleFailAddProcessPipDueToExplicitArtifactsInOpaqueDirectory(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            string opaqueDirectoryPath);

        [GeneratedEvent(
            (int)EventId.InvalidPipDueToInvalidServicePipDependency,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "The pip '{pipDescription}' could not be added because one of its service pip dependencies is not a service pip).")]
        public abstract void ScheduleFailAddPipDueToInvalidServicePipDependency(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (int)EventId.ScheduleFailAddPipDueToInvalidPreserveOutputWhitelist,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "The pip '{pipDescription}' could not be added because one of PreserveOutputWhitelist is neither static file output nor directory output).")]
        public abstract void ScheduleFailAddPipDueToInvalidPreserveOutputWhitelist(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (int)EventId.ScheduleFailAddPipDueToInvalidAllowPreserveOutputsFlag,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "The pip '{pipDescription}' could not be added because PreserveOutputWhitelist is set even though AllowPreserveOutputs is false for the pip).")]
        public abstract void ScheduleFailAddPipDueToInvalidAllowPreserveOutputsFlag(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (int)EventId.InvalidCopyFilePipDueToSameSourceAndDestinationPath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The copy-file pip '{pipDescription}' could not be added because the path '{filePath}' was used as both its source and destination.")]
        public abstract void ScheduleFailAddCopyFilePipDueToSameSourceAndDestinationPath(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string filePath);

        [GeneratedEvent(
            (int)EventId.InvalidWriteFilePipSinceOutputIsRewritten,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The write-file pip '{pipDescription}' could not be added since it rewrites its destination '{rewrittenFile}'. Write-file pips are not allowed to rewrite outputs, since they do not have any inputs by which to order the rewrite.")]
        public abstract void ScheduleFailAddWriteFilePipSinceOutputIsRewritten(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile);

        [GeneratedEvent(
            (int)EventId.InvalidInputUnderNonReadableRoot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its input '{outputFile}' is under a non-readable mount '{rootDirectory}'.")]
        public abstract void ScheduleFailAddPipInvalidInputUnderNonReadableRoot(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            string rootDirectory);

        [GeneratedEvent(
            (int)EventId.InvalidInputSincePathIsWrittenAndThusNotSource,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added to the build graph because its input '{outputFile}' is produced by pip '{producingPipDesc}'. " +
                "In order for these pips to execute in the correct order, you should reference the value '{producingPipValueId}' rather than a literal path.")]
        public abstract void ScheduleFailAddPipInvalidInputSincePathIsWrittenAndThusNotSource(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            long producingPipSemiStableHash,
            string producingPipDesc,
            string producingPipValueId);

        [GeneratedEvent(
            (int)EventId.InvalidInputSinceCorrespondingOutputIsTemporary,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because it references temporary file '{outputFile}'. That file is produced by the pip '{producingPipDesc}'.")]
        public abstract void ScheduleFailAddPipInvalidInputSinceCorespondingOutputIsTemporary(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            long producingPipSemiStableHash,
            string producingPipDesc,
            string producingPipValueId);

        [GeneratedEvent(
            (int)EventId.InvalidInputSinceInputIsRewritten,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its input '{rewrittenFile}' is re-written to produce a later version. " +
                "Only the final version of a re-written path may be used as a normal input. Consider referencing the later version produced by '{producingPipDescription}'.")]
        public abstract void ScheduleFailAddPipInvalidInputSinceInputIsRewritten(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)EventId.InvalidTempDirectoryInvalidPath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its temp directory '{tempDirectory}' as specified by the environment variable '{tempEnvironmentVariableName}' is not a valid absolute path.")]
        public abstract void ScheduleFailAddPipInvalidTempDirectoryInvalidPath(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string tempDirectory,
            string tempEnvironmentVariableName);

        [GeneratedEvent(
            (int)EventId.InvalidTempDirectoryUnderNonWritableRoot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its temp directory '{tempDirectory}' as specified by the environment variable '{tempEnvironmentVariableName}' is under a non-writable mount '{rootDirectory}'.")]
        public abstract void ScheduleFailAddPipInvalidTempDirectoryUnderNonWritableRoot(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string tempDirectory,
            string rootDirectory,
            string tempEnvironmentVariableName);

        [GeneratedEvent(
            (int)EventId.InvalidOutputSinceOutputIsSource,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{rewrittenFile}' is already declared as a source file.")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceOutputIsSource(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile);

        [GeneratedEvent(
            (int)EventId.InvalidOutputUnderNonWritableRoot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{outputFile}' is under a non-writable mount '{rootDirectory}'.")]
        public abstract void ScheduleFailAddPipInvalidOutputUnderNonWritableRoot(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            string rootDirectory);

        [GeneratedEvent(
            (int)EventId.InvalidOutputSinceDirectoryHasBeenSealed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{rewrittenFile}' would be written under the directory '{sealedDirectoryPath}', which has been sealed by the pip '{producingPipDescription}'. "
                + "The content of a fully-sealed directory can no longer change. Consider adding this file as a dependency of the sealed directory, or changing the directory to be 'partially' sealed.")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceDirectoryHasBeenSealed(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile,
            string sealedDirectoryPath,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)EventId.InvalidInputSinceSourceFileCannotBeInsideOutputDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its input '{rewrittenFile}' is a source file, but is specified to be under the output directory '{sealedDirectoryPath}', which has been added by the pip '{producingPipDescription}'.")]
        public abstract void ScheduleFailAddPipInvalidInputSinceSourceFileCannotBeInsideOutputDirectory(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile,
            string sealedDirectoryPath,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)EventId.InvalidGraphSinceOutputDirectoryContainsSourceFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Invalid graph since '{outputDirectory}', produced by '{outputDirectoryProducerDescription}', contains the source file '{sourceFile}'.")]
        public abstract void ScheduleFailInvalidGraphSinceOutputDirectoryContainsSourceFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string outputDirectory,
            string outputDirectoryProducerDescription,
            string sourceFile);

        [GeneratedEvent(
            (int)EventId.InvalidGraphSinceOutputDirectoryCoincidesSourceFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Invalid graph since '{outputDirectory}', produced by '{outputDirectoryProducerDescription}', coincides with the source file '{sourceFile}'.")]
        public abstract void ScheduleFailInvalidGraphSinceOutputDirectoryCoincidesSourceFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string outputDirectory,
            string outputDirectoryProducerDescription,
            string sourceFile);

        [GeneratedEvent(
            (int)EventId.InvalidGraphSinceOutputDirectoryContainsOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Invalid graph since '{outputDirectory}', produced by '{outputDirectoryProducerDescription}', contains the output file '{outputFile}', produced by '{outputFileProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceOutputDirectoryContainsOutputFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string outputDirectory,
            string outputDirectoryProducerDescription,
            string outputFile,
            string outputFileProducerDescription);

        [GeneratedEvent(
            (int)EventId.InvalidGraphSinceOutputDirectoryCoincidesOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Invalid graph since '{outputDirectory}', produced by '{outputDirectoryProducerDescription}', coincides with the output file '{outputFile}', produced by '{outputFileProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceOutputDirectoryCoincidesOutputFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string outputDirectory,
            string outputDirectoryProducerDescription,
            string outputFile,
            string outputFileProducerDescription);

        [GeneratedEvent(
            (int)EventId.InvalidGraphSinceOutputDirectoryContainsSealedDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Invalid graph since '{outputDirectory}', produced by '{outputDirectoryProducerDescription}', contains the sealed directory '{sealedDirectory}', produced by '{sealedDirectoryProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceOutputDirectoryContainsSealedDirectory(
            LoggingContext context,
            string file,
            int line,
            int column,
            string outputDirectory,
            string outputDirectoryProducerDescription,
            string sealedDirectory,
            string sealedDirectoryProducerDescription);

        [GeneratedEvent(
           (int)EventId.InvalidGraphSinceOutputDirectoryCoincidesSealedDirectory,
           EventGenerators = EventGenerators.LocalOnly,
           EventLevel = Level.Error,
           Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
           EventTask = (int)Events.Tasks.Scheduler,
           Message =
               Events.ProvenancePrefix +
               "Invalid graph since '{outputDirectory}', produced by '{outputDirectoryProducerDescription}', coincides with the sealed directory '{sealedDirectory}', produced by '{sealedDirectoryProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceOutputDirectoryCoincidesSealedDirectory(
           LoggingContext context,
           string file,
           int line,
           int column,
           string outputDirectory,
           string outputDirectoryProducerDescription,
           string sealedDirectory,
           string sealedDirectoryProducerDescription);

        [GeneratedEvent(
            (int)EventId.InvalidGraphSinceSharedOpaqueDirectoryContainsExclusiveOpaqueDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Invalid graph since the shared opaque directory '{sharedOpaqueDirectory}', produced by '{sharedOpaqueDirectoryProducerDescription}', contains the exclusive opaque directory '{exclusiveOpaqueDirectory}', " +
                "produced by '{exclusiveOpaqueDirectoryProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceSharedOpaqueDirectoryContainsExclusiveOpaqueDirectory(
            LoggingContext context,
            string file,
            int line,
            int column,
            string sharedOpaqueDirectory,
            string sharedOpaqueDirectoryProducerDescription,
            string exclusiveOpaqueDirectory,
            string exclusiveOpaqueDirectoryProducerDescription);

        [GeneratedEvent(
            (int)EventId.InvalidGraphSinceFullySealedDirectoryIncomplete,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Fully sealed directories must specify all files contained within the directory. Directory '{sealedDirectory}' does not contain '{missingFile}' which is a file referenced by pip {referencingPip}. Add that file to the Sealed Directory definition to fix this error.")]
        public abstract void InvalidGraphSinceFullySealedDirectoryIncomplete(
            LoggingContext context,
            string file,
            int line,
            int column,
            string sealedDirectory,
            string referencingPip,
            string missingFile);

        [GeneratedEvent(
            (int)EventId.InvalidGraphSinceSourceSealedDirectoryContainsOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Invalid graph since source sealed directory '{sourceSealedDirectory}' contains the output file '{outputFile}', produced by '{outputFileProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceSourceSealedDirectoryContainsOutputFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string sourceSealedDirectory,
            string outputFile,
            string outputFileProducerDescription);

        [GeneratedEvent(
            (int)EventId.InvalidGraphSinceSourceSealedDirectoryCoincidesSourceFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Invalid graph since source sealed directory '{sourceSealedDirectory}' coincides with the source file '{sourceFile}'.")]
        public abstract void ScheduleFailInvalidGraphSinceSourceSealedDirectoryCoincidesSourceFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string sourceSealedDirectory,
            string sourceFile);

        [GeneratedEvent(
            (int)EventId.InvalidGraphSinceSourceSealedDirectoryCoincidesOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Invalid graph since source sealed directory '{sourceSealedDirectory}' coincides with the output file '{outputFile}', produced by '{outputFileProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceSourceSealedDirectoryCoincidesOutputFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string sourceSealedDirectory,
            string outputFile,
            string outputFileProducerDescription);

        [GeneratedEvent(
            (int)EventId.InvalidGraphSinceSourceSealedDirectoryContainsOutputDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "Invalid graph since source sealed directory '{sourceSealedDirectory}', contains the output directory '{outputDirectory}', produced by '{outputDirectoryProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceSourceSealedDirectoryContainsOutputDirectory(
            LoggingContext context,
            string file,
            int line,
            int column,
            string sourceSealedDirectory,
            string outputDirectory,
            string outputDirectoryProducerDescription);

        [GeneratedEvent(
            (int)EventId.InvalidOutputSinceDirectoryHasBeenProducedByAnotherPip,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{directory}' has been produced by another pip '{producingPipDescription}'")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceDirectoryHasBeenProducedByAnotherPip(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string directory,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)EventId.InvalidGraphSinceArtifactPathOverlapsTempPath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Invalid temp path declared. Pips cannot declare artifacts underneath declared temp directories.\r\n{artifactLocation.File}({artifactLocation.Line},{artifactLocation.Position}): [{artifactProducerPip}] declared '{artifactPath}' as an artifact path.\r\n{tempLocation.File}({tempLocation.Line},{tempLocation.Position}): [{tempProducerPip}] declared '{tempPath}' as a temp path.")]
        public abstract void InvalidGraphSinceArtifactPathOverlapsTempPath(
            LoggingContext context,
            Location tempLocation,
            string tempPath,
            string tempProducerPip,
            Location artifactLocation,
            string artifactPath,
            string artifactProducerPip);

        [GeneratedEvent(
            (int)EventId.InvalidOutputSinceOutputIsBothSpecifiedAsFileAndDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{output}' is specified as both file and directory outputs.")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceOutputIsBothSpecifiedAsFileAndDirectory(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string output);

        [GeneratedEvent(
            (int)EventId.SourceDirectoryUsedAsDependency,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its input directory '{path}' is a source directory (only sealed directories can be used as directory inputs).")]
        public abstract void SourceDirectoryUsedAsDependency(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string path);

        [GeneratedEvent(
            (int)EventId.InvalidOutputSinceRewrittenOutputMismatchedWithInput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' rewrites its input dependency '{rewrittenFile}', but that dependency's version does not match the rewritten output. " +
                "It must depend on the immediately prior version of that path, or not depend on that path at all.")]
        public abstract void ScheduleFailAddPipRewrittenOutputMismatchedWithInput(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile);

        [GeneratedEvent(
            (int)EventId.InvalidOutputDueToSimpleDoubleWrite,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because it would produce '{outputFile}' which is already being produced by '{producingPipDescription}'.")]
        public abstract void ScheduleFailAddPipInvalidOutputDueToSimpleDoubleWrite(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)EventId.AllowSameContentPolicyNotAvailableForStaticallyDeclaredOutputs,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                "Pip '{pipDescription}' is participating in a double write to the path '{outputFile}'. The double write policy for this pip is set to allow double writes as long as the content of the produced file is the same. " +
                "However, this policy is only supported for output files under opaque directories, not for statically specified output files. The double write will be flagged as an error regardless of the produced content.")]
        public abstract void AllowSameContentPolicyNotAvailableForStaticallyDeclaredOutputs(
            LoggingContext context,
            string pipDescription,
            string outputFile);

        [GeneratedEvent(
            (int)EventId.RewritingPreservedOutput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' rewrites '{outputFile}' which is already being produced by '{producingPipDescription}' who intends to preserve its outputs.")]
        public abstract void ScheduleAddPipInvalidOutputDueToRewritingPreservedOutput(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)EventId.InvalidOutputSinceRewritingOldVersion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message = Events.ProvenancePrefix + "The pip '{pipDescription}' cannot be added because its output '{outputFile}' has already been declared as being re-written. " +
                      "Re-writes must form a linear sequence (consider re-writing the latest version of the path from the pip '{producingPipDescription}' / value '{producingPipValueId}').")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceRewritingOldVersion(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)EventId.InvalidOutputSinceOutputHasUnexpectedlyHighWriteCount,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{rewrittenFile}' has an unexpectedly high write count. The previous version of that path does not exist. This indicates an error in the build logic.")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceOutputHasUnexpectedlyHighWriteCount(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile);

        [GeneratedEvent(
            (int)EventId.InvalidOutputSincePreviousVersionUsedAsInput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because it declares it will rewrite output '{rewrittenFile}', which has already been specified a a non-rewritten input of another pip. " +
                "Only the final version of a re-written path may be used as a normal input.")]
        public abstract void ScheduleFailAddPipInvalidOutputSincePreviousVersionUsedAsInput(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile);

        [GeneratedEvent(
            (int)EventId.InvalidOutputSinceFileHasBeenPartiallySealed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{rewrittenFile}' has already been sealed as part of '{sealedDirectoryPath}' by the pip '{producingPipDescription}'. "
                + "Files which have been partially or fully sealed may no longer change. Consider sealing the final version of this file.")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceFileHasBeenPartiallySealed(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile,
            string sealedDirectoryPath,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)EventId.DirectoryFingerprintExercisedRule,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "DirectoryFingerprinter exercised exception rule '{0}' for path '{1}'")]
        public abstract void DirectoryFingerprintExercisedRule(LoggingContext context, string ruleName, string path);

        [GeneratedEvent(
            (int)EventId.PathSetValidationTargetFailedAccessCheck,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Diagnostics),
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "{pipDescription} Strong fingerprint could not be computed because FileContentRead for '{path}' is not allowed for the pip because it is not a declared dependency. PathSet will not be usable")]
        public abstract void PathSetValidationTargetFailedAccessCheck(LoggingContext context, string pipDescription, string path);

        [GeneratedEvent(
            (int)EventId.DirectoryFingerprintComputedFromGraph,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Computed static (graph-based) membership fingerprint {1} for process {3} and directory '{0}' [{2} members]")]
        public abstract void DirectoryFingerprintComputedFromGraph(LoggingContext context, string path, string fingerprint, int memberCount, string processDescription);

        [GeneratedEvent(
            (int)EventId.DirectoryFingerprintingFilesystemEnumerationFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Failed to list the contents of the following directory in order to compute its fingerprint: '{0}' ; {1}")]
        public abstract void DirectoryFingerprintingFilesystemEnumerationFailed(LoggingContext context, string path, string failure);

        [GeneratedEvent(
            (int)EventId.DirectoryFingerprintComputedFromFilesystem,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Computed dynamic (filesystem-based) membership fingerprint {1} for directory '{0}' [{2} members]")]
        public abstract void DirectoryFingerprintComputedFromFilesystem(LoggingContext context, string path, string fingerprint, int memberCount);

        [GeneratedEvent(
            (int)EventId.StartFilterApplyTraversal,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            EventOpcode = (byte)EventOpcode.Start,
            Message = Events.PhasePrefix + "Traversing graph applying filter to pips")]
        public abstract void StartFilterApplyTraversal(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.EndFilterApplyTraversal,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Scheduler,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = Events.PhasePrefix + "Done traversing graph applying filter to pips")]
        public abstract void EndFilterApplyTraversal(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.JournalProcessingStatisticsForScheduler,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance | Events.Keywords.Progress),
            Message = Events.PhasePrefix + "USN journal statistics for scheduler: {message}")]
        public abstract void JournalProcessingStatisticsForScheduler(LoggingContext context, string message);

        [GeneratedEvent(
            (int)EventId.JournalProcessingStatisticsForSchedulerTelemetry,
            EventGenerators = EventGenerators.TelemetryOnly | Generators.Statistics,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance | Events.Keywords.Progress),
            Message = "USN journal statistics for scheduler")]
        public abstract void JournalProcessingStatisticsForSchedulerTelemetry(LoggingContext context, string scanningJournalStatus, IDictionary<string, long> stats);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingNewlyPresentFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = Events.ArtifactOrPipChangePrefix + "Newly present file '{path}'")]
        public abstract void IncrementalSchedulingNewlyPresentFile(LoggingContext context, string path);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingNewlyPresentDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = Events.ArtifactOrPipChangePrefix + "Newly present directory '{path}'")]
        public abstract void IncrementalSchedulingNewlyPresentDirectory(LoggingContext context, string path);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingSourceFileIsDirty,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = Events.ArtifactOrPipChangePrefix + "Source file is dirty => Reason: {reason} | Path change reason: {pathChangeReason}")]
        public abstract void IncrementalSchedulingSourceFileIsDirty(LoggingContext context, string reason, string pathChangeReason);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingPipIsDirty,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = Events.ArtifactOrPipChangePrefix + Pip.SemiStableHashPrefix + "{pipHash:X16} is dirty => Reason: {reason} | Path change reason: {pathChangeReason}")]
        public abstract void IncrementalSchedulingPipIsDirty(LoggingContext context, long pipHash, string reason, string pathChangeReason);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingPipIsPerpetuallyDirty,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = Events.ArtifactOrPipChangePrefix + Pip.SemiStableHashPrefix + "{pipHash:X16} is perpetually dirty => Reason: {reason}")]
        public abstract void IncrementalSchedulingPipIsPerpetuallyDirty(LoggingContext context, long pipHash, string reason);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingReadDirtyNodeState,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            Message = "Reading dirty node state file '{path}': Status: {status} | Reason: {reason} | Elapsed time: {elapsedMs}ms")]
        public abstract void IncrementalSchedulingReadDirtyNodeState(LoggingContext context, string path, string status, string reason, long elapsedMs);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingArtifactChangesCounters,
            EventGenerators = EventGenerators.LocalAndTelemetryAndStatistic,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "Artifact changes inferred by journal scanning: Newly added files: {0} | Newly added directories: {1} | Changed static files: {2} | Changed dynamically observed files (possibly path probes): {3} | Changed dynamically observed enumeration memberships: {4} | Perpetually dirty pips: {5}")]
        public abstract void IncrementalSchedulingArtifactChangesCounters(LoggingContext context, long newlyAddedFiles, long newlyAddedDirectories, long changedStaticFiles, long changedDynamicallyObservedFiles, long changedDynamicallyObservedEnumerationMembership, long perpetuallyDirtyPips);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingArtifactChangeSample,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "Samples of changes: \r\n{samples}")]
        public abstract void IncrementalSchedulingArtifactChangeSample(LoggingContext context, string samples);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingAssumeAllPipsDirtyDueToFailedJournalScan,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "Assuming all pips dirty because scanning journal failed: {reason}")]
        public abstract void IncrementalSchedulingAssumeAllPipsDirtyDueToFailedJournalScan(LoggingContext context, string reason);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingAssumeAllPipsDirtyDueToAntiDependency,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "Assuming all pips dirty because anti-dependency is invalidated, i.e., new files are added: {addedFilesCount}")]
        public abstract void IncrementalSchedulingAssumeAllPipsDirtyDueToAntiDependency(LoggingContext context, long addedFilesCount);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingDirtyPipChanges,
            EventGenerators = EventGenerators.LocalAndTelemetryAndStatistic,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "Dirty pips changes: Status: {0} | Initial set of pips after journal scanning: {1} | Pips that get dirtied transitively: {2} | Elapsed time: {3}ms")]
        public abstract void IncrementalSchedulingDirtyPipChanges(LoggingContext context, bool status, long initialDirty, long transitivelyDirty, long elapsedMs);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingPreciseChange,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "Precise change: Status: {0} | Dirty pip changes: {1} | Reason: {2} | Description: {3} | Elapsed time: {4}ms")]
        public abstract void IncrementalSchedulingPreciseChange(LoggingContext context, bool status, bool dirtyNodeChanges, string reason, string description, long elapsedMs);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingIdsMismatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "Loading or reusing incremental scheduling state failed because the new id is less safe than the existing one: \r\nNew id:\r\n{newId}\r\nExisting id:\r\n{existingId}")]
        public abstract void IncrementalSchedulingIdsMismatch(LoggingContext context, string newId, string existingId);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingTokensMismatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "Incremental scheduling state failed to subscribe to file change tracker due to mismatched tokens: Expected token: {expectedToken} | Actual token: {actualToken}")]
        public abstract void IncrementalSchedulingTokensMismatch(LoggingContext context, string expectedToken, string actualToken);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingLoadState,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            Message = "Loading incremental scheduling state at '{path}': Status: {status} | Reason: {reason} | Elapsed time: {elapsedMs}ms")]
        public abstract void IncrementalSchedulingLoadState(LoggingContext context, string path, string status, string reason, long elapsedMs);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingReuseState,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            Message = "Attempt to reuse existing incremental scheduling state: {reason}")]
        public abstract void IncrementalSchedulingReuseState(LoggingContext context, string reason);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingSaveState,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            Message = "Saving incremental scheduling state at '{path}': Status: {status} | Reason: {reason} | Elapsed time: {elapsedMs}ms")]
        public abstract void IncrementalSchedulingSaveState(LoggingContext context, string path, string status, string reason, long elapsedMs);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingProcessGraphChange,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Performance),
            Message = "Processing graph change to update incremental scheduling state: Loaded graph id: {loadedGraphId} | New graph id: {newGraphId} | Elapsed time: {elapsedMs}ms")]
        public abstract void IncrementalSchedulingProcessGraphChange(LoggingContext context, string loadedGraphId, string newGraphId, long elapsedMs);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingProcessGraphChangeGraphId,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage),
            Message = "Processing graph change to update incremental scheduling state: Has seen the graph: {status} | Graph id: {graphId} | Date seen: {dateSeen}")]
        public abstract void IncrementalSchedulingProcessGraphChangeGraphId(LoggingContext context, string status, string graphId, string dateSeen);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingProcessGraphChangeProducerChange,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage),
            Message = Events.ArtifactOrPipChangePrefix + " Producer of a path has changed: Path: {path} | New producer: Pip{newProducerHash:X16} | Old producer fingerprint: {oldProducerFingerprint}")]
        public abstract void IncrementalSchedulingProcessGraphChangeProducerChange(LoggingContext context, string path, long newProducerHash, string oldProducerFingerprint);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingProcessGraphChangePathNoLongerSourceFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage),
            Message = Events.ArtifactOrPipChangePrefix + " Path {path} is no longer a source file.")]
        public abstract void IncrementalSchedulingProcessGraphChangePathNoLongerSourceFile(LoggingContext context, string path);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingPipDirtyAcrossGraphBecauseSourceIsDirty,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage),
            Message = Events.ArtifactOrPipChangePrefix + " Pip{pipHash:X16} is dirty across graph because source file '{sourceFile}' is considered dirty")]
        public abstract void IncrementalSchedulingPipDirtyAcrossGraphBecauseSourceIsDirty(LoggingContext context, long pipHash, string sourceFile);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingPipDirtyAcrossGraphBecauseDependencyIsDirty,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage),
            Message = Events.ArtifactOrPipChangePrefix + " Pip{pipHash:X16} is dirty across graph because its dependency Pip{depPipHash:X16} ({depFingerprint}) is considered dirty")]
        public abstract void IncrementalSchedulingPipDirtyAcrossGraphBecauseDependencyIsDirty(LoggingContext context, long pipHash, long depPipHash, string depFingerprint);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingSourceFileOfOtherGraphIsDirtyDuringScan,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage),
            Message = Events.ArtifactOrPipChangePrefix + " Source file '{sourceFile}' of other graphs is dirty => Path change reason: {pathChangeReason}")]
        public abstract void IncrementalSchedulingSourceFileOfOtherGraphIsDirtyDuringScan(LoggingContext context, string sourceFile, string pathChangeReason);

        [GeneratedEvent(
            (int)EventId.IncrementalSchedulingPipOfOtherGraphIsDirtyDuringScan,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage),
            Message = Events.ArtifactOrPipChangePrefix + " Pip with fingerprint {pipFingerprint} of other graph is dirty => Path: {path} | Path change reason: {pathChangeReason}")]
        public abstract void IncrementalSchedulingPipOfOtherGraphIsDirtyDuringScan(LoggingContext context, string pipFingerprint, string path, string pathChangeReason);

        [GeneratedEvent(
           (int)EventId.IncrementalSchedulingPipDirtyDueToChangesInDynamicObservationAfterScan,
           EventGenerators = EventGenerators.LocalAndTelemetryAndStatistic,
           EventLevel = Level.Verbose,
           EventTask = (ushort)Events.Tasks.Scheduler,
           Keywords = (int)Events.Keywords.UserMessage,
           Message = "Dirty pips due to changes in dynamic observation after journal scan: Dynamic paths: {dynamicPathCount} | Dynamic path enumerations: {dynamicPathEnumerationCount} | Elapsed time: {elapsedMs}ms")]
        public abstract void IncrementalSchedulingPipDirtyDueToChangesInDynamicObservationAfterScan(LoggingContext context, int dynamicPathCount, int dynamicPathEnumerationCount, long elapsedMs);

        [GeneratedEvent(
           (int)EventId.IncrementalSchedulingPipsOfOtherPipGraphsGetDirtiedAfterScan,
           EventGenerators = EventGenerators.LocalAndTelemetryAndStatistic,
           EventLevel = Level.Verbose,
           EventTask = (ushort)Events.Tasks.Scheduler,
           Keywords = (int)Events.Keywords.UserMessage,
           Message = "Dirty pips belonging to other pip graphs after journal scan: Pips: {pipCount} | Elapsed time: {elapsedMs}ms")]
        public abstract void IncrementalSchedulingPipsOfOtherPipGraphsGetDirtiedAfterScan(LoggingContext context, int pipCount, long elapsedMs);

        [GeneratedEvent(
            (ushort)EventId.ServicePipStarting,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Starting service pip")]
        internal abstract void ScheduleServicePipStarting(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.ServicePipShuttingDown,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{servicePipDescription}] Shutting down service pip")]
        internal abstract void ScheduleServicePipShuttingDown(LoggingContext loggingContext, string servicePipDescription, string shutdownPipDescription);

        [GeneratedEvent(
            (ushort)EventId.ServicePipTerminatedBeforeStartupWasSignaled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{pipDescription}] Service pip terminated before its startup was signaled")]
        internal abstract void ScheduleServiceTerminatedBeforeStartupWasSignaled(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.ServicePipFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{servicePipDescription}] Service pip failed")]
        internal abstract void ScheduleServicePipFailed(LoggingContext loggingContext, string servicePipDescription);

        [GeneratedEvent(
            (ushort)EventId.ServicePipShuttingDownFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{shutdownPipDescription}] Service shutdown pip failed")]
        internal abstract void ScheduleServicePipShuttingDownFailed(LoggingContext loggingContext, string servicePipDescription, string shutdownPipDescription);

        [GeneratedEvent(
            (ushort)EventId.IpcClientForwardedMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "IPC pip logged a message: [{level}] {message}")]
        internal abstract void IpcClientForwardedMessage(LoggingContext loggingContext, string level, string message);

        [GeneratedEvent(
            (ushort)EventId.IpcClientFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "IPC client failed: {exceptionMessage}")]
        internal abstract void IpcClientFailed(LoggingContext loggingContext, string exceptionMessage);

        [GeneratedEvent(
            (ushort)EventId.ApiServerForwarderIpcServerMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{ShortProductName} API Server] IPC server logged a message: [{level}] {message}")]
        internal abstract void ApiServerForwardedIpcServerMessage(LoggingContext loggingContext, string level, string message);

        [GeneratedEvent(
            (ushort)EventId.ApiServerOperationReceived,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{ShortProductName} API Server] Received operation: '{operation}'.")]
        internal abstract void ApiServerOperationReceived(LoggingContext loggingContext, string operation);

        [GeneratedEvent(
            (ushort)EventId.ApiServerInvalidOperation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{ShortProductName} API Server] Received invalid operation: '{operation}'. {reason}")]
        internal abstract void ApiServerInvalidOperation(LoggingContext loggingContext, string operation, string reason);

        [GeneratedEvent(
            (ushort)EventId.ApiServerMaterializeFileExecuted,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{ShortProductName} API Server] Operation MaterializeFile('{file}') executed. Succeeded: {succeeded}.")]
        internal abstract void ApiServerMaterializeFileExecuted(LoggingContext loggingContext, string file, bool succeeded);

        [GeneratedEvent(
            (ushort)EventId.ApiServerReportStatisticsExecuted,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{ShortProductName} API Server] Operation ReportStatistics executed; {numStatistics} statistics reported.")]
        internal abstract void ApiServerReportStatisticsExecuted(LoggingContext loggingContext, int numStatistics);

        [GeneratedEvent(
            (ushort)EventId.ApiServerGetSealedDirectoryContentExecuted,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Message = "[{ShortProductName} API Server] Operation GetSealedDirectoryContent('{directory}') executed.")]
        internal abstract void ApiServerGetSealedDirectoryContentExecuted(LoggingContext loggingContext, string directory);
        
        [GeneratedEvent(
            (ushort)LogEventId.UnexpectedlySmallObservedInputCount,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = Events.ProvenancePrefix +
                "Pip '{pipDescription}' had an expectedly small observed input count. The largest pathset for this fingerprint had: [AbsentFileProbes:{maxAbsentFileProbe}, DirectoryEnumerationCount:{maxDirectoryEnumerations}, ExistingDirectoryProbeCount,{maxDirectoryProbes}, FileContentReadCount:{maxFileContentReads}]. " +
            "The pathset for this run had: [AbsentFileProbes:{currentAbsentFileProbe}, DirectoryEnumerationCount:{currentDirectoryEnumerations}, ExistingDirectoryProbeCount,{currentDirectoryProbes}, FileContentReadCount:{currentFileContentReads}], ExistingFileProbeCount:{currentExistingFileProbes}].")]
        public abstract void UnexpectedlySmallObservedInputCount(LoggingContext loggingContext, string pipDescription, int maxAbsentFileProbe, int maxDirectoryEnumerations, int maxDirectoryProbes, int maxFileContentReads,
            int currentAbsentFileProbe, int currentDirectoryEnumerations, int currentDirectoryProbes, int currentFileContentReads, int currentExistingFileProbes);

        [GeneratedEvent(
            (int)LogEventId.PerformanceDataCacheTrace,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "{message}")]
        public abstract void PerformanceDataCacheTrace(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.HistoricMetadataCacheTrace,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "{message}")]
        public abstract void HistoricMetadataCacheTrace(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.HistoricMetadataCacheCreateFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Failed to create historic metadata cache: {message}. This does not fail the build, but may impact performance.")]
        public abstract void HistoricMetadataCacheCreateFailed(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.HistoricMetadataCacheOperationFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Historic metadata cache operation failed, further access is disabled: {message}. This does not fail the build, but may impact performance.")]
        public abstract void HistoricMetadataCacheOperationFailed(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.HistoricMetadataCacheSaveFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Historic metadata cache save failed: {message}. This does not fail the build, but may impact performance.")]
        public abstract void HistoricMetadataCacheSaveFailed(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.HistoricMetadataCacheLoadFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Historic metadata cache load failed: {message}. This does not fail the build, but may impact performance.")]
        public abstract void HistoricMetadataCacheLoadFailed(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.HistoricMetadataCacheCloseCalled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Historic metadata close called.")]
        public abstract void HistoricMetadataCacheCloseCalled(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.CriticalPathPipRecord,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.CriticalPaths,
            Message = "Critical Path Pip Duration={pipDurationMs}ms Result={executionLevel} ExplicitlyScheduled={isExplicitlyScheduled} Index={indexFromBeginning} {pipDescription}")]
        public abstract void CriticalPathPipRecord(
            LoggingContext context, 
            long pipSemiStableHash, 
            string pipDescription,
            long pipDurationMs, 
            long exeDurationMs, 
            long queueDurationMs, 
            int indexFromBeginning, 
            bool isExplicitlyScheduled, 
            string executionLevel, 
            int numCacheEntriesVisited, 
            int numPathSetsDownloaded);

        [GeneratedEvent(
            (int)LogEventId.CriticalPathChain,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.CriticalPaths,
            Message = "{0}")]
        public abstract void CriticalPathChain(LoggingContext context, string criticalPathMessage);

        #region Symlink file

        [GeneratedEvent(
            (int)LogEventId.FailedLoadSymlinkFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Engine,
            Message = "Failed to load symlink file: {message}.")]
        public abstract void FailedLoadSymlinkFile(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToCreateSymlinkFromSymlinkMap,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Failed to create symlink from '{source}' to '{target}': {message}")]
        public abstract void FailedToCreateSymlinkFromSymlinkMap(LoggingContext loggingContext, string source, string target, string message);

        [GeneratedEvent(
            (ushort)LogEventId.CreateSymlinkFromSymlinkMap,
            EventGenerators = EventGenerators.LocalAndTelemetryAndStatistic,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Symlink creations: Created symlinks: {createdSymlinkCount} | Reuse symlinks: {reuseSymlinkCount} | Failed creations: {failedSymlinkCount} | Elapsed time: {createSymlinkDurationMs}ms")]
        public abstract void CreateSymlinkFromSymlinkMap(LoggingContext loggingContext, int createdSymlinkCount, int reuseSymlinkCount, int failedSymlinkCount, int createSymlinkDurationMs);

        [GeneratedEvent(
            (int)LogEventId.SymlinkFileTraceMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Engine,
            Message = "{message}")]
        public abstract void SymlinkFileTraceMessage(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.UnexpectedAccessOnSymlinkPath,
            EventGenerators = EventGenerators.LocalOnly,
            // TODO: Should this be informational?
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Engine,
            Message = "[{pipDescription}] Unexpected access on symlink {pathKind} path '{path}': {inputType} (Tools: {tools}).")]
        public abstract void UnexpectedAccessOnSymlinkPath(LoggingContext context, string pipDescription, string path, string pathKind, string inputType, string tools);

        #endregion

        #region Preserved output tracker

        [GeneratedEvent(
            (int)LogEventId.SavePreservedOutputsTracker,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Engine,
            Message = "Save preserved output tracker file at '{path}' with preserved output salt '{salt}'")]
        public abstract void SavePreservedOutputsTracker(LoggingContext context, string path, string salt);

        #endregion

        #region Determinism probe to detect nondeterministic PIPs

        [GeneratedEvent(
            (ushort)EventId.DeterminismProbeEncounteredNondeterministicOutput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Produces inconsistent output: {file} - {cache} in cache vs {execution} executed")]
        internal abstract void DeterminismProbeEncounteredNondeterministicOutput(LoggingContext loggingContext, string pipDescription, string file, string cache, string execution);

        [GeneratedEvent(
            (ushort)EventId.DeterminismProbeEncounteredProcessThatCannotRunFromCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Cannot run from cache, preventing the check for determinism")]
        internal abstract void DeterminismProbeEncounteredProcessThatCannotRunFromCache(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.DeterminismProbeEncounteredUnexpectedStrongFingerprintMismatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Consumes inconsistent input, indicated by strong fingerprint differences: {cached} (path-set {cachedPathSet}) in cache vs {executed} (path-set {executedPathSet}) executed")]
        internal abstract void DeterminismProbeEncounteredUnexpectedStrongFingerprintMismatch(LoggingContext loggingContext, string pipDescription, string cached, string cachedPathSet, string executed, string executedPathSet);

        [GeneratedEvent(
            (ushort)EventId.DeterminismProbeEncounteredPipFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Behaves inconsistently, currently failing but succeeded during a prior run")]
        internal abstract void DeterminismProbeEncounteredPipFailure(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.DeterminismProbeEncounteredUncacheablePip,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Behaves inconsistently, currently uncacheable, but it was cacheable in a prior run")]
        internal abstract void DeterminismProbeEncounteredUncacheablePip(LoggingContext loggingContext, string pipDescription);

        [GeneratedEvent(
            (ushort)EventId.DeterminismProbeDetectedUnexpectedMismatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] {message}")]
        internal abstract void DeterminismProbeDetectedUnexpectedMismatch(LoggingContext loggingContext, string pipDescription, string message);

        [GeneratedEvent(
            (ushort)EventId.DeterminismProbeEncounteredOutputDirectoryDifferentFiles,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Produces inconsistent sets of files in directory output {directory}:\nOnly present in cache entry:\n{cache}Only present during execution:\n{execution}")]
        internal abstract void DeterminismProbeEncounteredOutputDirectoryDifferentFiles(LoggingContext loggingContext, string pipDescription, string directory, string cache, string execution);

        [GeneratedEvent(
            (ushort)EventId.DeterminismProbeEncounteredNondeterministicDirectoryOutput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.PipExecutor,
            Message = "[{pipDescription}] Produces inconsistent file in directory output {directory}: {file} - {cache} in cache vs {execution} executed")]
        internal abstract void DeterminismProbeEncounteredNondeterministicDirectoryOutput(LoggingContext loggingContext, string pipDescription, string directory, string file, string cache, string execution);

        #endregion

        [GeneratedEvent(
            (int)EventId.DirtyBuildExplicitlyRequestedModules,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "Module dirty build is enabled. Here are the modules of filter passing nodes: '{modules}'")]
        public abstract void DirtyBuildExplicitlyRequestedModules(LoggingContext context, string modules);

        [GeneratedEvent(
            (int)EventId.DirtyBuildProcessNotSkippedDueToMissingOutput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "The pip '{pipDescription}' cannot be skipped because at least one output of this pip is missing on disk: '{path}'. The consumer pip '{consumerDescription}'.")]
        public abstract void DirtyBuildProcessNotSkippedDueToMissingOutput(LoggingContext context, string pipDescription, string path, string consumerDescription);

        [GeneratedEvent(
            (int)EventId.DirtyBuildProcessNotSkipped,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "The pip '{pipDescription}' cannot be skipped because {reason}.")]
        public abstract void DirtyBuildProcessNotSkipped(LoggingContext context, string pipDescription, string reason);

        [GeneratedEvent(
            (int)EventId.DirtyBuildStats,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "Dirty build statistics. Elapsed decision time: {0} ms, DirtyModule enabled: {1}, # Explicitly selected processes: {2}, # Scheduled processes: {3}, # Must executed processes: {4}, # Skipped processes: {5}.")]
        public abstract void DirtyBuildStats(LoggingContext context, long durationMs, bool isDirtyModule, int numExplicitlySelectedProcesses, int numScheduledProcesses, int numBeExecutedProcesses, int skippedProcesses);

        [GeneratedEvent(
            (int)EventId.MinimumWorkersNotSatisfied,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.InfrastructureError),
            Message = "Minimum workers not satisfied. # Minimum workers: {0}, # Connected workers: {1}")]
        public abstract void MinimumWorkersNotSatisfied(LoggingContext context, int minimumWorkers, int connectedWorkers);

        [GeneratedEvent(
            (int)EventId.BuildSetCalculatorProcessStats,
            EventGenerators = EventGenerators.LocalAndTelemetryAndStatistic,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message =
                "Build set calculator: Processes in graph: {processesInGraph} | Explicitly selected processes: {explicitlySelectedProcesses} | Processes need to be built: {processesInBuildCone} | Processes skipped by incremental scheduling: {processesSkippedByIncrementalScheduling} | Scheduled processes: {scheduledProcesses} |  | Elapsed time: {buildSetCalculatorDurationMs}ms")]
        public abstract void BuildSetCalculatorProcessStats(
            LoggingContext context,
            int processesInGraph,
            int explicitlySelectedProcesses,
            int processesInBuildCone,
            int processesSkippedByIncrementalScheduling,
            int scheduledProcesses,
            int buildSetCalculatorDurationMs);

        [GeneratedEvent(
            (int)EventId.BuildSetCalculatorStats,
            EventGenerators = EventGenerators.LocalAndTelemetryAndStatistic,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message =
                "Build set calculator: \r\n\tComputing build cone: [Dirty processes: {dirtyProcesses} out of {explicitlySelectedProcesses} explicitly selected ({nonMaterializedProcesses} due to non-materialized) | Dirty pips: {dirtyNodes} out of {explicitlySelectedNodes} explicitly selected ({nonMaterializedNodes} due to non-materialized) | Elapsed time: {elapsedConeBuild}ms]\r\n\tGetting scheduled pips: [Scheduled pips: {scheduledNodes} ({scheduledProcesses} processes, {metaNodes} meta pips)| Elapsed time: {getScheduledNodesDurationMs}ms]")]
        public abstract void BuildSetCalculatorStats(
            LoggingContext context,
            int dirtyNodes,
            int dirtyProcesses,
            int explicitlySelectedNodes,
            int explicitlySelectedProcesses,
            int nonMaterializedNodes,
            int nonMaterializedProcesses,
            int elapsedConeBuild,
            int scheduledNodes,
            int scheduledProcesses,
            int metaNodes,
            int getScheduledNodesDurationMs);

        [GeneratedEvent(
            (int)EventId.BuildSetCalculatorScheduleDependenciesUntilCleanAndMaterializedStats,
            EventGenerators = EventGenerators.LocalAndTelemetryAndStatistic,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)Events.Keywords.UserMessage,
            Message =
                "Build set calculator to schedule dependencies until clean and materialized: Initial pips: {initialNodes} ({initialProcesses} processes) | Pips added due to not clean-materialized: {nodesAddedDueToNotCleanMaterialized} ({processesAddedDueToNotCleanMaterialized} processes) | Pips added due to collateral dirty: {nodesAddedDueToCollateralDirty} ({processesAddedDueToCollateralDirty} processes) | Pips added as clean-materialized frontier: {nodesAddedDueToCleanMaterialized} ({processesAddedDueToCleanMaterialized} processes) | Elapsed time: {scheduleDependenciesUntilCleanAndMaterializedDurationMs}ms")]
        public abstract void BuildSetCalculatorScheduleDependenciesUntilCleanAndMaterializedStats(
            LoggingContext context,
            int initialNodes,
            int initialProcesses,
            int nodesAddedDueToNotCleanMaterialized,
            int processesAddedDueToNotCleanMaterialized,
            int nodesAddedDueToCollateralDirty,
            int processesAddedDueToCollateralDirty,
            int nodesAddedDueToCleanMaterialized,
            int processesAddedDueToCleanMaterialized,
            int scheduleDependenciesUntilCleanAndMaterializedDurationMs);

        [GeneratedEvent(
            (ushort)LogEventId.LimitingResourceStatistics,
            EventGenerators = EventGenerators.TelemetryOnly | Generators.Statistics,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Events.Keywords.Diagnostics)]
        public abstract void LimitingResourceStatistics(LoggingContext context, IDictionary<string, long> statistics);

        [GeneratedEvent(
            (int) EventId.FailedToDuplicateSchedulerFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort) Events.Tasks.Scheduler,
            Keywords = (int) Events.Keywords.UserMessage,
            Message = "Failed to duplicate scheduler file '{sourcePath}' to '{destinationPath}': {reason}")]
        public abstract void FailedToDuplicateSchedulerFile(LoggingContext context, string sourcePath, string destinationPath, string reason);

        [GeneratedEvent(
            (int)EventId.KextFailedToInitializeConnectionManager,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Error,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.InfrastructureError),
            Message = "Failed to initialize the sandbox kernel extension connection manager: {reason}")]
        public abstract void KextFailedToInitializeConnectionManager(LoggingContext context, string reason);

        [GeneratedEvent(
            (int)EventId.KextFailureNotificationReceived,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Error,
            EventTask = (ushort)Events.Tasks.Scheduler,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.InfrastructureError),
             Message = "Received unrecoverable error from sandbox kernel extension, please reload the extension and retry, tweaking configuration parameters if necessary (e.g., /numberOfKextConnections, /reportQueueSizeMb).  Error code: {errorCode}.  Additional description: {description}.")]
        public abstract void KextFailureNotificationReceived(LoggingContext context, int errorCode, string description);

        [GeneratedEvent(
            (ushort)EventId.LowMemory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "Machine ran out of physical ram and had to fall back to the page file. This can dramatically impact build performance. Either too much concurrency was used during the build or the memory throttling options were not effective. Try adjusting the following options: /maxproc, /maxRamUtilizationPercentage, /minAvailableRamMb. See verbose help text for details: {MainExecutableName} /help:verbose",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void LowMemory(LoggingContext context, long machineMinimumAvailablePhysicalMB);

        [GeneratedEvent(
            (int)EventId.InvalidSharedOpaqueDirectoryDueToOverlap,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
                Events.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its shared output directory '{sharedOutputDirectory}' would be under the scope of the shared output directory '{parentSharedOutputDirectory}'. "
                + "Shared output directories specified by the same pip should not be within each other.")]
        public abstract void ScheduleFailAddPipInvalidSharedOpaqueDirectoryDueToOverlap(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string sharedOutputDirectory,
            string parentSharedOutputDirectory);

        [GeneratedEvent(
            (int)EventId.VirtualizationFilterDetachError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "Error detaching virtualization filter. {errorDetail}")]
        public abstract void VirtualizationFilterDetachError(
            LoggingContext context,
            string errorDetail);

        [GeneratedEvent(
            (ushort)EventId.CacheMissAnalysis,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Cache miss analysis for {pipDescription}. Is it from cache lookup: {fromCacheLookup}\r\n{reason}\r\n")]
        public abstract void CacheMissAnalysis(LoggingContext loggingContext, string pipDescription, string reason, bool fromCacheLookup);

        [GeneratedEvent(
            (ushort)EventId.CacheMissAnalysisException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Cache miss analysis failed for {pipDescription} with exception: {exception}\r\nOld entry keys:\r\n{oldEntry}\r\nNew entry keys:\r\n{newEntry}")]
        public abstract void CacheMissAnalysisException(LoggingContext loggingContext, string pipDescription, string exception, string oldEntry, string newEntry);

        [GeneratedEvent(
            (int)EventId.MissingKeyWhenSavingFingerprintStore,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Fingerprint store cannot be saved to cache because no fingerprint store key information was given to {ShortProductName}: /traceInfo:fingerprintStoreKey=<value>")]
        public abstract void MissingKeyWhenSavingFingerprintStore(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.FingerprintStoreSavingFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Failed to save fingerprint store in cache: {reason}. This does not fail the build.")]
        public abstract void FingerprintStoreSavingFailed(LoggingContext context, string reason);

        [GeneratedEvent(
            (int)EventId.FingerprintStoreToCompareTrace,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "{message}")]
        public abstract void GettingFingerprintStoreTrace(LoggingContext context, string message);

        [GeneratedEvent(
            (int)EventId.SuccessLoadFingerprintStoreToCompare,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Successfully loaded the fingerprint store to compare. Mode: {mode}, path: {path}")]
        public abstract void SuccessLoadFingerprintStoreToCompare(LoggingContext context, string mode, string path);

        [GeneratedEvent(
            (int)EventId.FileArtifactContentMismatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message = "File '{fileArtifact}' was reported multiple times with different content hashes (old hash: {existingHash}, new hash: {newHash}). " +
            "This indicates a double write violation that can lead to an unreliable build because consumers of this file may see different contents of the file during the build. " +
            "This violation is potentially caused by /unsafe_UnexpectedFileAccessesAreErrors-.")]
        public abstract void FileArtifactContentMismatch(
            LoggingContext context,
            string fileArtifact,
            string existingHash,
            string newHash);

        [GeneratedEvent(
            (int)EventId.PreserveOutputsDoNotApplyToSharedOpaques,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
            "[{pipDescription}] This pip specifies shared opaque directories, but the option to preserve pip outputs is enabled. " +
            "Outputs produced in shared opaque directories are never preserved, even if this option is on.")]
        public abstract void PreserveOutputsDoNotApplyToSharedOpaques(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (int)EventId.DeleteFullySealDirectoryUnsealedContents,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.Scheduler,
            Message =
            "[{pipDescription}] '{directoryPath}' is a fully seal directory. Perform a scrubbing to delete unsealed contents. Deleted content list:\n{deletedPaths}")]
        public abstract void DeleteFullySealDirectoryUnsealedContents(
            LoggingContext context,
            string directoryPath,
            string pipDescription,
            string deletedPaths);
    }
}
#pragma warning restore CA1823 // Unused field
