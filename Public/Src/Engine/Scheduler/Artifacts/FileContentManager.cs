// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;
using Logger = BuildXL.Scheduler.Tracing.Logger;

namespace BuildXL.Scheduler.Artifacts
{
    /// <summary>
    /// This class tracks content hashes and materialization state of files allowing
    /// managing hashing and materialization of pip inputs and outputs. As pips run
    /// they report content (potentially not materialized). Later pips may request materialization
    /// of inputs whose content must have been reported by an earlier operation (either through
    /// <see cref="ReportOutputContent"/>, <see cref="ReportInputContent"/>, <see cref="TryHashDependenciesAsync"/>,
    /// or <see cref="TryHashOutputsAsync"/>.
    ///
    /// Reporting:
    /// * <see cref="ReportOutputContent"/> is used to report the output content of all pips except hash source file
    /// * <see cref="ReportDynamicDirectoryContents"/> is used to report the files produced into dynamic output directories
    /// * <see cref="TryHashOutputsAsync"/> is used to report content for the hash source file pip or during incremental scheduling.
    /// * <see cref="TryHashDependenciesAsync"/> is used to report content for inputs prior to running/cache lookup.
    /// * <see cref="ReportInputContent"/> is used to report the content of inputs (distributed workers only since prerequisite pips
    /// will run on the worker and report output content)
    ///
    /// Querying:
    /// * <see cref="GetInputContent"/> retrieves hash of reported content
    /// * <see cref="TryQuerySealedOrUndeclaredInputContentAsync"/> gets the hash of the input file inside a sealed directory or a file that
    /// is outside any declared containers, but allowed undeclared reads are on.
    /// This may entail hashing the file.
    /// * <see cref="ListSealedDirectoryContents"/> gets the files inside a sealed directory (including dynamic directories which
    /// have been reported via <see cref="ReportDynamicDirectoryContents"/>.
    ///
    /// Materialization:
    /// At a high level materialization has the following workflow
    /// * Get files/directories to materialize
    /// * Delete materialized output directories
    /// * Delete files required to be absent (reported with absent file hash)
    /// * Pin hashes of existent files in the cache
    /// * Place existent files from cache
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    public sealed class FileContentManager : IQueryableFileContentManager
    {
        private const int MAX_SYMLINK_TRAVERSALS = 100;
        private readonly ITempDirectoryCleaner m_tempDirectoryCleaner;

        #region Internal State

        /// <summary>
        /// Cached completed pip output origin tasks
        /// </summary>
        private readonly ReadOnlyArray<Task<PipOutputOrigin>> m_originTasks =
            ReadOnlyArray<Task<PipOutputOrigin>>.From(EnumTraits<PipOutputOrigin>.EnumerateValues()
            .Select(origin => Task.FromResult(origin)));

        private static readonly Task<FileMaterializationInfo?> s_placeHolderFileHashTask = Task.FromResult<FileMaterializationInfo?>(null);

        /// <summary>
        /// Statistics for file content operations
        /// </summary>
        private FileContentStats m_stats;

        /// <summary>
        /// Gets the current file content stats
        /// </summary>
        public FileContentStats FileContentStats => m_stats;

        /// <summary>
        /// Dictionary of number of cache content hits by cache name.
        /// </summary>
        private readonly ConcurrentDictionary<string, int> m_cacheContentSource = new ConcurrentDictionary<string, int>();

        // Semaphore to limit concurrency for cache IO calls
        private readonly SemaphoreSlim m_materializationSemaphore = new SemaphoreSlim(EngineEnvironmentSettings.MaterializationConcurrency);

        /// <summary>
        /// Pending materializations
        /// </summary>
        private readonly ConcurrentBigMap<FileArtifact, Task<PipOutputOrigin>> m_materializationTasks = new ConcurrentBigMap<FileArtifact, Task<PipOutputOrigin>>();

        /// <summary>
        /// Map of deletion tasks for materialized directories
        /// </summary>
        private readonly ConcurrentBigMap<DirectoryArtifact, Task<bool>> m_dynamicDirectoryDeletionTasks = new ConcurrentBigMap<DirectoryArtifact, Task<bool>>();

        /// <summary>
        /// The directories which have already been materialized
        /// </summary>
        private readonly ConcurrentBigSet<DirectoryArtifact> m_materializedDirectories = new ConcurrentBigSet<DirectoryArtifact>();

        /// <summary>
        /// File hashing tasks for tracking completion of hashing. Entries here are transient and only used to ensure
        /// a file is hashed only once
        /// </summary>
        private readonly ConcurrentBigMap<FileArtifact, Task<FileMaterializationInfo?>> m_fileArtifactHashTasks =
            new ConcurrentBigMap<FileArtifact, Task<FileMaterializationInfo?>>();

        /// <summary>
        /// The contents of dynamic output directories
        /// </summary>
        private readonly ConcurrentBigMap<DirectoryArtifact, SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>> m_sealContents =
            new ConcurrentBigMap<DirectoryArtifact, SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>>();

        /// <summary>
        /// Current materializations for files by their path. Allows ensuring that latest rewrite count is the only file materialized
        /// </summary>
        private readonly ConcurrentBigMap<AbsolutePath, FileArtifact> m_currentlyMaterializingFilesByPath = new ConcurrentBigMap<AbsolutePath, FileArtifact>();

        /// <summary>
        /// All the sealed files for registered sealed directories. Maps the sealed path to the file artifact which was sealed. We always seal at a
        /// particular rewrite count.
        /// </summary>
        private readonly ConcurrentBigMap<AbsolutePath, FileArtifact> m_sealedFiles = new ConcurrentBigMap<AbsolutePath, FileArtifact>();

        /// <summary>
        /// The registered seal directories (all the files in the directory should be in <see cref="m_sealedFiles"/> unless it is a sealed source directory
        /// </summary>
        private readonly ConcurrentBigSet<DirectoryArtifact> m_registeredSealDirectories = new ConcurrentBigSet<DirectoryArtifact>();

        /// <summary>
        /// Maps paths to the corresponding seal source directory artifact
        /// </summary>
        private readonly ConcurrentBigMap<AbsolutePath, DirectoryArtifact> m_sealedSourceDirectories =
            new ConcurrentBigMap<AbsolutePath, DirectoryArtifact>();

        /// <summary>
        /// Pool of artifact state objects contain state used during hashing and materialization operations
        /// </summary>
        private readonly ConcurrentQueue<PipArtifactsState> m_statePool = new ConcurrentQueue<PipArtifactsState>();

        /// <summary>
        /// Content hashes for source and output artifacts. Source artifacts have content hashes that are known statically,
        /// whereas output artifacts have hashes that are determined by tool execution (including cached execution).
        /// </summary>
        private readonly ConcurrentBigMap<FileArtifact, FileMaterializationInfo> m_fileArtifactContentHashes =
            new ConcurrentBigMap<FileArtifact, FileMaterializationInfo>();

        /// <summary>
        /// The distinct content used during the build and associated hashes. A <see cref="ContentId"/> represents
        /// a pairing of file and hash (via an index into <see cref="m_fileArtifactContentHashes"/>). In this set, the hash is used as the key.
        /// </summary>
        private readonly ConcurrentBigSet<ContentId> m_allCacheContentHashes = new ConcurrentBigSet<ContentId>();

        /// <summary>
        /// Set of paths for which <see cref="TryQueryContentAsync"/> was called which were determined to be a directory
        /// </summary>
        private readonly ConcurrentBigSet<AbsolutePath> m_contentQueriedDirectoryPaths = new ConcurrentBigSet<AbsolutePath>();

        /// <summary>
        /// Maps files in registered dynamic output directories to the corresponding dynamic output directory artifact
        /// </summary>
        private readonly ConcurrentBigMap<FileArtifact, DirectoryArtifact> m_dynamicOutputFileDirectories =
            new ConcurrentBigMap<FileArtifact, DirectoryArtifact>();

        /// <summary>
        /// Symlink definitions.
        /// </summary>
        private readonly SymlinkDefinitions m_symlinkDefinitions;

        /// <summary>
        /// Flag indicating if symlink should be created lazily.
        /// </summary>
        private bool LazySymlinkCreation => m_host.Configuration.Schedule.UnsafeLazySymlinkCreation && m_symlinkDefinitions != null;
        #endregion

        #region External State (i.e. passed into constructor)

        private PipExecutionContext Context => m_host.Context;

        private SemanticPathExpander SemanticPathExpander => m_host.SemanticPathExpander;

        private LocalDiskContentStore LocalDiskContentStore => m_host.LocalDiskContentStore;

        private IArtifactContentCache ArtifactContentCache { get; }

        private IConfiguration Configuration => m_host.Configuration;

        private IExecutionLogTarget ExecutionLog => m_host.ExecutionLog;

        private IOperationTracker OperationTracker { get; }

        private readonly FlaggedHierarchicalNameDictionary<Unit> m_outputMaterializationExclusionMap;

        private ILocalDiskFileSystemExistenceView m_localDiskFileSystemView;
        private ILocalDiskFileSystemExistenceView LocalDiskFileSystemView => m_localDiskFileSystemView ?? LocalDiskContentStore;

        /// <summary>
        /// The host for getting data about pips
        /// </summary>
        private readonly IFileContentManagerHost m_host;

        // TODO: Enable or remove this functionality (i.e. materializing source file in addition to pip outputs
        // on distributed build workers)
        private bool SourceFileMaterializationEnabled => Configuration.Distribution.EnableSourceFileMaterialization;

        private bool IsDistributedWorker => Configuration.Distribution.BuildRole == DistributedBuildRoles.Worker;

        /// <summary>
        /// Unit tests only. Used to suppress warnings which are not considered by unit tests
        /// when hashing source files
        /// </summary>
        internal bool TrackFilesUnderInvalidMountsForTests = false;

        private static readonly SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> s_emptySealContents =
            SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(new FileArtifact[0], OrdinalFileArtifactComparer.Instance);

        #endregion

        /// <summary>
        /// Creates a new file content manager with the specified host for providing
        /// auxillary data
        /// </summary>
        public FileContentManager(
            IFileContentManagerHost host,
            IOperationTracker operationTracker,
            SymlinkDefinitions symlinkDefinitions = null,
            ITempDirectoryCleaner tempDirectoryCleaner = null)
        {
            m_host = host;
            ArtifactContentCache = new ElidingArtifactContentCacheWrapper(host.ArtifactContentCache);
            OperationTracker = operationTracker;
            m_symlinkDefinitions = symlinkDefinitions;
            m_tempDirectoryCleaner = tempDirectoryCleaner;

            m_outputMaterializationExclusionMap = new FlaggedHierarchicalNameDictionary<Unit>(host.Context.PathTable, HierarchicalNameTable.NameFlags.Root);
            foreach (var outputMaterializationExclusionRoot in host.Configuration.Schedule.OutputMaterializationExclusionRoots)
            {
                m_outputMaterializationExclusionMap.TryAdd(outputMaterializationExclusionRoot.Value, Unit.Void);
            }
        }

        /// <summary>
        /// Sets local file system observer
        /// </summary>
        internal void SetLocalDiskFileSystemExistenceView(ILocalDiskFileSystemExistenceView localDiskFileSystem)
        {
            Contract.Requires(localDiskFileSystem != null);
            m_localDiskFileSystemView = localDiskFileSystem;
        }

        /// <summary>
        /// Registers the completion of a seal directory pip
        /// </summary>
        public void RegisterStaticDirectory(DirectoryArtifact artifact)
        {
            RegisterDirectoryContents(artifact);
        }

        /// <summary>
        /// Records the hash of an input of the pip. All static inputs must be reported, even those that were already up-to-date.
        /// </summary>
        public bool ReportWorkerPipInputContent(LoggingContext loggingContext, FileArtifact artifact, in FileMaterializationInfo info)
        {
            Contract.Assert(IsDistributedWorker);

            SetFileArtifactContentHashResult result = SetFileArtifactContentHash(artifact, info, PipOutputOrigin.NotMaterialized);

            // Notify the host with content that was reported
            m_host.ReportContent(artifact, info, PipOutputOrigin.NotMaterialized);

            if (result == SetFileArtifactContentHashResult.HasConflictingExistingEntry)
            {
                var existingInfo = m_fileArtifactContentHashes[artifact];

                // File was already hashed (i.e. seal directory input) prior to this pip which has the explicit input.
                // Report the content mismatch and fail.
                ReportWorkerContentMismatch(
                       loggingContext,
                       Context.PathTable,
                       artifact,
                       expectedHash: info.Hash,
                       actualHash: existingInfo.Hash);

                return false;
            }

            return true;
        }

        /// <summary>
        /// Records the hash of an input of the pip. All static inputs must be reported, even those that were already up-to-date.
        /// </summary>
        public void ReportInputContent(FileArtifact artifact, in FileMaterializationInfo info)
        {
            ReportContent(artifact, info, PipOutputOrigin.NotMaterialized);
        }

        /// <summary>
        /// Records the hash of an output of the pip. All static outputs must be reported, even those that were already up-to-date.
        /// </summary>
        public void ReportOutputContent(
            OperationContext operationContext,
            string pipDescription,
            FileArtifact artifact,
            in FileMaterializationInfo info,
            PipOutputOrigin origin,
            bool doubleWriteErrorsAreWarnings = false)
        {
            Contract.Requires(artifact.IsOutputFile);

            if (ReportContent(artifact, info, origin, doubleWriteErrorsAreWarnings))
            {
                if (origin != PipOutputOrigin.NotMaterialized && artifact.IsOutputFile)
                {
                    LogOutputOrigin(operationContext, pipDescription, artifact.Path.ToString(Context.PathTable), info, origin);
                }
            }
        }

        /// <summary>
        /// Ensures pip source inputs are hashed
        /// </summary>
        public async Task<Possible<Unit>> TryHashSourceDependenciesAsync(Pip pip, OperationContext operationContext)
        {
            using (PipArtifactsState state = GetPipArtifactsState())
            {
                // Get inputs
                PopulateDependencies(pip, state.PipArtifacts, includeLazyInputs: true, onlySourceFiles: true);

                var maybeInputsHashed = await TryHashFileArtifactsAsync(state, operationContext, pip.ProcessAllowsUndeclaredSourceReads);

                if (!maybeInputsHashed.Succeeded)
                {
                    return maybeInputsHashed.Failure;
                }

                return maybeInputsHashed;
            }
        }

        /// <summary>
        /// Ensures pip inputs are hashed
        /// </summary>
        public async Task<Possible<Unit>> TryHashDependenciesAsync(Pip pip, OperationContext operationContext)
        {
            // If force skip dependencies then try to hash the files. How does this work with lazy materialization? Or distributed build?
            // Probably assume dependencies are materialized if force skip dependencies is enabled
            using (PipArtifactsState state = GetPipArtifactsState())
            {
                // Get inputs
                PopulateDependencies(pip, state.PipArtifacts, includeLazyInputs: true);

                // In case of dirty build, we need to hash the seal contents without relying on consumers of seal contents.
                SealDirectory sealDirectory = pip as SealDirectory;
                if (sealDirectory != null)
                {
                    foreach (var input in sealDirectory.Contents)
                    {
                        state.PipArtifacts.Add(FileOrDirectoryArtifact.Create(input));
                    }
                }

                return await TryHashArtifacts(operationContext, state, pip.ProcessAllowsUndeclaredSourceReads);
            }
        }

        /// <summary>
        /// Ensures pip outputs are hashed
        /// </summary>
        public async Task<Possible<Unit>> TryHashOutputsAsync(Pip pip, OperationContext operationContext)
        {
            using (PipArtifactsState state = GetPipArtifactsState())
            {
                // Get outputs
                PopulateOutputs(pip, state.PipArtifacts);

                // If running TryHashOutputs, the pip's outputs are assumed to be materialized
                // Mark directory artifacts as materialized so we don't try to materialize them later
                // NOTE: We don't hash the contents of the directory because they will be hashed lazily
                // by consumers of the seal directory
                MarkDirectoryMaterializations(state);

                return await TryHashArtifacts(operationContext, state, pip.ProcessAllowsUndeclaredSourceReads);
            }
        }

        private async Task<Possible<Unit>> TryHashArtifacts(OperationContext operationContext, PipArtifactsState state, bool allowUndeclaredSourceReads)
        {
            var maybeReported = EnumerateAndReportDynamicOutputDirectories(state.PipArtifacts);

            if (!maybeReported.Succeeded)
            {
                return maybeReported.Failure;
            }

            // Register the seal file contents of the directory dependencies
            RegisterDirectoryContents(state.PipArtifacts);

            // Hash inputs if necessary
            var maybeInputsHashed = await TryHashFileArtifactsAsync(state, operationContext, allowUndeclaredSourceReads);

            if (!maybeInputsHashed.Succeeded)
            {
                return maybeInputsHashed.Failure;
            }

            return Unit.Void;
        }

        /// <summary>
        /// Ensures pip directory inputs are registered with file content manager
        /// </summary>
        public void RegisterDirectoryDependencies(Pip pip)
        {
            using (PipArtifactsState state = GetPipArtifactsState())
            {
                // Get inputs
                PopulateDependencies(pip, state.PipArtifacts);

                // Register the seal file contents of the directory dependencies
                RegisterDirectoryContents(state.PipArtifacts);
            }
        }

        /// <summary>
        /// Ensures pip inputs are materialized
        /// </summary>
        public async Task<bool> TryMaterializeDependenciesAsync(Pip pip, OperationContext operationContext)
        {
            using (PipArtifactsState state = GetPipArtifactsState())
            {
                // Get inputs
                PopulateDependencies(pip, state.PipArtifacts);

                // Register the seal file contents of the directory dependencies
                RegisterDirectoryContents(state.PipArtifacts);

                // Materialize inputs
                var result = await TryMaterializeArtifactsCore(new PipInfo(pip, Context), operationContext, state, materializatingOutputs: false, isDeclaredProducer: false);
                Contract.Assert(result != ArtifactMaterializationResult.None);

                switch (result)
                {
                    case ArtifactMaterializationResult.Succeeded:
                        return true;
                    case ArtifactMaterializationResult.PlaceFileFailed:
                        Logger.Log.PipMaterializeDependenciesFromCacheFailure(
                            operationContext,
                            pip.GetDescription(Context),
                            state.GetFailure().DescribeIncludingInnerFailures());
                        return false;
                    default:
                        // Catch-all error for non-cache dependency materialization failures
                        Logger.Log.PipMaterializeDependenciesFailureUnrelatedToCache(
                            operationContext,
                            pip.GetDescription(Context),
                            result.ToString(),
                            state.GetFailure().DescribeIncludingInnerFailures());
                        return false;
                }
            }
        }

        /// <summary>
        /// Ensures pip outputs are materialized
        /// </summary>
        public async Task<Possible<PipOutputOrigin>> TryMaterializeOutputsAsync(Pip pip, OperationContext operationContext)
        {
            using (PipArtifactsState state = GetPipArtifactsState())
            {
                bool hasExcludedOutput = false;

                // Get outputs
                PopulateOutputs(pip, state.PipArtifacts, exclude: output =>
                {
                    if (m_outputMaterializationExclusionMap.TryGetFirstMapping(output.Path.Value, out var mapping))
                    {
                        hasExcludedOutput = true;
                        return true;
                    }

                    return false;
                });

                // Register the seal file contents of the directory dependencies
                RegisterDirectoryContents(state.PipArtifacts);

                // Materialize outputs
                var result = await TryMaterializeArtifactsCore(new PipInfo(pip, Context), operationContext, state, materializatingOutputs: true, isDeclaredProducer: true);
                if (result != ArtifactMaterializationResult.Succeeded)
                {
                    return state.GetFailure();
                }

                if (hasExcludedOutput)
                {
                    return PipOutputOrigin.NotMaterialized;
                }

                // NotMaterialized means the files were materialized by some other operation
                // Normalize this to DeployedFromCache because the outputs are materialized
                return state.OverallOutputOrigin == PipOutputOrigin.NotMaterialized
                    ? PipOutputOrigin.DeployedFromCache
                    : state.OverallOutputOrigin;
            }
        }

        /// <summary>
        /// Attempts to load the output content for the pip to ensure it is available for materialization
        /// </summary>
        public async Task<bool> TryLoadAvailableOutputContentAsync(
            PipInfo pipInfo,
            OperationContext operationContext,
            IReadOnlyList<(FileArtifact fileArtifact, ContentHash contentHash)> filesAndContentHashes,
            Action onFailure = null,
            Action<int, string> onContentUnavailable = null,
            bool materialize = false)
        {
            Logger.Log.ScheduleTryBringContentToLocalCache(operationContext, pipInfo.Description);
            Interlocked.Increment(ref m_stats.TryBringContentToLocalCache);

            if (!materialize)
            {
                var result = await TryLoadAvailableContentAsync(
                    operationContext,
                    pipInfo,
                    materializingOutputs: true,
                    // Only failures matter since we are checking a cache entry and not actually materializing
                    onlyLogUnavailableContent: true,
                    filesAndContentHashes: filesAndContentHashes.SelectList((tuple, index) => (tuple.fileArtifact, tuple.contentHash, index)),
                    onFailure: failure => { onFailure?.Invoke(); },
                    onContentUnavailable: onContentUnavailable ?? ((index, hashLogStr) => { /* Do nothing. Callee already logs the failure */ }));

                return result;
            }
            else
            {
                using (var state = GetPipArtifactsState())
                {
                    state.VerifyMaterializationOnly = true;

                    foreach (var fileAndContentHash in filesAndContentHashes)
                    {
                        FileArtifact file = fileAndContentHash.fileArtifact;
                        ContentHash hash = fileAndContentHash.contentHash;
                        state.PipInfo = pipInfo;
                        state.AddMaterializationFile(
                            fileToMaterialize: file,
                            allowReadOnly: true,
                            materializationInfo: FileMaterializationInfo.CreateWithUnknownLength(hash),
                            materializationCompletion: TaskSourceSlim.Create<PipOutputOrigin>(),
                            symlinkTarget: AbsolutePath.Invalid);
                    }

                    return await PlaceFilesAsync(operationContext, pipInfo, state, materialize: true);
                }
            }
        }

        /// <summary>
        /// Reports the contents of an output directory
        /// </summary>
        public void ReportDynamicDirectoryContents(DirectoryArtifact directoryArtifact, IEnumerable<FileArtifact> contents, PipOutputOrigin outputOrigin)
        {
            m_sealContents.GetOrAdd(directoryArtifact, contents, (key, contents2) =>
                SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(contents2, OrdinalFileArtifactComparer.Instance));

            RegisterDirectoryContents(directoryArtifact);

            if (outputOrigin != PipOutputOrigin.NotMaterialized)
            {
                // Mark directory as materialized and ensure no deletions for the directory
                m_dynamicDirectoryDeletionTasks.TryAdd(directoryArtifact, BoolTask.True);
                MarkDirectoryMaterialization(directoryArtifact);
            }
        }

        /// <summary>
        /// Enumerates dynamic output directory.
        /// </summary>
        public Possible<Unit> EnumerateDynamicOutputDirectory(
            DirectoryArtifact directoryArtifact,
            Action<FileArtifact> handleFile,
            Action<AbsolutePath> handleDirectory)
        {
            Contract.Requires(directoryArtifact.IsValid);

            var pathTable = Context.PathTable;
            var directoryPath = directoryArtifact.Path;
            var queue = new Queue<AbsolutePath>();
            queue.Enqueue(directoryPath);

            while (queue.Count > 0)
            {
                var currentDirectoryPath = queue.Dequeue();
                var result = m_host.LocalDiskContentStore.TryEnumerateDirectoryAndTrackMembership(
                    currentDirectoryPath,
                    handleEntry: (entry, attributes) =>
                    {
                        var path = currentDirectoryPath.Combine(pathTable, entry);
                        // must treat directory symlinks as files: recursing on directory symlinks can lean to infinite loops
                        if (!FileUtilities.IsDirectoryNoFollow(attributes))
                        {
                            var fileArtifact = FileArtifact.CreateOutputFile(path);
                            handleFile?.Invoke(fileArtifact);
                        }
                        else
                        {
                            handleDirectory?.Invoke(path);
                            queue.Enqueue(path);
                        }
                    });
                if (!result.Succeeded)
                {
                    return result.Failure;
                }
            }

            return Unit.Void;
        }

        /// <summary>
        /// Lists the contents of a sealed directory (static or dynamic).
        /// </summary>
        public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealedDirectoryContents(DirectoryArtifact directoryArtifact)
        {
            SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> contents;

            var sealDirectoryKind = m_host.GetSealDirectoryKind(directoryArtifact);
            if (sealDirectoryKind.IsDynamicKind())
            {
                // If sealContents does not have the dynamic directory, then the dynamic directory has no content and it is produced by another worker.
                return m_sealContents.TryGetValue(directoryArtifact, out contents) ? contents : s_emptySealContents;
            }

            if (!m_sealContents.TryGetValue(directoryArtifact, out contents))
            {
                // Load and cache contents from host
                contents = m_host.ListSealDirectoryContents(directoryArtifact);
                m_sealContents.TryAdd(directoryArtifact, contents);
            }

            return contents;
        }

        /// <summary>
        /// Gets the pip inputs including all direct file dependencies and output file seal directory file dependencies.
        /// </summary>
        public void CollectPipInputsToMaterialize(
            PipTable pipTable,
            Pip pip,
            HashSet<FileArtifact> files,
            MultiValueDictionary<FileArtifact, DirectoryArtifact> dynamicInputs = null,
            Func<FileOrDirectoryArtifact, bool> filter = null,
            Func<PipId, bool> serviceFilter = null)
        {
            CollectPipFilesToMaterialize(
                isMaterializingInputs: true,
                pipTable: pipTable,
                pip: pip,
                files: files,
                dynamicFileMap: dynamicInputs,
                shouldInclude: filter,
                shouldIncludeServiceFiles: serviceFilter);
        }

        /// <summary>
        /// Gets the pip outputs
        /// </summary>
        public void CollectPipOutputsToMaterialize(
            PipTable pipTable,
            Pip pip,
            HashSet<FileArtifact> files,
            MultiValueDictionary<FileArtifact, DirectoryArtifact> dynamicOutputs = null,
            Func<FileOrDirectoryArtifact, bool> shouldInclude = null)
        {
            CollectPipFilesToMaterialize(
                isMaterializingInputs: false,
                pipTable: pipTable,
                pip: pip,
                files: files,
                dynamicFileMap: dynamicOutputs,
                shouldInclude: shouldInclude);
        }

        /// <summary>
        /// Gets the pip inputs or outputs
        /// </summary>
        public void CollectPipFilesToMaterialize(
            bool isMaterializingInputs,
            PipTable pipTable,
            Pip pip,
            HashSet<FileArtifact> files = null,
            MultiValueDictionary<FileArtifact, DirectoryArtifact> dynamicFileMap = null,
            Func<FileOrDirectoryArtifact, bool> shouldInclude = null,
            Func<PipId, bool> shouldIncludeServiceFiles = null)
        {
            // Always include if no filter specified
            shouldInclude = shouldInclude ?? (a => true);
            shouldIncludeServiceFiles = shouldIncludeServiceFiles ?? (a => true);

            using (PipArtifactsState state = GetPipArtifactsState())
            {
                if (isMaterializingInputs)
                {
                    // Get inputs
                    PopulateDependencies(pip, state.PipArtifacts, includeLazyInputs: true);
                }
                else
                {
                    PopulateOutputs(pip, state.PipArtifacts);
                }

                foreach (var artifact in state.PipArtifacts)
                {
                    if (!shouldInclude(artifact))
                    {
                        continue;
                    }

                    if (artifact.IsDirectory)
                    {
                        DirectoryArtifact directory = artifact.DirectoryArtifact;
                        SealDirectoryKind sealDirectoryKind = m_host.GetSealDirectoryKind(directory);

                        foreach (var file in ListSealedDirectoryContents(directory))
                        {
                            if (sealDirectoryKind.IsDynamicKind())
                            {
                                dynamicFileMap?.Add(file, directory);
                            }

                            if (file.IsOutputFile || SourceFileMaterializationEnabled)
                            {
                                if (!shouldInclude(file))
                                {
                                    continue;
                                }

                                files?.Add(file);
                            }
                        }
                    }
                    else
                    {
                        files?.Add(artifact.FileArtifact);
                    }
                }
            }

            if (isMaterializingInputs)
            {
                // For the IPC pips, we need to collect the inputs of their service dependencies as well.
                if (pip.PipType == PipType.Ipc)
                {
                    var ipc = (IpcPip)pip;
                    foreach (var servicePipId in ipc.ServicePipDependencies)
                    {
                        if (!shouldIncludeServiceFiles(servicePipId))
                        {
                            continue;
                        }

                        var servicePip = pipTable.HydratePip(servicePipId, PipQueryContext.CollectPipInputsToMaterializeForIPC);
                        CollectPipInputsToMaterialize(pipTable, servicePip, files, dynamicFileMap, filter: shouldInclude);
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to the symlink target if registered
        /// </summary>
        public AbsolutePath TryGetRegisteredSymlinkFinalTarget(AbsolutePath symlink)
        {
            if (m_symlinkDefinitions == null)
            {
                return AbsolutePath.Invalid;
            }

            AbsolutePath symlinkTarget = symlink;
            for (int i = 0; i < MAX_SYMLINK_TRAVERSALS; i++)
            {
                var next = m_symlinkDefinitions.TryGetSymlinkTarget(symlinkTarget);
                if (next.IsValid)
                {
                    symlinkTarget = next;
                }
                else
                {
                    return symlinkTarget == symlink ? AbsolutePath.Invalid : symlinkTarget;
                }
            }

            // Symlink chain is too long
            List<AbsolutePath> symlinkChain = new List<AbsolutePath>();
            symlinkTarget = symlink;
            for (int i = 0; i < MAX_SYMLINK_TRAVERSALS; i++)
            {
                symlinkChain.Add(symlinkTarget);
                symlinkTarget = m_symlinkDefinitions.TryGetSymlinkTarget(symlinkTarget);
            }

            throw new BuildXLException(I(
                $"Registered symlink chain exceeds max length of {MAX_SYMLINK_TRAVERSALS}: {string.Join("->" + Environment.NewLine, symlinkChain)}"));
        }

        /// <summary>
        /// Reports an unexpected access which the file content manager can check to verify that access is safe with respect to lazy
        /// symlink creation
        /// </summary>
        internal void ReportUnexpectedSymlinkAccess(LoggingContext loggingContext, string pipDescription, AbsolutePath path, ObservedInputType observedInputType, CompactSet<ReportedFileAccess> reportedAccesses)
        {
            if (TryGetSymlinkPathKind(path, out var symlinkPathKind))
            {
                using (var toolPathSetWrapper = Pools.StringSetPool.GetInstance())
                using (var toolFileNameSetWrapper = Pools.StringSetPool.GetInstance())
                {
                    var toolPathSet = toolPathSetWrapper.Instance;
                    var toolFileNameSet = toolFileNameSetWrapper.Instance;

                    foreach (var reportedAccess in reportedAccesses)
                    {
                        if (!string.IsNullOrEmpty(reportedAccess.Process.Path))
                        {
                            if (toolPathSet.Add(reportedAccess.Process.Path))
                            {
                                AbsolutePath toolPath;
                                if (AbsolutePath.TryCreate(Context.PathTable, reportedAccess.Process.Path, out toolPath))
                                {
                                    toolFileNameSet.Add(toolPath.GetName(Context.PathTable).ToString(Context.StringTable));
                                }
                            }
                        }
                    }

                    Logger.Log.UnexpectedAccessOnSymlinkPath(
                        pipDescription: pipDescription,
                        context: loggingContext,
                        path: path.ToString(Context.PathTable),
                        pathKind: symlinkPathKind,
                        inputType: observedInputType.ToString(),
                        tools: string.Join(", ", toolFileNameSet));
                }
            }
        }

        internal bool TryGetSymlinkPathKind(AbsolutePath path, out string kind)
        {
            kind = null;
            if (m_symlinkDefinitions == null)
            {
                return false;
            }

            if (m_symlinkDefinitions.IsSymlink(path))
            {
                kind = "file";
            }
            else if (m_symlinkDefinitions.HasNestedSymlinks(path))
            {
                kind = "directory";
            }

            return kind != null;
        }

        /// <summary>
        /// Gets the updated semantic path information for the given path with data from the file content manager
        /// </summary>
        internal SemanticPathInfo GetUpdatedSemanticPathInfo(in SemanticPathInfo mountInfo)
        {
            if (mountInfo.IsValid && LazySymlinkCreation && m_symlinkDefinitions.HasNestedSymlinks(mountInfo.Root))
            {
                // Rewrite the semantic path info to indicate that the mount has potential build outputs
                return new SemanticPathInfo(mountInfo.RootName, mountInfo.Root, mountInfo.Flags | SemanticPathFlags.HasPotentialBuildOutputs);
            }

            return mountInfo;
        }

        /// <summary>
        /// Gets whether the given directory has potential outputs of the build
        /// </summary>
        public bool HasPotentialBuildOutputs(AbsolutePath directoryPath, in SemanticPathInfo mountInfo, bool isReadOnlyDirectory)
        {
            // If (1) the directory is writeable, or (2) the directory contains symlinks, that may have not been created, then use the graph enumeration.
            return (mountInfo.IsWritable && !isReadOnlyDirectory) ||
                (LazySymlinkCreation && m_symlinkDefinitions.DirectoryContainsSymlink(directoryPath));
        }

        /// <summary>
        /// For a given path (which must be one of the pip's input artifacts; not an output), returns a content hash if:
        /// - that path has been 'sealed' (i.e., is under a sealed directory) . Since a sealed directory never changes, the path is un-versioned
        /// (unlike a <see cref="FileArtifact" />)
        /// - that path is not under any sealed container (source or full/partial seal directory), but undeclared source reads are allowed. In that
        /// case the path is also unversioned because immutability is also guaranteed by dynamic enforcements.
        /// This method always succeeds or fails synchronously.
        /// </summary>
        public async Task<FileContentInfo?> TryQuerySealedOrUndeclaredInputContentAsync(AbsolutePath path, string consumerDescription, bool allowUndeclaredSourceReads)
        {
            FileOrDirectoryArtifact declaredArtifact;

            var isDynamicallyObservedSource = false;

            if (!m_sealedFiles.TryGetValue(path, out FileArtifact sealedFile))
            {
                var sourceSealDirectory = TryGetSealSourceAncestor(path);
                if (!sourceSealDirectory.IsValid)
                {
                    // If there is no sealed artifact that contains the read, then we check if undeclared source reads are allowed
                    if (!allowUndeclaredSourceReads)
                    {
                        // No source seal directory found
                        return null;
                    }
                    else
                    {
                        // If undeclared source reads is enabled but the path does not exist, then we can just shortcut
                        // the query here. This matches the declared case when no static artifact is found that contains
                        // the path
                        var maybeResult = FileUtilities.TryProbePathExistence(path.ToString(Context.PathTable), followSymlink: false);
                        if (!maybeResult.Succeeded || maybeResult.Result == PathExistence.Nonexistent)
                        {
                            return null;
                        }
                    }

                    // We set the declared artifact as the path itself, there is no 'declared container' for this case.
                    declaredArtifact = FileArtifact.CreateSourceFile(path);
                }
                else
                {
                    // The source seal directory is the artifact which is actually declared
                    // file artifact is created on the fly and never declared
                    declaredArtifact = sourceSealDirectory;
                }

                // The path is not under a full/partial seal directory. So it is either under a source seal, or it is an allowed undeclared read. In both
                // cases it is a dynamically observed source
                isDynamicallyObservedSource = true;

                // This path is in a sealed source directory or undeclared source reads are allowed
                // so create a source file and query the content of it
                sealedFile = FileArtifact.CreateSourceFile(path);
            }
            else
            {
                declaredArtifact = sealedFile;
            }

            FileMaterializationInfo? materializationInfo;
            using (var operationContext = OperationTracker.StartOperation(OperationKind.PassThrough, m_host.LoggingContext))
            using (operationContext.StartOperation(PipExecutorCounter.FileContentManagerTryQuerySealedInputContentDuration, sealedFile))
            {
                materializationInfo = await TryQueryContentAsync(sealedFile, operationContext, declaredArtifact, allowUndeclaredSourceReads, consumerDescription);
            }

            if (materializationInfo == null)
            {
                if (isDynamicallyObservedSource && m_contentQueriedDirectoryPaths.Contains(path))
                {
                    // Querying a directory under a source seal directory is allowed, return null which
                    // allows the ObservedInputProcessor to proceed
                    // This is different from the case of other seal directory types because the paths registered
                    // in those directories are required to be files or missing
                    // Querying a directory when undeclared source reads are allowed is also allowed
                    return null;
                }

                // This causes the ObservedInputProcessor to abort the strong fingerprint computation
                // by indicating a failure in hashing a path
                // TODO: In theory, ObservedInputProcessor should not treat
                return FileContentInfo.CreateWithUnknownLength(WellKnownContentHashes.UntrackedFile);
            }

            return materializationInfo.Value.FileContentInfo;
        }

        /// <summary>
        /// For a given file artifact (which must be one of the pip's input artifacts; not an output), returns a content hash.
        /// This method always succeeds synchronously. The specified artifact must be a statically known (not dynamic) dependency.
        /// </summary>
        public FileMaterializationInfo GetInputContent(FileArtifact fileArtifact)
        {
            FileMaterializationInfo materializationInfo;
            bool found = TryGetInputContent(fileArtifact, out materializationInfo);

            if (!found)
            {
                Contract.Assume(
                    false,
                    "Attempted to query the content hash for an artifact which has not passed through SetFileArtifactContentHash: "
                    + fileArtifact.Path.ToString(Context.PathTable));
            }

            return materializationInfo;
        }

        /// <summary>
        /// Attempts to get the materialization info for the input artifact
        /// </summary>
        public bool TryGetInputContent(FileArtifact file, out FileMaterializationInfo info)
        {
            return m_fileArtifactContentHashes.TryGetValue(file, out info);
        }

        /// <summary>
        /// Whether there is a directory artifact representing an output directory (shared or exclusive opaque) that contains the given path
        /// </summary>
        public bool TryGetContainingOutputDirectory(AbsolutePath path, out DirectoryArtifact containingOutputDirectory)
        {
            containingOutputDirectory = DirectoryArtifact.Invalid;
            return (m_sealedFiles.TryGetValue(path, out FileArtifact sealedFile) && m_dynamicOutputFileDirectories.TryGetValue(sealedFile, out containingOutputDirectory));
        }

        /// <summary>
        /// Attempts to materialize the given file
        /// </summary>
        public async Task<bool> TryMaterializeFile(FileArtifact outputFile)
        {
            var producer = GetDeclaredProducer(outputFile);
            using (var operationContext = OperationTracker.StartOperation(PipExecutorCounter.FileContentManagerTryMaterializeFileDuration, m_host.LoggingContext))
            {
                return ArtifactMaterializationResult.Succeeded
                    == await TryMaterializeFilesAsync(producer, operationContext, new[] { outputFile }, materializatingOutputs: true, isDeclaredProducer: true);
            }
        }

        /// <summary>
        /// Attempts to materialize the specified files
        /// </summary>
        public async Task<ArtifactMaterializationResult> TryMaterializeFilesAsync(
            Pip requestingPip,
            OperationContext operationContext,
            IEnumerable<FileArtifact> filesToMaterialize,
            bool materializatingOutputs,
            bool isDeclaredProducer)
        {
            var pipInfo = new PipInfo(requestingPip, Context);

            using (PipArtifactsState state = GetPipArtifactsState())
            {
                foreach (var item in filesToMaterialize)
                {
                    state.PipArtifacts.Add(item);
                }

                return await TryMaterializeArtifactsCore(
                                    pipInfo,
                                    operationContext,
                                    state,
                                    materializatingOutputs: materializatingOutputs,
                                    isDeclaredProducer: isDeclaredProducer);
            }
        }

        /// <summary>
        /// Gets the materialization origin of a file.
        /// </summary>
        public PipOutputOrigin GetPipOutputOrigin(FileArtifact file)
        {
            Contract.Requires(file.IsValid);

            return m_materializationTasks.TryGetValue(file, out Task<PipOutputOrigin> outputOrigin) ? outputOrigin.Result : PipOutputOrigin.NotMaterialized;
        }

        private PipArtifactsState GetPipArtifactsState()
        {
            PipArtifactsState state;
            if (!m_statePool.TryDequeue(out state))
            {
                return new PipArtifactsState(this);
            }

            return state;
        }

        private static void PopulateDependencies(Pip pip, HashSet<FileOrDirectoryArtifact> dependencies, bool includeLazyInputs = false, bool onlySourceFiles = false)
        {
            if (pip.PipType == PipType.SealDirectory)
            {
                // Seal directory contents are handled by consumer of directory
                return;
            }

            Func<FileOrDirectoryArtifact, bool> action = (input) =>
            {
                if (!onlySourceFiles || (input.IsFile && input.FileArtifact.IsSourceFile))
                {
                    dependencies.Add(input);
                }

                return true;
            };

            Func<FileOrDirectoryArtifact, bool> lazyInputAction = action;
            if (!includeLazyInputs)
            {
                lazyInputAction = lazyInput =>
                {
                    // Remove lazy inputs
                    dependencies.Remove(lazyInput);
                    return true;
                };
            }

            PipArtifacts.ForEachInput(pip, action, includeLazyInputs: true, overrideLazyInputAction: lazyInputAction);
        }

        private static void PopulateOutputs(Pip pip, HashSet<FileOrDirectoryArtifact> outputs, Func<FileOrDirectoryArtifact, bool> exclude = null)
        {
            PipArtifacts.ForEachOutput(pip, output =>
            {
                if (exclude?.Invoke(output) == true)
                {
                    return true;
                }

                outputs.Add(output);
                return true;
            }, includeUncacheable: false);
        }

        // TODO: Consider calling this from TryHashDependencies. That would allow us to remove logic which requires
        // that direct seal directory dependencies are scheduled
        private Possible<Unit> EnumerateAndReportDynamicOutputDirectories(HashSet<FileOrDirectoryArtifact> artifacts)
        {
            foreach (var artifact in artifacts)
            {
                if (artifact.IsDirectory)
                {
                    var directory = artifact.DirectoryArtifact;
                    SealDirectoryKind sealDirectoryKind = m_host.GetSealDirectoryKind(directory);

                    if (sealDirectoryKind == SealDirectoryKind.Opaque)
                    {
                        if (m_sealContents.ContainsKey(directory))
                        {
                            // Only enumerate and report if the directory has not already been reported
                            continue;
                        }

                        if (Context.CancellationToken.IsCancellationRequested)
                        {
                            return Context.CancellationToken.CreateFailure();
                        }

                        var result = EnumerateAndReportDynamicOutputDirectory(directory);
                        if (!result.Succeeded)
                        {
                            return result;
                        }
                    }
                }
            }

            return Unit.Void;
        }

        private Possible<Unit> EnumerateAndReportDynamicOutputDirectory(DirectoryArtifact directory)
        {
            using (var poolFileList = Pools.GetFileArtifactList())
            {
                var fileList = poolFileList.Instance;
                fileList.Clear();

                var result = EnumerateDynamicOutputDirectory(directory, handleFile: file => fileList.Add(file), handleDirectory: null);

                if (!result.Succeeded)
                {
                    return result.Failure;
                }

                ReportDynamicDirectoryContents(directory, fileList, PipOutputOrigin.UpToDate);
            }

            return Unit.Void;
        }

        private void RegisterDirectoryContents(HashSet<FileOrDirectoryArtifact> artifacts)
        {
            foreach (var artifact in artifacts)
            {
                if (artifact.IsDirectory)
                {
                    var directory = artifact.DirectoryArtifact;
                    RegisterDirectoryContents(directory);
                }
            }
        }

        private void RegisterDirectoryContents(DirectoryArtifact directory)
        {
            if (!m_registeredSealDirectories.Contains(directory))
            {
                SealDirectoryKind sealDirectoryKind = m_host.GetSealDirectoryKind(directory);

                foreach (var file in ListSealedDirectoryContents(directory))
                {
                    var addedFile = m_sealedFiles.GetOrAdd(file.Path, file).Item.Value;
                    Contract.Assert(addedFile == file, "Attempted to seal path twice with different rewrite counts");

                    if (sealDirectoryKind.IsDynamicKind())
                    {
                        // keep only the original {file -> directory} mapping
                        m_dynamicOutputFileDirectories.TryAdd(file, directory);
                    }
                }

                if (m_host.ShouldScrubFullSealDirectory(directory))
                {
                    using (var pooledList = Pools.GetStringList())
                    {
                        var unsealedFiles = pooledList.Instance;
                        FileUtilities.DeleteDirectoryContents(
                            path: directory.Path.ToString(Context.PathTable),
                            deleteRootDirectory: false,
                            shouldDelete: filePath =>
                            {
                                if (!m_sealedFiles.ContainsKey(AbsolutePath.Create(Context.PathTable, filePath)))
                                {
                                    unsealedFiles.Add(filePath);
                                    return true;
                                }
                                return false;
                            },
                            tempDirectoryCleaner: m_tempDirectoryCleaner);

                        Logger.Log.DeleteFullySealDirectoryUnsealedContents(
                            context: m_host.LoggingContext,
                            directoryPath: directory.Path.ToString(Context.PathTable),
                            pipDescription: GetAssociatedPipDescription(directory),
                            string.Join(Environment.NewLine, unsealedFiles.Select(f => "\t" + f)));
                    }

                }
                else if (sealDirectoryKind.IsSourceSeal())
                {
                    m_sealedSourceDirectories.TryAdd(directory.Path, directory);
                }

                m_registeredSealDirectories.Add(directory);
            }
        }

        /// <summary>
        /// Attempts to get the producer pip id for the producer of the file. The file may be
        /// a file produced inside a dynamic directory so its 'declared' producer will be the
        /// producer of the dynamic directory
        /// </summary>
        private PipId TryGetDeclaredProducerId(FileArtifact file)
        {
            return m_host.TryGetProducerId(GetDeclaredArtifact(file));
        }

        /// <summary>
        /// Attempts to get the producer pip for the producer of the file. The file may be
        /// a file produced inside a dynamic directory so its 'declared' producer will be the
        /// producer of the dynamic directory
        /// </summary>
        private Pip GetDeclaredProducer(FileArtifact file)
        {
            return m_host.GetProducer(GetDeclaredArtifact(file));
        }

        /// <summary>
        /// Gets the statically declared artifact corresponding to the file. In most cases, this is the file
        /// except for dynamic outputs or seal source files which are dynamically discovered
        /// </summary>
        private FileOrDirectoryArtifact GetDeclaredArtifact(FileArtifact file)
        {
            DirectoryArtifact declaredDirectory;
            if (m_sealedFiles.ContainsKey(file.Path))
            {
                if (file.IsOutputFile)
                {
                    if (m_dynamicOutputFileDirectories.TryGetValue(file, out declaredDirectory))
                    {
                        return declaredDirectory;
                    }
                }
            }
            else if (file.IsSourceFile)
            {
                declaredDirectory = TryGetSealSourceAncestor(file.Path);
                if (declaredDirectory.IsValid)
                {
                    return declaredDirectory;
                }
            }

            return file;
        }

        private async Task<Possible<Unit>> TryHashFileArtifactsAsync(PipArtifactsState state, OperationContext operationContext, bool allowUndeclaredSourceReads)
        {
            foreach (var artifact in state.PipArtifacts)
            {
                if (!artifact.IsDirectory)
                {
                    // Directory artifact contents are not hashed since they will be hashed dynamically
                    // if the pip accesses them, so the file is the declared artifact
                    FileMaterializationInfo fileContentInfo;
                    FileArtifact file = artifact.FileArtifact;
                    if (!m_fileArtifactContentHashes.TryGetValue(file, out fileContentInfo))
                    {
                        // Directory artifact contents are not hashed since they will be hashed dynamically
                        // if the pip accesses them, so the file is the declared artifact
                        state.HashTasks.Add(TryQueryContentAsync(
                            file,
                            operationContext,
                            declaredArtifact: file,
                            allowUndeclaredSourceReads));
                    }
                }
            }

            FileMaterializationInfo?[] artifactContentInfos = await Task.WhenAll(state.HashTasks);

            foreach (var artifactContentInfo in artifactContentInfos)
            {
                if (!artifactContentInfo.HasValue)
                {
                    return new Failure<string>("Could not retrieve input content for pip");
                }
            }

            return Unit.Void;
        }

        private async Task<FileMaterializationInfo?> TryQueryContentAsync(
            FileArtifact fileArtifact,
            OperationContext operationContext,
            FileOrDirectoryArtifact declaredArtifact,
            bool allowUndeclaredSourceReads,
            string consumerDescription = null,
            bool verifyingHash = false)
        {
            if (!verifyingHash)
            {
                // Just use the stored hash if available and we are not verifying the hash which must bypass the
                // use of the stored hash
                FileMaterializationInfo recordedfileContentInfo;
                if (m_fileArtifactContentHashes.TryGetValue(fileArtifact, out recordedfileContentInfo))
                {
                    return recordedfileContentInfo;
                }
            }

            Task<FileMaterializationInfo?> alreadyHashingTask;
            TaskSourceSlim<FileMaterializationInfo?> hashCompletion;
            if (!TryReserveCompletion(m_fileArtifactHashTasks, fileArtifact, out alreadyHashingTask, out hashCompletion))
            {
                var hash = await alreadyHashingTask;
                return hash;
            }

            FileMaterializationInfo? fileContentInfo;
            AbsolutePath symlinkTarget = TryGetSymlinkTarget(fileArtifact);
            if (symlinkTarget.IsValid)
            {
                fileContentInfo = GetOrComputeSymlinkInputContent(fileArtifact);
            }
            else
            {
                // for output files, call GetAndRecordFileContentHashAsyncCore directly which
                // doesn't include checks for mount points used for source file hashing
                TrackedFileContentInfo? trackedFileContentInfo = fileArtifact.IsSourceFile
                    ? await GetAndRecordSourceFileContentHashAsync(operationContext, fileArtifact, declaredArtifact, allowUndeclaredSourceReads, consumerDescription)
                    : await GetAndRecordFileContentHashAsyncCore(operationContext, fileArtifact, declaredArtifact, consumerDescription);

                fileContentInfo = trackedFileContentInfo?.FileMaterializationInfo;
            }

            if (fileContentInfo.HasValue)
            {
                if (!verifyingHash)
                {
                    // Don't store the hash when performing verification
                    ReportContent(fileArtifact, fileContentInfo.Value, symlinkTarget.IsValid ? PipOutputOrigin.NotMaterialized : PipOutputOrigin.UpToDate);
                }

                // Remove task now that content info is stored in m_fileArtifactContentHashes
                m_fileArtifactHashTasks.TryRemove(fileArtifact, out alreadyHashingTask);
            }

            hashCompletion.SetResult(fileContentInfo);
            return fileContentInfo;
        }

        /// <summary>
        /// Creates all symlink files in the symlink definitions
        /// </summary>
        public static bool CreateSymlinkEagerly(LoggingContext loggingContext, IConfiguration configuration, PathTable pathTable, SymlinkDefinitions symlinkDefinitions, CancellationToken cancellationToken)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(symlinkDefinitions != null);
            Contract.Requires(!configuration.Schedule.UnsafeLazySymlinkCreation || configuration.Engine.PopulateSymlinkDirectories.Count != 0);

            Logger.Log.SymlinkFileTraceMessage(loggingContext, I($"Eagerly creating symlinks found in symlink file."));

            int createdSymlinkCount = 0;
            int reuseExistingSymlinkCount = 0;
            int failedSymlinkCount = 0;

            var startTime = TimestampUtilities.Timestamp;

            var symlinkDirectories = symlinkDefinitions.DirectorySymlinkContents.Keys.ToList();
            var populateSymlinkDirectories = new HashSet<HierarchicalNameId>(configuration.Engine.PopulateSymlinkDirectories.Select(p => p.Value));

            Parallel.ForEach(
                symlinkDirectories,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = configuration.Schedule.MaxProcesses,
                },
                symlinkDirectory =>
                {
                    bool populateSymlinks = !configuration.Schedule.UnsafeLazySymlinkCreation;
                    if (!populateSymlinks)
                    {
                        // If populating symlinks lazily, check if the directory is under the explicitly specified symlink directories
                        // to populate
                        foreach (var parent in pathTable.EnumerateHierarchyBottomUp(symlinkDirectory.Value))
                        {
                            if (populateSymlinkDirectories.Contains(parent))
                            {
                                populateSymlinks = true;
                                break;
                            }
                        }
                    }

                    if (!populateSymlinks)
                    {
                        return;
                    }

                    var directorySymlinks = symlinkDefinitions.DirectorySymlinkContents[symlinkDirectory];
                    foreach (var symlinkPath in directorySymlinks)
                    {
                        var symlink = symlinkPath.ToString(pathTable);
                        var symlinkTarget = symlinkDefinitions[symlinkPath].ToString(pathTable);

                        if (cancellationToken.IsCancellationRequested)
                        {
                            Interlocked.Increment(ref failedSymlinkCount);
                            break;
                        }

                        bool created;

                        var maybeSymlink = FileUtilities.TryCreateSymlinkIfNotExistsOrTargetsDoNotMatch(symlink, symlinkTarget, true, out created);
                        if (maybeSymlink.Succeeded)
                        {
                            if (created)
                            {
                                Interlocked.Increment(ref createdSymlinkCount);
                            }
                            else
                            {
                                Interlocked.Increment(ref reuseExistingSymlinkCount);
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref failedSymlinkCount);
                            Logger.Log.FailedToCreateSymlinkFromSymlinkMap(loggingContext, symlink, symlinkTarget, maybeSymlink.Failure.DescribeIncludingInnerFailures());
                        }
                    }
                });

            Logger.Log.CreateSymlinkFromSymlinkMap(
                loggingContext,
                createdSymlinkCount,
                reuseExistingSymlinkCount,
                failedSymlinkCount,
                (int)(TimestampUtilities.Timestamp - startTime).TotalMilliseconds);

            return failedSymlinkCount == 0;
        }

        private async Task<ArtifactMaterializationResult> TryMaterializeArtifactsCore(
            PipInfo pipInfo,
            OperationContext operationContext,
            PipArtifactsState state,
            bool materializatingOutputs,
            bool isDeclaredProducer)
        {
            // If materializing outputs, all files come from the same pip and therefore have the same
            // policy for whether they are readonly
            bool? allowReadOnly = materializatingOutputs ? !PipArtifacts.IsOutputMustRemainWritablePip(pipInfo.UnderlyingPip) : (bool?)null;

            state.PipInfo = pipInfo;
            state.MaterializingOutputs = materializatingOutputs;
            state.IsDeclaredProducer = isDeclaredProducer;

            // Get the files which need to be materialized
            // We reserve completion of directory deletions and file materialization so only a single deleter/materializer of a
            // directory/file. If the operation is reserved, code will perform the operation. Otherwise, it will await a task
            // signaling the completion of the operation
            PopulateArtifactsToMaterialize(state, allowReadOnly);

            using (operationContext.StartOperation(PipExecutorCounter.FileContentManagerDeleteDirectoriesDuration))
            {
                // Delete dynamic directory contents prior to placing files
                // NOTE: This should happen before any per-file materialization/deletion operations because
                // it skips deleting declared files in the materialized directory under the assumption that
                // a later step will materialize/delete the file as necessary
                if (!await PrepareDirectoriesAsync(state, operationContext))
                {
                    return ArtifactMaterializationResult.PrepareDirectoriesFailed;
                }
            }

            if (IsDistributedWorker)
            {
                // Check that source files match (this may fail the materialization or leave the
                // source file to be materialized later if the materializeSourceFiles option is set (distributed worker))
                if (!await VerifySourceFileInputsAsync(operationContext, pipInfo, state))
                {
                    return ArtifactMaterializationResult.VerifySourceFilesFailed;
                }
            }

            // Delete the absent files if any
            if (!await DeleteFilesRequiredAbsentAsync(state, operationContext))
            {
                return ArtifactMaterializationResult.DeleteFilesRequiredAbsentFailed;
            }

            // Place Files:
            if (!await PlaceFilesAsync(operationContext, pipInfo, state))
            {
                return ArtifactMaterializationResult.PlaceFileFailed;
            }

            // Mark directories as materialized so that the full set of files in the directory will
            // not need to be checked for completion on subsequent materialization calls
            MarkDirectoryMaterializations(state);

            return ArtifactMaterializationResult.Succeeded;
        }

        private void PopulateArtifactsToMaterialize(PipArtifactsState state, bool? allowReadOnlyOverride)
        {
            foreach (var artifact in state.PipArtifacts)
            {
                if (artifact.IsDirectory)
                {
                    DirectoryArtifact directory = artifact.DirectoryArtifact;
                    if (m_materializedDirectories.Contains(directory))
                    {
                        // Directory is already materialized, no need to materialize its contents
                        continue;
                    }

                    SealDirectoryKind sealDirectoryKind = m_host.GetSealDirectoryKind(directory);

                    bool? directoryAllowReadOnlyOverride = allowReadOnlyOverride;

                    if (sealDirectoryKind == SealDirectoryKind.Opaque)
                    {
                        // Dynamic directories must be deleted before materializing files
                        // We don't want this to happen for shared dynamic ones
                        AddDirectoryDeletion(state, artifact.DirectoryArtifact);

                        // For dynamic directories we need to specify the value of
                        // allow read only since the host will not know about the
                        // dynamically produced file
                        if (directoryAllowReadOnlyOverride == null && m_host.TryGetProducerId(directory).IsValid)
                        {
                            var producer = m_host.GetProducer(directory);
                            directoryAllowReadOnlyOverride = !PipArtifacts.IsOutputMustRemainWritablePip(producer);
                        }
                    }
                    else if (sealDirectoryKind == SealDirectoryKind.SourceTopDirectoryOnly)
                    {
                        IReadOnlyList<AbsolutePath> paths;
                        if (LazySymlinkCreation && m_symlinkDefinitions.TryGetSymlinksInDirectory(directory.Path, out paths))
                        {
                            foreach (var path in paths)
                            {
                                AddFileMaterialization(
                                    state,
                                    FileArtifact.CreateSourceFile(path),
                                    directoryAllowReadOnlyOverride,
                                    m_symlinkDefinitions[path]);
                            }
                        }

                        continue;
                    }
                    else if (sealDirectoryKind == SealDirectoryKind.SourceAllDirectories)
                    {
                        if (LazySymlinkCreation && m_symlinkDefinitions.HasNestedSymlinks(directory))
                        {
                            foreach (var node in Context.PathTable.EnumerateHierarchyTopDown(directory.Path.Value))
                            {
                                var path = new AbsolutePath(node);
                                AddFileMaterialization(
                                    state,
                                    FileArtifact.CreateSourceFile(path),
                                    directoryAllowReadOnlyOverride,
                                    m_symlinkDefinitions.TryGetSymlinkTarget(path));
                            }
                        }

                        continue;
                    }

                    // Full, partial, and dynamic output must have contents materialized
                    foreach (var file in ListSealedDirectoryContents(directory))
                    {
                        // This is not needed for shared dynamic since we are not deleting them to begin with
                        if (sealDirectoryKind == SealDirectoryKind.Opaque)
                        {
                            // Track reported files inside dynamic directories so they are not deleted
                            // during the directory deletion step (they will be replaced by the materialization
                            // step or may have already been replaced if the file was explicitly materialized)
                            state.MaterializedDirectoryContents.Add(file);
                        }

                        AddFileMaterialization(state, file, directoryAllowReadOnlyOverride, TryGetSymlinkTarget(file));
                    }
                }
                else
                {
                    AddFileMaterialization(state, artifact.FileArtifact, allowReadOnlyOverride, TryGetSymlinkTarget(artifact.FileArtifact));
                }
            }
        }

        private AbsolutePath TryGetSymlinkTarget(FileArtifact file)
        {
            if (file.IsOutputFile || !LazySymlinkCreation)
            {
                // Only source files can be declared as symlinks
                return AbsolutePath.Invalid;
            }

            return m_symlinkDefinitions.TryGetSymlinkTarget(file);
        }

        private void MarkDirectoryMaterializations(PipArtifactsState state)
        {
            foreach (var artifact in state.PipArtifacts)
            {
                if (artifact.IsDirectory)
                {
                    MarkDirectoryMaterialization(artifact.DirectoryArtifact);
                }
            }
        }

        /// <summary>
        /// Checks if a <see cref="DirectoryArtifact"/> is materialized.
        /// </summary>
        public bool IsMaterialized(DirectoryArtifact directoryArtifact)
        {
            return m_materializedDirectories.Contains(directoryArtifact);
        }

        private void MarkDirectoryMaterialization(DirectoryArtifact directoryArtifact)
        {
            m_materializedDirectories.Add(directoryArtifact);
            m_host.ReportMaterializedArtifact(directoryArtifact);
        }

        private void AddDirectoryDeletion(PipArtifactsState state, DirectoryArtifact directoryArtifact)
        {
            var sealDirectoryKind = m_host.GetSealDirectoryKind(directoryArtifact);
            if (sealDirectoryKind != SealDirectoryKind.Opaque)
            {
                // Only dynamic output directories should be deleted
                return;
            }

            TaskSourceSlim<bool> deletionCompletion;
            Task<bool> alreadyDeletingTask;
            if (!TryReserveCompletion(m_dynamicDirectoryDeletionTasks, directoryArtifact, out alreadyDeletingTask, out deletionCompletion))
            {
                if (alreadyDeletingTask.Status != TaskStatus.RanToCompletion || !alreadyDeletingTask.Result)
                {
                    state.PendingDirectoryDeletions.Add(alreadyDeletingTask);
                }
            }
            else
            {
                state.DirectoryDeletionCompletions.Add((directoryArtifact, deletionCompletion));
            }
        }

        /// <summary>
        /// Adds a file to the list of files to be materialized.
        /// </summary>
        /// <param name="state">the state object containing the list of file materializations.</param>
        /// <param name="file">the file to materialize.</param>
        /// <param name="allowReadOnlyOverride">specifies whether the file is allowed to be read-only. If not specified, the host is queried.</param>
        /// <param name="symlinkTarget">the target of the symlink (if file is registered symlink).</param>
        /// <param name="dependentFileIndex">the index of a file (in the list of materialized files) which requires the materialization of this file as
        /// a prerequisite (if any). This is used when restoring content into cache for a host materialized file (i.e. write file output).</param>
        private void AddFileMaterialization(
            PipArtifactsState state,
            FileArtifact file,
            bool? allowReadOnlyOverride,
            AbsolutePath symlinkTarget,
            int? dependentFileIndex = null)
        {
            bool shouldMaterializeSourceFile = (IsDistributedWorker && SourceFileMaterializationEnabled) || symlinkTarget.IsValid;

            if (file.IsSourceFile && !shouldMaterializeSourceFile)
            {
                // Only distributed workers need to verify/materialize source files
                return;
            }

            TaskSourceSlim<PipOutputOrigin> materializationCompletion;
            Task<PipOutputOrigin> alreadyMaterializingTask;

            if (!TryReserveCompletion(m_materializationTasks, file, out alreadyMaterializingTask, out materializationCompletion))
            {
                if (dependentFileIndex != null)
                {
                    // Ensure the dependent artifact waits on the materialization of this file to complete
                    state.SetDependencyArtifactCompletion(dependentFileIndex.Value, alreadyMaterializingTask);
                }

                // Another thread tried to materialize this file
                // so add this to the list of pending placements so that we await the result before trying to place the other files.
                // Note: File is not added if it already finish materializing with a successful result since its safe
                // to just bypass it in this case
                if (alreadyMaterializingTask.Status != TaskStatus.RanToCompletion ||
                    alreadyMaterializingTask.Result == PipOutputOrigin.NotMaterialized)
                {
                    state.PendingPlacementTasks.Add((file, alreadyMaterializingTask));
                }
                else
                {
                    // Update OverallMaterializationResult
                    state.MergeResult(alreadyMaterializingTask.Result);
                }
            }
            else
            {
                if (dependentFileIndex != null)
                {
                    // Ensure the dependent artifact waits on the materialization of this file to complete
                    state.SetDependencyArtifactCompletion(dependentFileIndex.Value, materializationCompletion.Task);
                }

                // Don't query host for allow readonly for symlinks as this
                // has no effect and host may not be aware of the file (source
                // seal directory files)
                if (symlinkTarget.IsValid)
                {
                    allowReadOnlyOverride = false;
                }

                FileMaterializationInfo materializationInfo = symlinkTarget.IsValid
                    ? GetOrComputeSymlinkInputContent(file)
                    : GetInputContent(file);


                state.AddMaterializationFile(
                    fileToMaterialize: file,
                    allowReadOnly: allowReadOnlyOverride ?? AllowFileReadOnly(file),
                    materializationInfo: materializationInfo,
                    materializationCompletion: materializationCompletion,
                    symlinkTarget: symlinkTarget);
            }
        }

        private FileMaterializationInfo GetOrComputeSymlinkInputContent(FileArtifact file)
        {
            FileMaterializationInfo materializationInfo;
            if (!TryGetInputContent(file, out materializationInfo))
            {
                var hash = m_host.LocalDiskContentStore.ComputePathHash(file.Path.ToString(Context.PathTable));
                materializationInfo = new FileMaterializationInfo(FileContentInfo.CreateWithUnknownLength(hash), file.Path.GetName(Context.PathTable));
            }

            return materializationInfo;
        }

        private async Task<bool> DeleteFilesRequiredAbsentAsync(PipArtifactsState state, OperationContext operationContext)
        {
            // Don't do anything for materialization that are already completed by prior states
            state.RemoveCompletedMaterializations();

            bool deletionSuccess = await Task.Run(() =>
            {
                bool success = true;
                for (int i = 0; i < state.MaterializationFiles.Count; i++)
                {
                    MaterializationFile materializationFile = state.MaterializationFiles[i];

                    if (materializationFile.MaterializationInfo.Hash == WellKnownContentHashes.AbsentFile)
                    {
                        var file = materializationFile.Artifact;
                        var filePath = file.Path.ToString(Context.PathTable);

                        try
                        {
                            ContentMaterializationOrigin origin = ContentMaterializationOrigin.UpToDate;

                            if (FileUtilities.Exists(filePath))
                            {
                                // Delete the file if it exists
                                FileUtilities.DeleteFile(filePath, tempDirectoryCleaner: m_tempDirectoryCleaner);
                                origin = ContentMaterializationOrigin.DeployedFromCache;
                            }

                            state.SetMaterializationSuccess(i, origin: origin, operationContext: operationContext);
                        }
                        catch (BuildXLException ex)
                        {
                            Logger.Log.StorageRemoveAbsentFileOutputWarning(
                                operationContext,
                                pipDescription: GetAssociatedPipDescription(file),
                                destinationPath: filePath,
                                errorMessage: ex.LogEventMessage);

                            success = false;
                            state.SetMaterializationFailure(i);
                        }
                    }
                }

                return success;
            });

            return deletionSuccess;
        }

        /// <summary>
        /// Delete the contents of opaque (or dynamic) directories before deploying files from cache if directories exist; otherwise create empty directories.
        /// </summary>
        /// <remarks>
        /// Creating empty directories when they don't exist ensures the correctness of replaying pip outputs. Those existence of such directories may be needed
        /// by downstream pips. Empty directories are not stored into the cache, but their paths are stored in the pip itself and are collected when we populate <see cref="PipArtifactsState"/>.
        /// If the pip outputs are removed, then to replay the empty output directories in the next build, when we have a cache hit, those directories need to be created.
        /// </remarks>
        private async Task<bool> PrepareDirectoriesAsync(PipArtifactsState state, OperationContext operationContext)
        {
            bool deletionSuccess = await Task.Run(() =>
            {
                bool success = true;

                // Delete the contents of opaque directories before deploying files from cache, or re-create empty directories if they don't exist.
                foreach (var (directory, completion) in state.DirectoryDeletionCompletions)
                {
                    try
                    {
                        var dirOutputPath = directory.Path.ToString(Context.PathTable);

                        if (FileUtilities.DirectoryExistsNoFollow(dirOutputPath))
                        {
                            // Delete directory contents if the directory itself exists. Directory content deletion
                            // can throw an exception if users are naughty, e.g. they remove the output directory, rename directory, etc.
                            // The exception is thrown because the method tries to verify that the directory has been emptied by
                            // enumerating the directory or a descendant directory. Note that this is a possibly expensive I/O operation.
                            FileUtilities.DeleteDirectoryContents(
                                dirOutputPath,
                                shouldDelete: filePath =>
                                {
                                    using (
                                        operationContext.StartAsyncOperation(
                                            PipExecutorCounter.FileContentManagerDeleteDirectoriesPathParsingDuration))
                                    {
                                        var file = FileArtifact.CreateOutputFile(AbsolutePath.Create(Context.PathTable, filePath));

                                        // MaterializedDirectoryContents will contain all declared contents of the directory which should not be deleted
                                        // as the file may already have been materialized by the file content manager. If the file was not materialized
                                        // by the file content manager, it will be deleted and replaced as a part of file materialization
                                        return !state.MaterializedDirectoryContents.Contains(file);
                                    }
                                },
                                tempDirectoryCleaner: m_tempDirectoryCleaner);
                        }
                        else
                        {
                            if (FileUtilities.FileExistsNoFollow(dirOutputPath))
                            {
                                FileUtilities.DeleteFile(dirOutputPath, waitUntilDeletionFinished: true, tempDirectoryCleaner: m_tempDirectoryCleaner);
                            }

                            // If the directory does not exist, create one. This is to ensure that an opaque directory is always present on disk.
                            FileUtilities.CreateDirectory(dirOutputPath);
                        }

                        m_dynamicDirectoryDeletionTasks[directory] = BoolTask.True;
                        completion.SetResult(true);
                    }
                    catch (BuildXLException ex)
                    {
                        Logger.Log.StorageCacheCleanDirectoryOutputError(
                            operationContext,
                            pipDescription: GetAssociatedPipDescription(directory),
                            destinationPath: directory.Path.ToString(Context.PathTable),
                            errorMessage: ex.LogEventMessage);
                        state.AddFailedDirectory(directory);

                        success = false;

                        m_dynamicDirectoryDeletionTasks[directory] = BoolTask.False;
                        completion.SetResult(false);
                    }
                }

                return success;
            });

            var deletionResults = await Task.WhenAll(state.PendingDirectoryDeletions);
            deletionSuccess &= deletionResults.All(result => result);

            return deletionSuccess;
        }

        /// <summary>
        /// Attempt to place files from local cache
        /// </summary>
        /// <remarks>
        /// Logs warnings when a file placement fails; does not log errors.
        /// </remarks>
        private async Task<bool> PlaceFilesAsync(
            OperationContext operationContext,
            PipInfo pipInfo,
            PipArtifactsState state,
            bool materialize = true)
        {
            bool success = true;

            if (state.MaterializationFiles.Count != 0)
            {
                var pathTable = Context.PathTable;

                // Remove the completed materializations (this is mainly to remove source file 'materializations') which
                // may have already completed if running in the mode where source files are assumed to be materialized prior to the
                // start of the build on a distributed worker
                state.RemoveCompletedMaterializations();

                success &= await TryLoadAvailableContentAsync(
                    operationContext,
                    pipInfo,
                    state);

                if (!materialize)
                {
                    return success;
                }

                // Remove the failures
                // After calling TryLoadAvailableContentAsync some files may be marked completed (as failures)
                // we need to remove them so we don't try to place them
                state.RemoveCompletedMaterializations();

                // Maybe we didn't manage to fetch all of the remote content. However, for the content that was fetched,
                // we still are mandated to finish materializing if possible and eventually complete the materialization task.
                using (operationContext.StartOperation(PipExecutorCounter.FileContentManagerPlaceFilesDuration))
                {
                    for (int i = 0; i < state.MaterializationFiles.Count; i++)
                    {
                        MaterializationFile materializationFile = state.MaterializationFiles[i];
                        FileArtifact file = materializationFile.Artifact;
                        FileMaterializationInfo materializationInfo = materializationFile.MaterializationInfo;
                        ContentHash hash = materializationInfo.Hash;
                        PathAtom fileName = materializationInfo.FileName;
                        AbsolutePath symlinkTarget = materializationFile.SymlinkTarget;
                        bool allowReadOnly = materializationFile.AllowReadOnly;
                        int materializationFileIndex = i;

                        state.PlacementTasks.Add(Task.Run(
                            async () =>
                            {
                                // Wait for the prior version of the file artifact to finish materialization
                                await materializationFile.PriorArtifactVersionCompletion;

                                if (Context.CancellationToken.IsCancellationRequested)
                                {
                                    state.SetMaterializationFailure(fileIndex: materializationFileIndex);
                                    success = false;
                                    return;
                                }

                                Possible<ContentMaterializationResult> possiblyPlaced;

                                using (var outerContext = operationContext.StartAsyncOperation(PipExecutorCounter.FileContentManagerTryMaterializeOuterDuration, file))
                                using (await m_materializationSemaphore.AcquireAsync())
                                {
                                    if (m_host.CanMaterializeFile(file))
                                    {
                                        using (outerContext.StartOperation(PipExecutorCounter.FileContentManagerHostTryMaterializeDuration, file))
                                        {
                                            var possiblyMaterialized = await m_host.TryMaterializeFileAsync(file, outerContext);
                                            possiblyPlaced = possiblyMaterialized.Then(origin =>
                                                new ContentMaterializationResult(
                                                    origin,
                                                    TrackedFileContentInfo.CreateUntracked(materializationInfo.FileContentInfo)));
                                        }
                                    }
                                    else
                                    {
                                        using (outerContext.StartOperation(
                                            (symlinkTarget.IsValid || materializationInfo.ReparsePointInfo.ReparsePointType == ReparsePointType.SymLink)
                                                ? PipExecutorCounter.TryMaterializeSymlinkDuration
                                                : PipExecutorCounter.FileContentManagerTryMaterializeDuration,
                                            file))
                                        {
                                            if (state.VerifyMaterializationOnly)
                                            {
                                                // Ensure local existence by opening content stream.
                                                var possiblyStream = await ArtifactContentCache.TryOpenContentStreamAsync(hash);

                                                if (possiblyStream.Succeeded)
                                                {
#pragma warning disable AsyncFixer02
                                                    possiblyStream.Result.Dispose();
#pragma warning restore AsyncFixer02

                                                    possiblyPlaced =
                                                        new Possible<ContentMaterializationResult>(
                                                            new ContentMaterializationResult(
                                                                ContentMaterializationOrigin.DeployedFromCache,
                                                                TrackedFileContentInfo.CreateUntracked(materializationInfo.FileContentInfo, fileName)));
                                                    possiblyPlaced = WithLineInfo(possiblyPlaced);
                                                }
                                                else
                                                {
                                                    possiblyPlaced = new Possible<ContentMaterializationResult>(possiblyStream.Failure);
                                                    possiblyPlaced = WithLineInfo(possiblyPlaced);
                                                }
                                            }
                                            else
                                            {
                                                // Try materialize content.
                                                possiblyPlaced = await LocalDiskContentStore.TryMaterializeAsync(
                                                    ArtifactContentCache,
                                                    fileRealizationModes: GetFileRealizationMode(allowReadOnly: allowReadOnly),
                                                    path: file.Path,
                                                    contentHash: hash,
                                                    fileName: fileName,
                                                    symlinkTarget: symlinkTarget,
                                                    reparsePointInfo: materializationInfo.ReparsePointInfo);
                                                possiblyPlaced = WithLineInfo(possiblyPlaced);
                                            }
                                        }
                                    }
                                }

                                if (possiblyPlaced.Succeeded)
                                {
                                    state.SetMaterializationSuccess(
                                        fileIndex: materializationFileIndex,
                                        origin: possiblyPlaced.Result.Origin,
                                        operationContext: operationContext);

                                    m_host.ReportFileArtifactPlaced(file);
                                }
                                else
                                {
                                    Logger.Log.StorageCacheGetContentWarning(
                                        operationContext,
                                        pipDescription: pipInfo.Description,
                                        contentHash: hash.ToHex(),
                                        destinationPath: file.Path.ToString(pathTable),
                                        errorMessage: possiblyPlaced.Failure.DescribeIncludingInnerFailures());

                                    state.SetMaterializationFailure(fileIndex: materializationFileIndex);

                                    // Latch overall success (across all placements) to false.
                                    success = false;
                                }
                            }));
                    }
                    await Task.WhenAll(state.PlacementTasks);
                }
            }

            // Wait on any placements for files already in progress by other pips
            using (operationContext.StartOperation(PipExecutorCounter.FileContentManagerPlaceFilesDuration))
            {
                state.PlacementTasks.Clear();
                foreach (var pendingPlacementTask in state.PendingPlacementTasks)
                {
                    state.PlacementTasks.Add(pendingPlacementTask.Item2);
                }

                await Task.WhenAll(state.PlacementTasks);

                foreach (var pendingPlacement in state.PendingPlacementTasks)
                {
                    var result = await pendingPlacement.Item2;
                    if (result == PipOutputOrigin.NotMaterialized)
                    {
                        var file = pendingPlacement.fileArtifact;
                        state.AddFailedFile(file, GetInputContent(file).Hash);

                        // Not materialized indicates failure
                        success = false;
                    }
                    else
                    {
                        state.MergeResult(result);
                    }
                }
            }

            return success;
        }

        private static Possible<T> WithLineInfo<T>(Possible<T> possible, [CallerMemberName] string caller = null, [CallerLineNumber] int line = 0)
        {
            return possible.Succeeded ? possible : new Failure<string>(I($"Failure line info: {caller} ({line})"), possible.Failure);
        }

        private static PipOutputOrigin GetPipOutputOrigin(ContentMaterializationOrigin origin, Pip pip)
        {
            switch (pip.PipType)
            {
                case PipType.WriteFile:
                case PipType.CopyFile:
                    return origin.ToPipOutputOriginHidingDeploymentFromCache();
                default:
                    return origin.ToPipOutputOrigin();
            }
        }

        /// <summary>
        /// Attempt to bring multiple file contents into the local cache.
        /// </summary>
        /// <remarks>
        /// May log warnings. Does not log errors.
        /// </remarks>
        private Task<bool> TryLoadAvailableContentAsync(
            OperationContext operationContext,
            PipInfo pipInfo,
            PipArtifactsState state)
        {
            return TryLoadAvailableContentAsync(
                operationContext,
                pipInfo,
                state.MaterializingOutputs,
                state.GetCacheMaterializationContentHashes(),
                onFailure: failure =>
                {
                    for (int index = 0; index < state.MaterializationFiles.Count; index++)
                    {
                        state.SetMaterializationFailure(index);
                    }

                    state.InnerFailure = failure;
                },
                onContentUnavailable: (index, hashLogStr) =>
                {
                    state.SetMaterializationFailure(index);

                    // Log the eventual path on failure for sake of correlating the file within the build
                    if (Configuration.Schedule.StoreOutputsToCache)
                    {
                        Logger.Log.FailedToLoadFileContentWarning(
                            operationContext,
                            pipInfo.Description,
                            hashLogStr,
                            state.MaterializationFiles[index].Artifact.Path.ToString(Context.PathTable));
                    }
                },
                state: state);
        }

        /// <summary>
        /// Attempt to bring multiple file contents into the local cache.
        /// </summary>
        /// <remarks>
        /// May log warnings. Does not log errors.
        /// </remarks>
        private async Task<bool> TryLoadAvailableContentAsync(
            OperationContext operationContext,
            PipInfo pipInfo,
            bool materializingOutputs,
            IReadOnlyList<(FileArtifact fileArtifact, ContentHash contentHash, int fileIndex)> filesAndContentHashes,
            Action<Failure> onFailure,
            Action<int, string> onContentUnavailable,
            bool onlyLogUnavailableContent = false,
            PipArtifactsState state = null)
        {
            const string TargetUpToDate = "True";
            const string TargetNotUpToDate = "False";
            const string TargetNotChecked = "Not Checked";

            bool success = true;

            Possible<ContentAvailabilityBatchResult, Failure> possibleResults;
            using (operationContext.StartOperation(PipExecutorCounter.FileContentManagerTryLoadAvailableContentDuration))
            {
                possibleResults =
                    await
                        ArtifactContentCache.TryLoadAvailableContentAsync(
                            filesAndContentHashes.Select(pathAndContentHash => pathAndContentHash.Item2).ToList());
            }

            if (!possibleResults.Succeeded)
            {
                // Actual failure (distinct from a per-hash miss); should be unusual
                // We need to fail all materialization tasks since we don't have per-hash results.

                // TODO: We may want to check if the files on disk are up-to-date.
                Logger.Log.StorageBringProcessContentLocalWarning(
                    operationContext,
                    pipInfo.Description,
                    possibleResults.Failure.DescribeIncludingInnerFailures());

                onFailure(possibleResults.Failure);

                success = false;
            }
            else
            {
                ContentAvailabilityBatchResult resultsBatch = possibleResults.Result;
                ReadOnlyArray<ContentAvailabilityResult> results = resultsBatch.Results;
                Contract.Assert(filesAndContentHashes.Count == results.Length);

                for (int i = 0; i < results.Length; i++)
                {
                    if (Context.CancellationToken.IsCancellationRequested)
                    {
                        success = false;
                        break;
                    }

                    var result = results[i];
                    var fileArtifact = filesAndContentHashes[i].fileArtifact;
                    var contentHash = filesAndContentHashes[i].contentHash;
                    var currentFileIndex = filesAndContentHashes[i].fileIndex;

                    using (operationContext.StartOperation(OperationCounter.FileContentManagerHandleContentAvailability, fileArtifact))
                    {
                        Contract.Assume(contentHash == result.Hash);

                        bool isAvailable = result.IsAvailable;
                        string targetLocationUpToDate = TargetNotChecked;

                        if (!isAvailable)
                        {
                            Possible<ContentDiscoveryResult, Failure>? existingContent = null;

                            bool isPreservedOutputFile = IsPreservedOutputFile(pipInfo.UnderlyingPip, materializingOutputs, fileArtifact);
                            bool shouldDiscoverContentOnDisk =
                                Configuration.Schedule.ReuseOutputsOnDisk ||
                                !Configuration.Schedule.StoreOutputsToCache ||
                                isPreservedOutputFile;

                            if (shouldDiscoverContentOnDisk)
                            {
                                using (operationContext.StartOperation(OperationCounter.FileContentManagerDiscoverExistingContent, fileArtifact))
                                {
                                    // Discover the existing file (if any) and get its content hash
                                    existingContent =
                                        await LocalDiskContentStore.TryDiscoverAsync(fileArtifact);
                                }
                            }

                            if (existingContent.HasValue &&
                                existingContent.Value.Succeeded &&
                                existingContent.Value.Result.TrackedFileContentInfo.FileContentInfo.Hash == contentHash)
                            {
                                Contract.Assert(shouldDiscoverContentOnDisk);

                                targetLocationUpToDate = TargetUpToDate;

                                if (isPreservedOutputFile || !Configuration.Schedule.StoreOutputsToCache)
                                {
                                    // If file should be preserved, then we do not restore its content back to the cache.
                                    // If the preserved file is copied using a copy-file pip, then the materialization of
                                    // the copy-file destination relies on the else-clause below where we try to get
                                    // other file using TryGetFileArtifactForHash.
                                    // If we don't store outputs to cache, then we should not include cache operation to determine
                                    // if the content is available. However, we just checked, by TryDiscoverAsync above, that the content
                                    // is available with the expected content hash. Thus, we can safely say that the content is available.
                                    isAvailable = true;
                                }
                                else
                                {
                                    // The file has the correct hash so we can restore it back into the cache for future use/retention.
                                    // But don't restore files that need to be preserved because they were not stored to the cache.
                                    var possiblyStored =
                                        await RestoreContentInCacheAsync(
                                            operationContext,
                                            pipInfo.UnderlyingPip,
                                            materializingOutputs,
                                            fileArtifact,
                                            contentHash,
                                            fileArtifact);

                                    // Try to be conservative here due to distributed builds (e.g., the files may not exist on other machines).
                                    isAvailable = possiblyStored.Succeeded;
                                }

                                if (isAvailable)
                                {
                                    // Content is up to date and available, so just mark the file as successfully materialized.
                                    state?.SetMaterializationSuccess(currentFileIndex, ContentMaterializationOrigin.UpToDate, operationContext);
                                }
                            }
                            else
                            {
                                if (shouldDiscoverContentOnDisk)
                                {
                                    targetLocationUpToDate = TargetNotUpToDate;
                                }

                                // If the up-to-dateness of file on disk is not checked, or the file on disk is not up-to-date,
                                // then fall back to using the cache.

                                // Attempt to find a materialized file for the hash and store that
                                // into the cache to ensure the content is available
                                // This is mainly used for incremental scheduling which does not account
                                // for content which has been evicted from the cache when performing copies
                                FileArtifact otherFile = TryGetFileArtifactForHash(contentHash);
                                if (!otherFile.IsValid)
                                {
                                    FileArtifact copyOutput = fileArtifact;
                                    FileArtifact copySource;
                                    while (m_host.TryGetCopySourceFile(copyOutput, out copySource))
                                    {
                                        // Use the source of the copy file as the file to restore
                                        otherFile = copySource;

                                        if (copySource.IsSourceFile)
                                        {
                                            // Reached a source file. Just abort rather than calling the host again.
                                            break;
                                        }

                                        // Try to keep going back through copy chain
                                        copyOutput = copySource;
                                    }
                                }

                                if (otherFile.IsValid)
                                {
                                    if (otherFile.IsSourceFile || IsFileMaterialized(otherFile))
                                    {
                                        var possiblyStored =
                                            await RestoreContentInCacheAsync(
                                                operationContext,
                                                pipInfo.UnderlyingPip,
                                                materializingOutputs,
                                                otherFile,
                                                contentHash,
                                                fileArtifact);

                                        isAvailable = possiblyStored.Succeeded;
                                    }
                                    else if (state != null && m_host.CanMaterializeFile(otherFile))
                                    {
                                        // Add the file containing the required content to the list of files to be materialized.
                                        // The added to the list rather than inlining the materializing to prevent duplicate/concurrent
                                        // materializations of the same file. It also ensures that the current file waits on the materialization
                                        // of the other file before attempting materialization.

                                        if (!TryGetInputContent(otherFile, out var otherFileMaterializationInfo))
                                        {
                                            // Need to set the materialization info in case it is not set on the current machine (i.e. distributed worker)
                                            // This can happen with copied write file outputs. Since the hash of the write file output will not be transferred to worker
                                            // but instead the copied output consumed by the pip will be transferred. We use the hash from the copied file since it is
                                            // the same. We recreate without the file name because copied files can have different names that the originating file.
                                            otherFileMaterializationInfo = FileMaterializationInfo.CreateWithUnknownName(state.MaterializationFiles[currentFileIndex].MaterializationInfo.FileContentInfo);
                                            ReportInputContent(otherFile, otherFileMaterializationInfo);
                                        }

                                        // Example (dataflow graph)
                                        // W[F_W0] -> C[F_C0] -> P1,P2
                                        // Where 'W' is a write file which writes an output 'F_W0' with hash '#W0'
                                        // Where 'C' is a copy file which copies 'F_W0' to 'F_C0' (with hash '#W0').
                                        // Where 'P1' and 'P2' are consumers of 'F_C0'

                                        // In this case when P1 materializes inputs,
                                        // This list of materialized files are:
                                        // 0: F_C0 = #W0 (i.e. materialize file with hash #W0 at the location F_C0)

                                        // When processing F_C0 (currentFileIndex=0) we enter this call and
                                        // The add file materialization call adds an entry to the list of files to materialize
                                        // and modifies F_C0 entry to wait for the completion of F_W0
                                        // 0: C0 = #W0 (+ wait for F_W0 to complete materialization)
                                        // + 1: F_W0 = #W0
                                        AddFileMaterialization(
                                            state,
                                            otherFile,
                                            allowReadOnlyOverride: null,
                                            symlinkTarget: AbsolutePath.Invalid,
                                            // Ensure that the current file waits on the materialization before attempting its materialization.
                                            // This ensures that content is present in the cache
                                            dependentFileIndex: currentFileIndex);
                                        isAvailable = true;
                                    }
                                }
                            }
                        }

                        // Log the result of each requested hash
                        string hashLogStr = contentHash.ToHex();

                        using (operationContext.StartOperation(OperationCounter.FileContentManagerHandleContentAvailabilityLogContentAvailability, fileArtifact))
                        {
                            if (!onlyLogUnavailableContent || !isAvailable)
                            {
                                if (materializingOutputs)
                                {
                                    Logger.Log.ScheduleCopyingPipOutputToLocalStorage(
                                        operationContext,
                                        pipInfo.Description,
                                        hashLogStr,
                                        result: isAvailable,
                                        targetLocationUpToDate: targetLocationUpToDate,
                                        remotelyCopyBytes: result.BytesTransferred);
                                }
                                else
                                {
                                    Logger.Log.ScheduleCopyingPipInputToLocalStorage(
                                        operationContext,
                                        pipInfo.SemiStableHash,
                                        pipInfo.Description,
                                        hashLogStr,
                                        result: isAvailable,
                                        targetLocationUpToDate: targetLocationUpToDate,
                                        remotelyCopyBytes: result.BytesTransferred);
                                }
                            }

                            if (result.IsAvailable)
                            {
                                // The result was available in cache so report it
                                // Note that, in the above condition, we are using "result.isAvailable" instead of "isAvailable".
                                // If we used "isAvailable", the couter would be incorrect because "isAvailable" can also mean that
                                // the artifact is already on disk (or in the object folder). This can happen when the preserved-output
                                // mode is enabled or when BuildXL doesn't store outputs to cache.
                                ReportTransferredArtifactToLocalCache(true, result.BytesTransferred, result.SourceCache);
                            }

                            // Misses for content are graceful (i.e., the 'load available content' succeeded but deemed something unavailable).
                            // We need to fail the materialization task in that case; there may be other waiters for the same hash / file.
                            if (!isAvailable)
                            {
                                success = false;
                                onContentUnavailable(currentFileIndex, hashLogStr);
                            }
                        }
                    }
                }
            }

            return success;
        }

        private FileRealizationMode GetFileRealizationModeForCacheRestore(
            Pip pip,
            bool materializingOutputs,
            FileArtifact file,
            AbsolutePath targetPath)
        {
            if (file.Path != targetPath)
            {
                // File has different path from the target path.
                return FileRealizationMode.Copy;
            }

            bool isPreservedOutputFile = IsPreservedOutputFile(pip, materializingOutputs, file);

            bool allowReadOnly = materializingOutputs
                ? !PipArtifacts.IsOutputMustRemainWritablePip(pip)
                : AllowFileReadOnly(file);

            return GetFileRealizationMode(allowReadOnly && !isPreservedOutputFile);
        }

        private async Task<Possible<TrackedFileContentInfo>> RestoreContentInCacheAsync(
            OperationContext operationContext,
            Pip pip,
            bool materializingOutputs,
            FileArtifact fileArtifact,
            ContentHash hash,
            FileArtifact targetFile)
        {
            if (!Configuration.Schedule.StoreOutputsToCache && !m_host.IsFileRewritten(targetFile))
            {
                return new Failure<string>("Storing content to cache is not allowed");
            }

            using (operationContext.StartOperation(OperationCounter.FileContentManagerRestoreContentInCache))
            {
                FileRealizationMode fileRealizationMode = GetFileRealizationModeForCacheRestore(pip, materializingOutputs, fileArtifact, targetFile.Path);
                bool shouldReTrack = fileRealizationMode != FileRealizationMode.Copy;

                var possiblyStored = await LocalDiskContentStore.TryStoreAsync(
                    ArtifactContentCache,
                    fileRealizationModes: fileRealizationMode,
                    path: fileArtifact.Path,
                    tryFlushPageCacheToFileSystem: shouldReTrack,
                    knownContentHash: hash,
                    trackPath: shouldReTrack);

                return possiblyStored;
            }
        }

        private void ReportTransferredArtifactToLocalCache(bool contentIsLocal, long transferredBytes, string sourceCache)
        {
            if (contentIsLocal && transferredBytes > 0)
            {
                Interlocked.Increment(ref m_stats.ArtifactsBroughtToLocalCache);
                Interlocked.Add(ref m_stats.TotalSizeArtifactsBroughtToLocalCache, transferredBytes);
            }

            m_cacheContentSource.AddOrUpdate(sourceCache, 1, (key, value) => value + 1);
        }

        private async Task<bool> VerifySourceFileInputsAsync(
            OperationContext operationContext,
            PipInfo pipInfo,
            PipArtifactsState state)
        {
            Contract.Requires(IsDistributedWorker);

            var pathTable = Context.PathTable;
            bool success = true;

            for (int i = 0; i < state.MaterializationFiles.Count; i++)
            {
                if (Context.CancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                MaterializationFile materializationFile = state.MaterializationFiles[i];
                FileArtifact file = materializationFile.Artifact;
                bool createSymlink = materializationFile.CreateSymlink;

                if (file.IsSourceFile && !createSymlink)
                {
                    // Start the task to hash input
                    state.HashTasks.Add(TryQueryContentAsync(
                        file,
                        operationContext,
                        declaredArtifact: file,
                        pipInfo.UnderlyingPip.ProcessAllowsUndeclaredSourceReads,
                        verifyingHash: true));
                }
                else
                {
                    // Just store placeholder task for output files/lazy symlinks since they are not verified
                    state.HashTasks.Add(s_placeHolderFileHashTask);
                }
            }

            for (int i = 0; i < state.MaterializationFiles.Count; i++)
            {
                MaterializationFile materializationFile = state.MaterializationFiles[i];
                FileArtifact file = materializationFile.Artifact;
                var materializationInfo = materializationFile.MaterializationInfo;
                var expectedHash = materializationInfo.Hash;
                bool createSymlink = materializationFile.CreateSymlink;

                if (file.IsOutputFile ||

                    // TODO: Bug #995938: Temporary hack to handle pip graph construction verifcation oversight
                    // where source files declared inside output directories
                    state.MaterializedDirectoryContents.Contains(file.Path) ||

                    // Don't verify if it a symlink creation
                    createSymlink)
                {
                    // Only source files should be verified
                    continue;
                }

                FileMaterializationInfo? maybeFileInfo = await state.HashTasks[i];
                bool sourceFileHashMatches = maybeFileInfo?.Hash.Equals(expectedHash) == true;

                if (SourceFileMaterializationEnabled)
                {
                    // Only attempt to materialize the file if the hash does not match
                    if (!sourceFileHashMatches)
                    {
                        if (expectedHash.IsSpecialValue() && expectedHash != WellKnownContentHashes.AbsentFile)
                        {
                            // We are trying to materialize the source file to a special value (like untracked) so
                            // just set it to succeed since these values cannot actually be materialized
                            // AbsentFile however can be materialized by deleting the file
                            state.SetMaterializationSuccess(fileIndex: i, origin: ContentMaterializationOrigin.UpToDate, operationContext: operationContext);
                        }

                        if (maybeFileInfo.Value.Hash == WellKnownContentHashes.AbsentFile)
                        {
                            Logger.Log.PipInputVerificationMismatchRecoveryExpectedExistence(
                                operationContext,
                                pipInfo.SemiStableHash,
                                pipInfo.Description,
                                filePath: file.Path.ToString(pathTable));
                        }
                        else if (expectedHash == WellKnownContentHashes.AbsentFile)
                        {
                            Logger.Log.PipInputVerificationMismatchRecoveryExpectedNonExistence(
                                operationContext,
                                pipInfo.SemiStableHash,
                                pipInfo.Description,
                                filePath: file.Path.ToString(pathTable));
                        }
                        else
                        {
                            Logger.Log.PipInputVerificationMismatchRecovery(
                                operationContext,
                                pipInfo.SemiStableHash,
                                pipInfo.Description,
                                actualHash: maybeFileInfo.Value.Hash.ToHex(),
                                expectedHash: expectedHash.ToHex(),
                                filePath: file.Path.ToString(pathTable));
                        }

                        // Just continue rather than reporting result so that the file will be materialized
                        continue;
                    }
                }
                else
                {
                    // Not materializing source files, so verify that the file matches instead
                    if (!maybeFileInfo.HasValue)
                    {
                        Logger.Log.PipInputVerificationUntrackedInput(
                            operationContext,
                            pipInfo.SemiStableHash,
                            pipInfo.Description,
                            file.Path.ToString(pathTable));
                    }
                    else if (maybeFileInfo.Value.Hash != expectedHash)
                    {
                        var actualFileInfo = maybeFileInfo.Value;
                        ReportWorkerContentMismatch(operationContext, pathTable, file, expectedHash, actualFileInfo.Hash);
                    }
                }

                if (sourceFileHashMatches)
                {
                    state.SetMaterializationSuccess(
                        fileIndex: i,
                        origin: ContentMaterializationOrigin.UpToDate,
                        operationContext: operationContext);
                }
                else
                {
                    state.SetMaterializationFailure(fileIndex: i);
                    success = false;
                }
            }

            Contract.Assert(success || operationContext.LoggingContext.WarningWasLogged, "Warning must be logged if source file verification fails");
            return success;
        }

        private static void ReportWorkerContentMismatch(
            LoggingContext loggingContext,
            PathTable pathTable,
            FileArtifact file,
            ContentHash expectedHash,
            ContentHash actualHash)
        {
            if (actualHash == WellKnownContentHashes.AbsentFile)
            {
                Logger.Log.PipInputVerificationMismatchExpectedExistence(
                    loggingContext,
                    filePath: file.Path.ToString(pathTable));
            }
            else if (expectedHash == WellKnownContentHashes.AbsentFile)
            {
                Logger.Log.PipInputVerificationMismatchExpectedNonExistence(
                    loggingContext,
                    filePath: file.Path.ToString(pathTable));
            }
            else
            {
                Logger.Log.PipInputVerificationMismatch(
                    loggingContext,
                    actualHash: actualHash.ToHex(),
                    expectedHash: expectedHash.ToHex(),
                    filePath: file.Path.ToString(pathTable));
            }
        }

        private DirectoryArtifact TryGetSealSourceAncestor(AbsolutePath path)
        {
            // Walk the parent directories of the sealedPath to find if it is under a sealedSourceDirectory.
            // The entries are cached and short-circuited otherwise
            var pathTable = Context.PathTable;
            var initialDirectory = path.GetParent(pathTable);
            var currentPath = initialDirectory;

            while (currentPath.IsValid)
            {
                DirectoryArtifact directory;
                if (m_sealedSourceDirectories.TryGetValue(currentPath, out directory))
                {
                    // Cache the parent folder of the file so that subsequent lookups don't have to traverse the parent chain.
                    if (currentPath != path && currentPath != initialDirectory)
                    {
                        m_sealedSourceDirectories.TryAdd(initialDirectory, directory);
                    }

                    return directory;
                }

                currentPath = currentPath.GetParent(pathTable);
            }

            return DirectoryArtifact.Invalid;
        }

        private static bool TryReserveCompletion<TKey, TResult>(
            ConcurrentBigMap<TKey, Task<TResult>> taskCompletionMap,
            TKey key,
            out Task<TResult> retrievedTask,
            out TaskSourceSlim<TResult> addedTaskCompletionSource)
        {
            Task<TResult> taskResult;
            if (taskCompletionMap.TryGetValue(key, out taskResult))
            {
                retrievedTask = taskResult;
                addedTaskCompletionSource = default;
                return false;
            }

            addedTaskCompletionSource = TaskSourceSlim.Create<TResult>();
            var actualMaterializationTask = taskCompletionMap.GetOrAdd(key, addedTaskCompletionSource.Task).Item.Value;

            if (actualMaterializationTask != addedTaskCompletionSource.Task)
            {
                retrievedTask = actualMaterializationTask;
                addedTaskCompletionSource = default;
                return false;
            }

            retrievedTask = null;
            return true;
        }

        /// <summary>
        /// Computes a content hash for a file artifact presently on disk.
        /// Computed content hashes are stored in the scheduler's <see cref="FileContentTable" />, if present.
        /// </summary>
        private async Task<TrackedFileContentInfo?> GetAndRecordSourceFileContentHashAsync(
            OperationContext operationContext,
            FileArtifact fileArtifact,
            FileOrDirectoryArtifact declaredArtifact,
            bool allowUndeclaredSourceReads,
            string consumerDescription = null)
        {
            Contract.Requires(fileArtifact.IsValid);
            Contract.Requires(fileArtifact.IsSourceFile);

            SemanticPathInfo mountInfo = SemanticPathExpander.GetSemanticPathInfo(fileArtifact.Path);

            // if there is a declared mount for the file, it has to allow hashing
            // otherwise, if there is no mount defined, we only hash the file if allowed undeclared source reads is on (and for some tests)
            if ((mountInfo.IsValid && mountInfo.AllowHashing) ||
                (!mountInfo.IsValid && (TrackFilesUnderInvalidMountsForTests || allowUndeclaredSourceReads)))
            {
                return await GetAndRecordFileContentHashAsyncCore(operationContext, fileArtifact, declaredArtifact, consumerDescription);
            }

            if (!mountInfo.IsValid)
            {
                Logger.Log.ScheduleIgnoringUntrackedSourceFileNotUnderMount(operationContext, fileArtifact.Path.ToString(Context.PathTable));
            }
            else
            {
                Logger.Log.ScheduleIgnoringUntrackedSourceFileUnderMountWithHashingDisabled(
                    operationContext,
                    fileArtifact.Path.ToString(Context.PathTable),
                    mountInfo.RootName.ToString(Context.StringTable));
            }

            Interlocked.Increment(ref m_stats.SourceFilesUntracked);
            return TrackedFileContentInfo.CreateUntrackedWithUnknownLength(WellKnownContentHashes.UntrackedFile);
        }

        private string GetAssociatedPipDescription(FileOrDirectoryArtifact declaredArtifact, string consumerDescription = null)
        {
            if (consumerDescription != null)
            {
                return consumerDescription;
            }

            if (declaredArtifact.IsFile)
            {
                DirectoryArtifact dynamicDirectoryArtifact;
                if (declaredArtifact.FileArtifact.IsSourceFile)
                {
                    consumerDescription = m_host.GetConsumerDescription(declaredArtifact);
                    if (consumerDescription != null)
                    {
                        return consumerDescription;
                    }
                }
                else if (m_dynamicOutputFileDirectories.TryGetValue(declaredArtifact.FileArtifact, out dynamicDirectoryArtifact))
                {
                    declaredArtifact = dynamicDirectoryArtifact;
                }
            }

            return m_host.GetProducerDescription(declaredArtifact);
        }

        private async Task<TrackedFileContentInfo?> GetAndRecordFileContentHashAsyncCore(
            OperationContext operationContext,
            FileArtifact fileArtifact,
            FileOrDirectoryArtifact declaredArtifact,
            string consumerDescription = null)
        {
            using (var outerContext = operationContext.StartAsyncOperation(PipExecutorCounter.FileContentManagerGetAndRecordFileContentHashDuration, fileArtifact))
            {
                ExpandedAbsolutePath artifactExpandedPath = fileArtifact.Path.Expand(Context.PathTable);
                string artifactFullPath = artifactExpandedPath.ExpandedPath;

                TrackedFileContentInfo fileTrackedHash;
                Possible<PathExistence> possibleProbeResult = LocalDiskFileSystemView.TryProbeAndTrackPathForExistence(artifactExpandedPath);
                if (!possibleProbeResult.Succeeded)
                {
                    Logger.Log.FailedToHashInputFileDueToFailedExistenceCheck(
                        operationContext,
                        m_host.GetProducerDescription(declaredArtifact),
                        artifactFullPath,
                        possibleProbeResult.Failure.DescribeIncludingInnerFailures());
                    return null;
                }

                if (possibleProbeResult.Result == PathExistence.ExistsAsDirectory)
                {
                    // Record the fact that this path is a directory for use by TryQuerySealedOrUndeclaredInputContent.
                    // Special behavior is used for directories
                    m_contentQueriedDirectoryPaths.Add(fileArtifact.Path);
                }

                if (possibleProbeResult.Result == PathExistence.ExistsAsFile)
                {
                    Possible<ContentDiscoveryResult> possiblyDiscovered =
                        await LocalDiskContentStore.TryDiscoverAsync(fileArtifact, artifactExpandedPath);

                    DiscoveredContentHashOrigin origin;
                    if (possiblyDiscovered.Succeeded)
                    {
                        fileTrackedHash = possiblyDiscovered.Result.TrackedFileContentInfo;
                        origin = possiblyDiscovered.Result.Origin;
                    }
                    else
                    {
                        // We may fail to access a file due to permissions issues, or due to some other process that has the file locked (opened for writing?)
                        Logger.Log.FailedToHashInputFile(
                            operationContext,
                            GetAssociatedPipDescription(declaredArtifact, consumerDescription),
                            artifactFullPath,
                            possiblyDiscovered.Failure.CreateException());
                        return null;
                    }

                    switch (origin)
                    {
                        case DiscoveredContentHashOrigin.NewlyHashed:
                            if (fileArtifact.IsSourceFile)
                            {
                                Interlocked.Increment(ref m_stats.SourceFilesHashed);
                            }
                            else
                            {
                                Interlocked.Increment(ref m_stats.OutputFilesHashed);
                            }

                            Logger.Log.StorageHashedSourceFile(operationContext, artifactFullPath, fileTrackedHash.Hash.ToHex());
                            break;
                        case DiscoveredContentHashOrigin.Cached:
                            if (fileArtifact.IsSourceFile)
                            {
                                Interlocked.Increment(ref m_stats.SourceFilesUnchanged);
                            }
                            else
                            {
                                Interlocked.Increment(ref m_stats.OutputFilesUnchanged);
                            }

                            Logger.Log.StorageUsingKnownHashForSourceFile(operationContext, artifactFullPath, fileTrackedHash.Hash.ToHex());
                            break;
                        default:
                            throw Contract.AssertFailure("Unhandled DiscoveredContentHashOrigin");
                    }
                }
                else if (possibleProbeResult.Result == PathExistence.ExistsAsDirectory)
                {
                    // Attempted to query the hash of a directory
                    // Case 1: For declared source files when TreatDirectoryAsAbsentFileOnHashingInputContent=true, we treat them as absent file hash
                    // Case 2: For declared source files when TreatDirectoryAsAbsentFileOnHashingInputContent=false, we return null and error
                    // Case 3: For other files (namely paths under sealed source direcotories or outputs), we return null. Outputs will error. Paths under
                    // sealed source directories will be handled by ObservedInputProcessor which will treat them as Enumeration/DirectoryProbe.
                    if (fileArtifact.IsSourceFile && declaredArtifact.IsFile)
                    {
                        // Declared source file
                        if (Configuration.Schedule.TreatDirectoryAsAbsentFileOnHashingInputContent)
                        {
                            // Case 1:
                            fileTrackedHash = TrackedFileContentInfo.CreateUntrackedWithUnknownLength(WellKnownContentHashes.AbsentFile, possibleProbeResult.Result);
                        }
                        else
                        {
                            // Case 2:
                            // We only log the warning for hash source file pips
                            Logger.Log.FailedToHashInputFileBecauseTheFileIsDirectory(
                                operationContext,
                                GetAssociatedPipDescription(declaredArtifact, consumerDescription),
                                artifactFullPath);

                            // This should error
                            return null;
                        }
                    }
                    else
                    {
                        // Case 3:
                        // Path under sealed source directory
                        // Caller will not error since this is a valid operation. ObservedInputProcessor will later discover that this is a directory
                        return null;
                    }
                }
                else
                {
                    Interlocked.Increment(ref m_stats.SourceFilesAbsent);
                    fileTrackedHash = TrackedFileContentInfo.CreateUntrackedWithUnknownLength(WellKnownContentHashes.AbsentFile, possibleProbeResult.Result);
                }

                if (BuildXL.Scheduler.ETWLogger.Log.IsEnabled(BuildXL.Tracing.Diagnostics.EventLevel.Verbose, Events.Keywords.Diagnostics))
                {
                    if (fileArtifact.IsSourceFile)
                    {
                        Logger.Log.ScheduleHashedSourceFile(operationContext, artifactFullPath, fileTrackedHash.Hash.ToHex());
                    }
                    else
                    {
                        Logger.Log.ScheduleHashedOutputFile(
                            operationContext,
                            GetAssociatedPipDescription(declaredArtifact, consumerDescription),
                            artifactFullPath,
                            fileTrackedHash.Hash.ToHex());
                    }
                }

                return fileTrackedHash;
            }
        }

        private FileRealizationMode GetFileRealizationMode(bool allowReadOnly)
        {
            return Configuration.Engine.UseHardlinks && allowReadOnly
                ? FileRealizationMode.HardLinkOrCopy // Prefers hardlinks, but will fall back to copying when creating a hard link fails. (e.g. >1023 links)
                : FileRealizationMode.Copy;
        }

        private void UpdateOutputContentStats(PipOutputOrigin origin)
        {
            switch (origin)
            {
                case PipOutputOrigin.UpToDate:
                    Interlocked.Increment(ref m_stats.OutputsUpToDate);
                    break;
                case PipOutputOrigin.DeployedFromCache:
                    Interlocked.Increment(ref m_stats.OutputsDeployed);
                    break;

                case PipOutputOrigin.Produced:
                    Interlocked.Increment(ref m_stats.OutputsProduced);
                    break;

                case PipOutputOrigin.NotMaterialized:
                    break;

                default:
                    throw Contract.AssertFailure("Unhandled PipOutputOrigin");
            }
        }

        private bool ReportContent(
            FileArtifact fileArtifact,
            in FileMaterializationInfo fileMaterializationInfo,
            PipOutputOrigin origin,
            bool doubleWriteErrorsAreWarnings = false)
        {
            SetFileArtifactContentHashResult result = SetFileArtifactContentHash(
                fileArtifact,
                fileMaterializationInfo,
                origin);

            // Notify the host with content that was reported
            m_host.ReportContent(fileArtifact, fileMaterializationInfo, origin);

            if (result == SetFileArtifactContentHashResult.Added)
            {
                return true;
            }

            if (result == SetFileArtifactContentHashResult.HasMatchingExistingEntry)
            {
                return false;
            }

            Contract.Equals(SetFileArtifactContentHashResult.HasConflictingExistingEntry, result);

            var existingInfo = m_fileArtifactContentHashes[fileArtifact];
            if (!Configuration.Sandbox.UnsafeSandboxConfiguration.UnexpectedFileAccessesAreErrors || doubleWriteErrorsAreWarnings)
            {
                // If we reached this case and UnexpectedFileAccessesAreErrors is false or
                // pip level option doubleWriteErrorsAreWarnings is set to true, that means
                // the flag supressed a double write violation detection. So let's just warn
                // and move on.
                Logger.Log.FileArtifactContentMismatch(
                    m_host.LoggingContext,
                    fileArtifact.Path.ToString(Context.PathTable),
                    existingInfo.Hash.ToHex(),
                    fileMaterializationInfo.Hash.ToHex());

                return false;
            }

            throw Contract.AssertFailure(I($"Content hash of file artifact '{fileArtifact.Path.ToString(Context.PathTable)}:{fileArtifact.RewriteCount}' can be set multiple times, but only with the same content hash (old hash: {existingInfo.Hash.ToHex()}, new hash: {fileMaterializationInfo.Hash.ToHex()})"));
        }

        private enum SetFileArtifactContentHashResult
        {
            /// <summary>
            /// Found entry with differing content hash
            /// </summary>
            HasConflictingExistingEntry,

            /// <summary>
            /// Found entry with the same content hash
            /// </summary>
            HasMatchingExistingEntry,

            /// <summary>
            /// New entry was added with the given content hash
            /// </summary>
            Added,
        }

        /// <summary>
        /// Records the given file artifact as having the given content hash.
        /// </summary>
        private SetFileArtifactContentHashResult SetFileArtifactContentHash(
            FileArtifact fileArtifact,
            in FileMaterializationInfo fileMaterializationInfo,
            PipOutputOrigin origin)
        {
            Contract.Requires(fileArtifact.IsValid, "Argument fileArtifact must be valid");
            AssertFileNamesMatch(Context, fileArtifact, fileMaterializationInfo);

            var result = m_fileArtifactContentHashes.GetOrAdd(fileArtifact, fileMaterializationInfo);
            if (result.IsFound)
            {
                FileContentInfo storedFileContentInfo = result.Item.Value.FileContentInfo;
                if (storedFileContentInfo.Hash != fileMaterializationInfo.Hash)
                {
                    // We allow the same hash to be reported multiple times, but only with the same content hash.
                    return SetFileArtifactContentHashResult.HasConflictingExistingEntry;
                }

                if (storedFileContentInfo.HasKnownLength &&
                    fileMaterializationInfo.FileContentInfo.HasKnownLength &&
                    storedFileContentInfo.Length != fileMaterializationInfo.Length)
                {
                    Contract.Assert(false,
                        $"File length mismatch for file '{fileMaterializationInfo.FileName}' :: " +
                        $"arg = {{ hash: {fileMaterializationInfo.Hash.ToHex()}, length: {fileMaterializationInfo.Length} }}, " +
                        $"stored = {{ hash: {storedFileContentInfo.Hash.ToHex()}, length: {storedFileContentInfo.Length}, rawLength: {storedFileContentInfo.RawLength}, existence: {storedFileContentInfo.Existence} }}");
                }
            }

            bool added = !result.IsFound;
            var contentId = new ContentId(result.Index);
            var contentSetItem = new ContentIdSetItem(contentId, fileMaterializationInfo.Hash, this);

            bool? isNewContent = null;
            if (origin != PipOutputOrigin.NotMaterialized)
            {
                // Mark the file as materialized
                // Due to StoreNoOutputToCache, we need to update the materialization task.
                // For copy file pip, with StoreNoOutputToCache enabled, BuildXL first tries to materialize the output, but
                // because the output is most likely not in the cache, TryLoadAvailableContent will fail and subsequently
                // will set the materialization task for the output file to NotMaterialized.
                var originAsTask = ToTask(origin);

                var addOrUpdateResult = m_materializationTasks.AddOrUpdate(
                    fileArtifact,
                    ToTask(origin),
                    (f, newOrigin) => newOrigin,
                    (f, newOrigin, oldOrigin) => oldOrigin.Result == PipOutputOrigin.NotMaterialized ? newOrigin : oldOrigin);

                if (!addOrUpdateResult.IsFound || addOrUpdateResult.OldItem.Value.Result == PipOutputOrigin.NotMaterialized)
                {
                    EnsureHashMappedToMaterializedFile(contentSetItem, isNewContent: out isNewContent);
                }

                m_host.ReportMaterializedArtifact(fileArtifact);
            }

            if (added)
            {
                if (fileMaterializationInfo.FileContentInfo.HasKnownLength &&
                    (isNewContent ?? m_allCacheContentHashes.AddItem(contentSetItem)))
                {
                    if (fileArtifact.IsOutputFile)
                    {
                        Interlocked.Add(ref m_stats.TotalCacheSizeNeeded, fileMaterializationInfo.Length);
                    }
                }

                ExecutionLog?.FileArtifactContentDecided(new FileArtifactContentDecidedEventData
                {
                    FileArtifact = fileArtifact,
                    FileContentInfo = fileMaterializationInfo.FileContentInfo,
                    OutputOrigin = origin,
                });

                return SetFileArtifactContentHashResult.Added;
            }

            return SetFileArtifactContentHashResult.HasMatchingExistingEntry;
        }

        /// <summary>
        /// Gets whether a file artifact is materialized
        /// </summary>
        private bool IsFileMaterialized(FileArtifact file)
        {
            Task<PipOutputOrigin> materializationResult;
            return m_materializationTasks.TryGetValue(file, out materializationResult)
                    && materializationResult.IsCompleted
                    && materializationResult.Result != PipOutputOrigin.NotMaterialized;
        }

        private bool AllowFileReadOnly(FileArtifact file)
        {
            Contract.Requires(file.IsValid);

            // File can be a dynamic output. First get the declared artifact.
            FileOrDirectoryArtifact declaredArtifact = GetDeclaredArtifact(file);
            return m_host.AllowArtifactReadOnly(declaredArtifact);
        }

        private bool IsPreservedOutputFile(FileArtifact file)
        {
            Contract.Requires(file.IsValid);

            // File can be a dynamic output. First get the declared artifact.
            FileOrDirectoryArtifact declaredArtifact = GetDeclaredArtifact(file);
            return m_host.IsPreservedOutputArtifact(declaredArtifact);
        }

        private bool IsPreservedOutputFile(Pip pip, bool materializingOutput, FileArtifact file)
        {
            Contract.Requires(file.IsValid);

            if (Configuration.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs == PreserveOutputsMode.Disabled)
            {
                return false;
            }

            if (!materializingOutput)
            {
                return IsPreservedOutputFile(file);
            }

            var pipId = m_host.TryGetProducerId(file);
            if (pipId.IsValid)
            {
                Contract.Assert(pipId == pip.PipId);
                return PipArtifacts.IsPreservedOutputsPip(pip);
            }

            // Invalid pip id indicates that the file is a dynamic output.
            return false;
        }

        /// <summary>
        /// Adds the mapping of hash to file into <see cref="m_allCacheContentHashes"/>
        /// </summary>
        private void EnsureHashMappedToMaterializedFile(ContentIdSetItem materializedFileContentItem, out bool? isNewContent)
        {
            // Update m_allCacheContentHashes to point to the content hash for a materialized
            // file (used to recover content which cannot be materialized because it is not available in
            // the cache by storing the content from the materialized file).
            // Only updated if current value does not point to materialized file to avoid
            // lock contention with lots of files with the same content (not known to be a problem
            // but this logic is added as a precaution)
            var hashAddResult = m_allCacheContentHashes.GetOrAddItem(materializedFileContentItem);
            isNewContent = !hashAddResult.IsFound;
            if (hashAddResult.IsFound)
            {
                // Only update m_allCacheContentHashes if there was already an entry
                // and it was not materialized
                FileArtifact contentFile = hashAddResult.Item.GetFile(this);
                if (!IsFileMaterialized(contentFile))
                {
                    m_allCacheContentHashes.UpdateItem(materializedFileContentItem);
                }
            }
        }

        /// <summary>
        /// Attempts to get the file artifact which has the given hash (if any)
        /// </summary>
        private FileArtifact TryGetFileArtifactForHash(ContentHash hash)
        {
            ContentId contentId;
            if (m_allCacheContentHashes.TryGetItem(new ContentIdSetItem(hash, this), out contentId))
            {
                var file = contentId.GetFile(this);
                if (IsFileMaterialized(file) || m_host.CanMaterializeFile(file))
                {
                    return file;
                }
            }

            return FileArtifact.Invalid;
        }

        private void LogOutputOrigin(OperationContext operationContext, string pipDescription, string path, in FileMaterializationInfo info, PipOutputOrigin origin)
        {
            string hashHex = info.Hash.ToHex();
            string reparsePointInfo = info.ReparsePointInfo.ToString();

            switch (origin)
            {
                case PipOutputOrigin.Produced:
                    Logger.Log.SchedulePipOutputProduced(operationContext, pipDescription, path, hashHex, reparsePointInfo);
                    break;

                case PipOutputOrigin.UpToDate:
                    Logger.Log.SchedulePipOutputUpToDate(operationContext, pipDescription, path, hashHex, reparsePointInfo);
                    break;

                case PipOutputOrigin.NotMaterialized:
                    Logger.Log.SchedulePipOutputNotMaterialized(operationContext, pipDescription, path, hashHex, reparsePointInfo);
                    break;

                default:
                    Contract.Assert(origin == PipOutputOrigin.DeployedFromCache, "Unhandled PipOutputOrigin");
                    Logger.Log.SchedulePipOutputDeployedFromCache(operationContext, pipDescription, path, hashHex, reparsePointInfo);
                    break;
            }

            UpdateOutputContentStats(origin);
        }

        private static void AssertFileNamesMatch(PipExecutionContext context, FileArtifact fileArtifact, in FileMaterializationInfo fileMaterializationInfo)
        {
            Contract.Requires(fileArtifact.IsValid);

            if (!fileMaterializationInfo.FileName.IsValid)
            {
                return;
            }

            PathAtom fileArtifactFileName = fileArtifact.Path.GetName(context.PathTable);
            if (!fileMaterializationInfo.FileName.CaseInsensitiveEquals(context.StringTable, fileArtifactFileName))
            {
                string fileArtifactPathString = fileArtifact.Path.ToString(context.PathTable);
                string fileMaterializationFileNameString = fileMaterializationInfo.FileName.ToString(context.StringTable);
                Contract.Assert(
                    false,
                    I($"File name should only differ by casing. File artifact's full path: '{fileArtifactPathString}'; file artifact's file name: '{fileArtifactFileName.ToString(context.StringTable)}'; materialization info file name: '{fileMaterializationFileNameString}'"));
            }
        }

        /// <summary>
        /// Reports schedule stats that are relevant at the completion of a build.
        /// </summary>
        public void LogStats(LoggingContext loggingContext)
        {
            Logger.Log.StorageCacheContentHitSources(loggingContext, m_cacheContentSource);

            Dictionary<string, long> statistics = new Dictionary<string, long> { { Statistics.TotalCacheSizeNeeded, m_stats.TotalCacheSizeNeeded } };

            Logger.Log.CacheTransferStats(
                loggingContext,
                tryBringContentToLocalCacheCounts: Volatile.Read(ref m_stats.TryBringContentToLocalCache),
                artifactsBroughtToLocalCacheCounts: Volatile.Read(ref m_stats.ArtifactsBroughtToLocalCache),
                totalSizeArtifactsBroughtToLocalCache: ((double)Volatile.Read(ref m_stats.TotalSizeArtifactsBroughtToLocalCache)) / (1024 * 1024));

            Logger.Log.SourceFileHashingStats(
                loggingContext,
                sourceFilesHashed: Volatile.Read(ref m_stats.SourceFilesHashed),
                sourceFilesUnchanged: Volatile.Read(ref m_stats.SourceFilesUnchanged),
                sourceFilesUntracked: Volatile.Read(ref m_stats.SourceFilesUntracked),
                sourceFilesAbsent: Volatile.Read(ref m_stats.SourceFilesAbsent));

            Logger.Log.OutputFileHashingStats(
                loggingContext,
                outputFilesHashed: Volatile.Read(ref m_stats.OutputFilesHashed),
                outputFilesUnchanged: Volatile.Read(ref m_stats.OutputFilesUnchanged));

            statistics.Add(Statistics.OutputFilesChanged, m_stats.OutputFilesHashed);
            statistics.Add(Statistics.OutputFilesUnchanged, m_stats.OutputFilesUnchanged);

            statistics.Add(Statistics.SourceFilesChanged, m_stats.SourceFilesHashed);
            statistics.Add(Statistics.SourceFilesUnchanged, m_stats.SourceFilesUnchanged);
            statistics.Add(Statistics.SourceFilesUntracked, m_stats.SourceFilesUntracked);
            statistics.Add(Statistics.SourceFilesAbsent, m_stats.SourceFilesAbsent);

            Logger.Log.OutputFileStats(
                loggingContext,
                outputFilesNewlyCreated: Volatile.Read(ref m_stats.OutputsProduced),
                outputFilesDeployed: Volatile.Read(ref m_stats.OutputsDeployed),
                outputFilesUpToDate: Volatile.Read(ref m_stats.OutputsUpToDate));

            statistics.Add(Statistics.OutputFilesProduced, m_stats.OutputsProduced);
            statistics.Add(Statistics.OutputFilesCopiedFromCache, m_stats.OutputsDeployed);
            statistics.Add(Statistics.OutputFilesUpToDate, m_stats.OutputsUpToDate);

            int numDirectoryArtifacts = m_sealContents.Count;
            long numFileArtifacts = 0;
            foreach (var kvp in m_sealContents)
            {
                numFileArtifacts += kvp.Value.Length;
            }

            statistics.Add("FileContentManager_SealContents_NumDirectoryArtifacts", numDirectoryArtifacts);
            statistics.Add("FileContentManager_SealContents_NumFileArtifacts", numFileArtifacts);

            BuildXL.Tracing.Logger.Log.BulkStatistic(loggingContext, statistics);
        }

        private Task<PipOutputOrigin> ToTask(PipOutputOrigin origin)
        {
            return m_originTasks[(int)origin];
        }

        /// <summary>
        /// A content id representing an index into <see cref="m_fileArtifactContentHashes"/> referring
        /// to a file and hash
        /// </summary>
        private readonly struct ContentId
        {
            public static readonly ContentId Invalid = new ContentId(-1);

            public bool IsValid => FileArtifactContentHashesIndex >= 0;

            public readonly int FileArtifactContentHashesIndex;

            public ContentId(int fileArtifactContentHashesIndex)
            {
                FileArtifactContentHashesIndex = fileArtifactContentHashesIndex;
            }

            private KeyValuePair<FileArtifact, FileMaterializationInfo> GetEntry(FileContentManager manager)
            {
                Contract.Assert(FileArtifactContentHashesIndex >= 0);
                return manager
                    .m_fileArtifactContentHashes
                    .BackingSet[FileArtifactContentHashesIndex];
            }

            public ContentHash GetHash(FileContentManager manager)
            {
                FileMaterializationInfo info = GetEntry(manager).Value;
                return info.Hash;
            }

            public FileArtifact GetFile(FileContentManager manager)
            {
                return GetEntry(manager).Key;
            }
        }

        /// <summary>
        /// Wrapper for adding a content id (index into <see cref="m_fileArtifactContentHashes"/>) to a concurrent
        /// big set keyed by hash and also for looking up content id by hash.
        /// </summary>
        private readonly struct ContentIdSetItem : IPendingSetItem<ContentId>
        {
            private readonly ContentId m_contentId;
            private readonly FileContentManager m_manager;
            private readonly ContentHash m_hash;

            public ContentIdSetItem(ContentId contentId, ContentHash hash, FileContentManager manager)
            {
                m_contentId = contentId;
                m_manager = manager;
                m_hash = hash;
            }

            public ContentIdSetItem(ContentHash hash, FileContentManager manager)
            {
                m_contentId = ContentId.Invalid;
                m_manager = manager;
                m_hash = hash;
            }

            public int HashCode => m_hash.GetHashCode();

            public ContentId CreateOrUpdateItem(ContentId oldItem, bool hasOldItem, out bool remove)
            {
                remove = false;
                Contract.Assert(m_contentId.IsValid);
                return m_contentId;
            }

            public bool Equals(ContentId other)
            {
                return m_hash.Equals(other.GetHash(m_manager));
            }
        }

        private struct MaterializationFile
        {
            public readonly FileArtifact Artifact;
            public readonly FileMaterializationInfo MaterializationInfo;
            public readonly bool AllowReadOnly;
            public readonly TaskSourceSlim<PipOutputOrigin> MaterializationCompletion;
            public Task PriorArtifactVersionCompletion;
            public readonly AbsolutePath SymlinkTarget;

            public bool CreateSymlink => SymlinkTarget.IsValid;

            public MaterializationFile(
                FileArtifact artifact,
                FileMaterializationInfo materializationInfo,
                bool allowReadOnly,
                TaskSourceSlim<PipOutputOrigin> materializationCompletion,
                Task priorArtifactVersionCompletion,
                AbsolutePath symlinkTarget)
            {
                Artifact = artifact;
                MaterializationInfo = materializationInfo;
                AllowReadOnly = allowReadOnly;
                MaterializationCompletion = materializationCompletion;
                PriorArtifactVersionCompletion = priorArtifactVersionCompletion;
                SymlinkTarget = symlinkTarget;
            }
        }

        /// <summary>
        /// Pooled state used by hashing and materialization operations
        /// </summary>
        private sealed class PipArtifactsState : IDisposable
        {
            private readonly FileContentManager m_manager;

            public PipArtifactsState(FileContentManager manager)
            {
                m_manager = manager;
            }

            /// <summary>
            /// The pip info for the materialization operation
            /// </summary>
            public PipInfo PipInfo { get; set; }

            /// <summary>
            /// Indicates whether content is materialized for verification purposes only
            /// </summary>
            public bool VerifyMaterializationOnly { get; set; }

            /// <summary>
            /// Indicates whether the operation is materializing outputs
            /// </summary>
            public bool MaterializingOutputs { get; set; }

            /// <summary>
            /// Indicates pip is the declared producer for the materialized files
            /// </summary>
            public bool IsDeclaredProducer { get; set; }

            /// <summary>
            /// The overall output origin result for materializing outputs
            /// </summary>
            public PipOutputOrigin OverallOutputOrigin { get; private set; } = PipOutputOrigin.NotMaterialized;

            /// <summary>
            /// The materialized file paths in all materialized directories
            /// </summary>
            public readonly HashSet<AbsolutePath> MaterializedDirectoryContents = new HashSet<AbsolutePath>();

            /// <summary>
            /// All the artifacts to process
            /// </summary>
            public readonly HashSet<FileOrDirectoryArtifact> PipArtifacts = new HashSet<FileOrDirectoryArtifact>();

            /// <summary>
            /// The completion results for directory deletions
            /// </summary>
            public readonly List<(DirectoryArtifact, TaskSourceSlim<bool>)> DirectoryDeletionCompletions =
                new List<(DirectoryArtifact, TaskSourceSlim<bool>)>();

            /// <summary>
            /// Required directory deletions initiated by other pips which must be awaited
            /// </summary>
            public readonly List<Task<bool>> PendingDirectoryDeletions = new List<Task<bool>>();

            /// <summary>
            /// The set of files to materialize
            /// </summary>
            public readonly List<MaterializationFile> MaterializationFiles = new List<MaterializationFile>();

            /// <summary>
            /// The paths and content hashes for files in <see cref="MaterializationFiles"/>
            /// </summary>
            private readonly List<(FileArtifact, ContentHash, int)> m_filesAndContentHashes =
                new List<(FileArtifact, ContentHash, int)>();

            /// <summary>
            /// The tasks for hashing files
            /// </summary>
            public readonly List<Task<FileMaterializationInfo?>> HashTasks = new List<Task<FileMaterializationInfo?>>();

            /// <summary>
            /// Materialization tasks initiated by other pips which must be awaited
            /// </summary>
            public readonly List<(FileArtifact fileArtifact, Task<PipOutputOrigin> tasks)> PendingPlacementTasks =
                new List<(FileArtifact, Task<PipOutputOrigin>)>();

            /// <summary>
            /// Materialization tasks initiated by the current pip
            /// </summary>
            public readonly List<Task> PlacementTasks = new List<Task>();

            /// <summary>
            /// Files which failed to materialize
            /// </summary>
            private readonly List<(FileArtifact, ContentHash)> m_failedFiles = new List<(FileArtifact, ContentHash)>();

            /// <summary>
            /// Directories which failed to materialize
            /// </summary>
            private readonly List<DirectoryArtifact> m_failedDirectories = new List<DirectoryArtifact>();

            /// <nodoc />
            public Failure InnerFailure = null;

            /// <summary>
            /// Get the content hashes for <see cref="MaterializationFiles"/>
            /// </summary>
            public IReadOnlyList<(FileArtifact, ContentHash, int)> GetCacheMaterializationContentHashes()
            {
                m_filesAndContentHashes.Clear();
                for (int i = 0; i < MaterializationFiles.Count; i++)
                {
                    var file = MaterializationFiles[i];
                    if (!(file.CreateSymlink || file.MaterializationInfo.IsReparsePointActionable) && !m_manager.m_host.CanMaterializeFile(file.Artifact))
                    {
                        m_filesAndContentHashes.Add((file.Artifact, file.MaterializationInfo.Hash, i));
                    }
                }

                return m_filesAndContentHashes;
            }

            public void Dispose()
            {
                MaterializingOutputs = false;
                IsDeclaredProducer = false;
                VerifyMaterializationOnly = false;
                PipInfo = null;

                OverallOutputOrigin = PipOutputOrigin.NotMaterialized;
                MaterializedDirectoryContents.Clear();
                PipArtifacts.Clear();
                DirectoryDeletionCompletions.Clear();
                PendingDirectoryDeletions.Clear();

                MaterializationFiles.Clear();
                m_filesAndContentHashes.Clear();

                HashTasks.Clear();
                PendingPlacementTasks.Clear();
                PlacementTasks.Clear();
                m_failedFiles.Clear();
                m_failedDirectories.Clear();

                InnerFailure = null;

                m_manager.m_statePool.Enqueue(this);
            }

            /// <summary>
            /// Gets the materialization failure. NOTE: This should only be called when the materialization result is not successful
            /// </summary>
            public ArtifactMaterializationFailure GetFailure()
            {
                Contract.Assert(m_failedFiles.Count != 0 || m_failedDirectories.Count != 0);
                return new ArtifactMaterializationFailure(m_failedFiles.ToReadOnlyArray(), m_failedDirectories.ToReadOnlyArray(), m_manager.m_host.Context.PathTable, InnerFailure);
            }

            /// <summary>
            /// Set the materialization result for the file to failure
            /// </summary>
            public void SetMaterializationFailure(int fileIndex)
            {
                var failedFile = MaterializationFiles[fileIndex];
                AddFailedFile(failedFile.Artifact, failedFile.MaterializationInfo.FileContentInfo.Hash);

                SetMaterializationResult(fileIndex, success: false);
            }

            /// <summary>
            /// Adds a failed file
            /// </summary>
            public void AddFailedFile(FileArtifact file, ContentHash contentHash)
            {
                lock (m_failedFiles)
                {
                    m_failedFiles.Add((file, contentHash));
                }
            }

            /// <summary>
            /// Adds a failed directory
            /// </summary>
            public void AddFailedDirectory(DirectoryArtifact directory)
            {
                lock (m_failedDirectories)
                {
                    m_failedDirectories.Add(directory);
                }
            }

            /// <summary>
            /// Ensures that the materialization of the specified file is not started until after the given
            /// completion of an artifact dependency (i.e. host materialized files).
            /// </summary>
            public void SetDependencyArtifactCompletion(int fileIndex, Task dependencyArtifactCompletion)
            {
                var materializationFile = MaterializationFiles[fileIndex];
                var priorArtifactCompletion = materializationFile.PriorArtifactVersionCompletion;
                if (priorArtifactCompletion != null && !priorArtifactCompletion.IsCompleted)
                {
                    // Wait for prior artifact and the dependency artifact before attempting materialization
                    priorArtifactCompletion = Task.WhenAll(dependencyArtifactCompletion, priorArtifactCompletion);
                }
                else
                {
                    // No outstanding prior artifact, just wait for the dependency artifact before attempting materialization
                    priorArtifactCompletion = dependencyArtifactCompletion;
                }

                materializationFile.PriorArtifactVersionCompletion = priorArtifactCompletion;
                MaterializationFiles[fileIndex] = materializationFile;
            }

            /// <summary>
            /// Set the materialization result for the file to success with the given <see cref="PipOutputOrigin"/>
            /// </summary>
            public void SetMaterializationSuccess(int fileIndex, ContentMaterializationOrigin origin, OperationContext operationContext)
            {
                PipOutputOrigin result = origin.ToPipOutputOrigin();

                if (!VerifyMaterializationOnly)
                {
                    MaterializationFile materializationFile = MaterializationFiles[fileIndex];
                    var file = materializationFile.Artifact;
                    if (file.IsOutputFile &&
                        (IsDeclaredProducer || m_manager.TryGetDeclaredProducerId(file).IsValid))
                    {
                        var producer = IsDeclaredProducer ? PipInfo.UnderlyingPip : m_manager.GetDeclaredProducer(file);

                        result = GetPipOutputOrigin(origin, producer);
                        var producerDescription = IsDeclaredProducer
                            ? PipInfo.Description
                            : producer.GetDescription(m_manager.Context);

                        m_manager.LogOutputOrigin(
                            operationContext,
                            producerDescription,
                            file.Path.ToString(m_manager.Context.PathTable),
                            materializationFile.MaterializationInfo,
                            result);

                        // Notify the host that output content for a pip was materialized
                        // NOTE: This is specifically for use when materializing outputs
                        // to preserve legacy behavior for tests.
                        m_manager.m_host.ReportContent(file, materializationFile.MaterializationInfo, result);
                    }
                }

                SetMaterializationResult(fileIndex, success: true, result: result);
            }

            private void SetMaterializationResult(int materializationFileIndex, bool success, PipOutputOrigin result = PipOutputOrigin.NotMaterialized)
            {
                Contract.Requires(result != PipOutputOrigin.NotMaterialized || !success, "Successfully materialization cannot have NotMaterialized result");
                MaterializationFile file = MaterializationFiles[materializationFileIndex];
                file.MaterializationCompletion.SetResult(result);

                if (!VerifyMaterializationOnly)
                {
                    // Normalize to task results to shared cached pip origin tasks to save memory
                    m_manager.m_materializationTasks[file.Artifact] = m_manager.ToTask(result);
                }

                m_manager.m_currentlyMaterializingFilesByPath.CompareRemove(file.Artifact.Path, file.Artifact);
                MergeResult(result);
            }

            /// <summary>
            /// Combines the individual result with <see cref="OverallOutputOrigin"/>
            /// </summary>
            public void MergeResult(PipOutputOrigin result)
            {
                lock (this)
                {
                    // Merged result is result with highest precedence
                    OverallOutputOrigin =
                        GetMergePrecedence(result) > GetMergePrecedence(OverallOutputOrigin)
                            ? result
                            : OverallOutputOrigin;
                }
            }

            private static int GetMergePrecedence(PipOutputOrigin origin)
            {
                switch (origin)
                {
                    case PipOutputOrigin.Produced:
                        // Takes precedence over all other results. Producing any content
                        // means the pip result is produced
                        return 3;
                    case PipOutputOrigin.DeployedFromCache:
                        // Pip result is deployed from cache if its outputs are up to date or deployed
                        // deployed from cache
                        return 2;
                    case PipOutputOrigin.UpToDate:
                        // Pip result is only up to date if all its outputs are up to date
                        return 1;
                    case PipOutputOrigin.NotMaterialized:
                        return 0;
                    default:
                        throw Contract.AssertFailure(I($"Unexpected PipOutputOrigin: {origin}"));
                }
            }

            /// <summary>
            /// Adds a file to be materialized
            /// </summary>
            public void AddMaterializationFile(
                FileArtifact fileToMaterialize,
                bool allowReadOnly,
                in FileMaterializationInfo materializationInfo,
                TaskSourceSlim<PipOutputOrigin> materializationCompletion,
                AbsolutePath symlinkTarget)
            {
                Contract.Assert(PipInfo != null, "PipInfo must be set to materialize files");

                var result = m_manager.m_currentlyMaterializingFilesByPath.AddOrUpdate(
                    fileToMaterialize.Path,
                    fileToMaterialize,
                    (path, file) => file,
                    (path, file, oldFile) => file.RewriteCount > oldFile.RewriteCount
                        ? file
                        : oldFile);

                Task priorArtifactCompletion = Unit.VoidTask;

                // Only materialize the file if it is the latest version
                bool isLatestVersion = result.Item.Value == fileToMaterialize;

                if (isLatestVersion)
                {
                    if (result.IsFound && result.OldItem.Value != fileToMaterialize)
                    {
                        priorArtifactCompletion = m_manager.m_materializationTasks[result.OldItem.Value];
                    }

                    // Populate collections with corresponding information for files
                    MaterializationFiles.Add(new MaterializationFile(
                        fileToMaterialize,
                        materializationInfo,
                        allowReadOnly,
                        materializationCompletion,
                        priorArtifactCompletion,
                        symlinkTarget));
                }
                else
                {
                    // File is not materialized because it is not the latest file version
                    materializationCompletion.SetResult(PipOutputOrigin.NotMaterialized);
                }
            }

            /// <summary>
            /// Remove completed materializations
            /// </summary>
            public void RemoveCompletedMaterializations()
            {
                MaterializationFiles.RemoveAll(file => file.MaterializationCompletion.Task.IsCompleted);
            }
        }
    }
}
