// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.FileSystem;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tracing;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler.Fingerprints
{
    using DirectoryMemberEntry = ValueTuple<AbsolutePath, string>;

    /// <summary>
    /// An <see cref="ObservedInputProcessor"/> is a generic means from promoting some 'observation' (such as file
    /// accesses traced from detours) into a set of <see cref="ObservedInput"/>s (in a <see cref="ObservedInputProcessingResult"/>).
    /// The processor access-checks the observations (is the process allowed to access the particular file?) and also provides a hook
    /// for comparing the observation with a proposed <see cref="ObservedInput"/>.
    /// From the produced <see cref="ObservedInputProcessingResult"/>, one can derive a <see cref="BuildXL.Engine.Cache.Fingerprints.StrongContentFingerprint"/>
    /// and <see cref="ObservedPathSet"/> pair, which are the basis for the second phase of 'two-phase' fingerprint lookup.
    ///
    /// Processing is parameterized on a 'target' type, which must be a struct implementing <see cref="IObservedInputProcessingTarget{TObservation}"/>.
    /// A target must be able to project its observation type into a path (since common processing operates on the observed path).
    /// In response to access-check failures etc. and <see cref="ObservedInput"/> proposals, a target may choose how to respond; for example,
    /// a target responsible for processing observed file-accesses (from a real process) may log warnings and blame a tool, whereas a target
    /// responsible for cache-lookup may fail quietly (maybe logging miss-reason quietly).
    ///
    /// Of note is that a variety of targets allow observed input processing to be in common among various fingerprinting approaches, and between
    /// cache-lookup vs. runtime-enforcement:
    /// * Single-phase fingerprinting:
    ///   - Enforcement: / Storage: A just-executed process may generate warnings or errors if accesses are disallowed.
    ///                  The resulting observed inputs are stored directly in a cache descriptor.
    ///   - Lookup: The observed inputs in a cache descriptor are (re)processed to see if they are still permissible and if they agree on content.
    /// * Two-phase fingerprinting:
    ///   - Enforcement / Storage: Like single-phase, but the result is used to generate a path-set and strong fingerprint pair to store with.
    ///   - Lookup: Like single-phase, but a prior <see cref="ObservedPathSet"/> is used to generate a strong fingerprint to look up.
    /// </summary>
    /// <remarks>
    /// Note that the target type parameter is constrained to <see cref="IObservedInputProcessingTarget{TObservation}"/> and also
    /// <c>struct</c>. Since the JIT specializes for any value-type instantiation, this means we get static-dispatch, inlining, dead-code
    /// elimination, etc. rather than interface calls or (worse still!) calls to lambda closures.
    /// </remarks>
    public static class ObservedInputProcessor
    {
        private static readonly Task<FileContentInfo?> s_nullFileContentInfoTask = Task.FromResult<FileContentInfo?>(null);

        /// <summary>
        /// Processes the paths in an <see cref="ObservedPathSet"/>. In two-phase lookup, this is used to derive a strong fingerprint
        /// corresponding to the path-set (lookup side).
        /// </summary>
        public static async Task<ObservedInputProcessingResult> ProcessPriorPathSetAsync<TTarget>(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            TTarget target,
            CacheablePipInfo pip,
            ObservedPathSet pathSet)
            where TTarget : struct, IObservedInputProcessingTarget<ObservedPathEntry>
        {
            using (var environmentAdapter = new ObservedInputProcessingEnvironmentAdapter(environment, state))
            {
                return await ProcessInternalAsync(
                    operationContext,
                    environmentAdapter,
                    target,
                    pip,
                    pathSet.Paths.BaseArray,
                    pathSet.ObservedAccessedFileNames,
                    isCacheLookup: true);
            }
        }

        /// <summary>
        /// Processes observed file accesses. This is used in both two-phase and single-phase fingerprinting, since both
        /// run processes the same way. Single-phase will store the resulting <see cref="ObservedInput"/>s,
        /// whereas two-phase fingerprinting will derive an <see cref="ObservedPathSet"/> and <see cref="BuildXL.Engine.Cache.Fingerprints.StrongContentFingerprint"/> pair.
        /// </summary>
        public static async Task<ObservedInputProcessingResult> ProcessNewObservationsAsync<TTarget>(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state,
            TTarget target,
            CacheablePipInfo pip,
            ReadOnlyArray<ObservedFileAccess> accesses,
            bool trackFileChanges = true)
            where TTarget : struct, IObservedInputProcessingTarget<ObservedFileAccess>
        {
            using (var environmentAdapter = new ObservedInputProcessingEnvironmentAdapter(environment, state))
            {
                return await ProcessInternalAsync(
                    operationContext,
                    environmentAdapter,
                    target,
                    pip,
                    accesses,
                    default(SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer>),
                    isCacheLookup: false,
                    trackFileChanges: trackFileChanges);
            }
        }
        
        /// <summary>
        /// Actual processing logic for any <see cref="IObservedInputProcessingEnvironment"/> and any <see cref="IObservedInputProcessingTarget{TObservation}"/>.
        /// This method is left internal only for testing (tests may provide a fully mocked environment, preventing observation of the real filesystem).
        /// This implementation requires that the <paramref name="observations"/> array is sorted by each entry's expanded
        /// path. Observations for duplicate paths are permitted (the observations themselves may be distinct due to other members).
        /// </summary>
        internal static async Task<ObservedInputProcessingResult> ProcessInternalAsync<TTarget, TEnv, TObservation>(
            OperationContext operationContext,
            TEnv environment,
            TTarget target,
            CacheablePipInfo pip,
            ReadOnlyArray<TObservation> observations,
            SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer> observedAccessedFileNames,
            bool isCacheLookup,
            bool trackFileChanges = true)
            where TTarget : struct, IObservedInputProcessingTarget<TObservation>
            where TEnv : IObservedInputProcessingEnvironment
        {
            Contract.Requires(!isCacheLookup ^ observedAccessedFileNames.IsValid);
            using (operationContext.StartOperation(PipExecutorCounter.ObservedInputProcessorProcessInternalDuration))
            using (var processingState = ObservedInputProcessingState.GetInstance())
            {
                var pathTable = environment.Context.PathTable;
                PathTable.ExpandedAbsolutePathComparer pathComparer = pathTable.ExpandedPathComparer;
                ReadOnlyArray<DirectoryArtifact> directoryDependencies = pip.DirectoryDependencies;

                int numAbsentPathsEliminated = 0;

                var sourceDirectoriesAllDirectories = processingState.SourceDirectoriesAllDirectories;
                var sourceDirectoriesTopDirectoryOnly = processingState.SourceDirectoriesTopDirectoryOnly;
                var dynamicallyObservedFiles = processingState.DynamicallyObservedFiles;
                var allowedUndeclaredSourceReads = processingState.AllowedUndeclaredReads;
                var absentPathProbesUnderNonDependenceOutputDirectories = processingState.AbsentPathProbesUnderNonDependenceOutputDirectories;
                var directoryDependencyContentsFilePaths = processingState.DirectoryDependencyContentsFilePaths;
                var enumeratedDirectories = processingState.EnumeratedDirectories;
                var searchPaths = processingState.SearchPaths;

                var bitSetSize = BitSet.RoundToValidBitCount(observations.Length);
                var observationsUnderSourceSealDirectories = new BitSet(bitSetSize);
                observationsUnderSourceSealDirectories.SetLength(bitSetSize);
                var outOfSourceSealObservations = new BitSet(bitSetSize);
                outOfSourceSealObservations.SetLength(bitSetSize);
                HashSet<AbsolutePath> possiblyBadAccesses;
                HashSet<HierarchicalNameId> pipFileSystemViewPathIds = null;

                var allowUndeclaredSourceReads = pip.UnderlyingPip.ProcessAllowsUndeclaredSourceReads;

                using (operationContext.StartOperation(PipExecutorCounter.ObservedInputProcessorPreProcessDuration))
                {
                    using (operationContext.StartOperation(PipExecutorCounter.ObservedInputProcessorPreProcessListDirectoriesDuration))
                    {
                        for (int i = 0; i < directoryDependencies.Length; i++)
                        {
                            var directoryDependency = directoryDependencies[i];
                            var listDirectoryContents = environment.ListSealedDirectoryContents(directoryDependency);

                            if (!listDirectoryContents.BaseArray.IsValid)
                            {
                                // TODO: Debugging for a one-off crash.
                                Contract.Assume(listDirectoryContents.BaseArray.IsValid, I($"Environment failed to retrieve directory contents for directory:'{directoryDependency.Path.ToString(pathTable)}'. Directory dependency IsSharedOpaque:{directoryDependency.IsSharedOpaque}. PartialSealId:{directoryDependency.PartialSealId}. Base Array was invalid but SortedReadOnlyArray was valid."));
                            }

                            if (!listDirectoryContents.IsValid)
                            {
                                // TODO: Debugging for a one-off crash that we aren't sure still exists.
                                Contract.Assume(listDirectoryContents.IsValid, I($"Environment failed to retrieve directory contents for directory:'{directoryDependency.Path.ToString(pathTable)}'. Directory dependency IsSharedOpaque:{directoryDependency.IsSharedOpaque}. PartialSealId:{directoryDependency.PartialSealId} "));
                            }

                            directoryDependencyContentsFilePaths.UnionWith(listDirectoryContents.Select(f => f.Path));
                            if (environment.IsSourceSealedDirectory(directoryDependency, out var allDirectories, out var patterns))
                            {
                                if (allDirectories)
                                {
                                    sourceDirectoriesAllDirectories.Add(new SourceSealWithPatterns(directoryDependency.Path, patterns));
                                }
                                else
                                {
                                    sourceDirectoriesTopDirectoryOnly.Add(new SourceSealWithPatterns(directoryDependency.Path, patterns));
                                }
                            }
                        }
                    }

                    // We have observed path accesses, but need to match them up to file artifacts.
                    // We can do this with OrdinalPathOnlyFileArtifactComparer and arbitrary write counts
                    // TODO: This is a very silly conversion. Should re-implement the needed comparer things in a new IBinaryComparer<TLeft, TRight>
                    //       and make a FileArtifact - AbsolutePath comparer.
                    var observationArtifacts = processingState.ObservationArtifacts;

                    using (operationContext.StartOperation(PipExecutorCounter.ObservedInputProcessorPreProcessValidateSealSourceDuration))
                    {
                        for (int i = 0; i < observations.Length; i++)
                        {
                            AbsolutePath path = target.GetPathOfObservation(observations[i]);
                            if (directoryDependencyContentsFilePaths.Contains(path))
                            {
                                // Path is explicitly mentioned in a seal directory dependency's contents
                                // no need to check if it is under a source seal directory
                                continue;
                            }

                            // Check to see if the observation is under a topDirectoryOnly Sealed Source Directories
                            bool underSealedSource = false;
                            for (int j = 0; j < sourceDirectoriesTopDirectoryOnly.Count && !underSealedSource; j++)
                            {
                                var sourceSealWithPatterns = sourceDirectoriesTopDirectoryOnly[j];
                                if (sourceSealWithPatterns.Contains(pathTable, path))
                                {
                                    underSealedSource = true;
                                    observationsUnderSourceSealDirectories.Add(i);
                                }
                                else if (path == sourceSealWithPatterns.Path)
                                {
                                    // Consider the sealed directory itself as a dynamic observation as this likely
                                    // is an enumeration we want to capture for caching
                                    observationsUnderSourceSealDirectories.Add(i);
                                }
                            }

                            // Check to see if the observation is under one of the AllDirectory Sealed Source Directories
                            for (int j = 0; j < sourceDirectoriesAllDirectories.Count && !underSealedSource; j++)
                            {
                                var sourceSealWithPatterns = sourceDirectoriesAllDirectories[j];
                                if (sourceSealWithPatterns.Contains(pathTable, path, isTopDirectoryOnly: false))
                                {
                                    // Note the directories themselves are never part of the seal.
                                    underSealedSource = true;
                                    observationsUnderSourceSealDirectories.Add(i);
                                }
                                else if (path == sourceSealWithPatterns.Path)
                                {
                                    // Consider the sealed directory itself as a dynamic observation as this likely
                                    // is an enumeration we want to capture for caching
                                    observationsUnderSourceSealDirectories.Add(i);
                                }
                            }
                            
                            if (!underSealedSource)
                            {
                                observationArtifacts.Add(path);
                            }
                        }
                    }

                    // Note that we validated the sort order of 'observations' above w.r.t expanded order, but need
                    // a fake artifacts sorted ordinally.
                    // TODO: We can remove this step if ListSealedDirectoryContents is changed to returned expansion-sorted results.
                    possiblyBadAccesses = observationArtifacts;
                }

                // Processed results.
                var observationInfos = new ObservationInfo[observations.Length];
                ObservedInput[] observedInputs = new ObservedInput[observations.Length];
                ObservedInputProcessingStatus status = ObservedInputProcessingStatus.Success;
                int valid = 0;
                int invalid = 0;

                bool minimalGraphUsed = false;
                // Do the processing in 2 passes.
                // First pass: obtain paths and start all hashing tasks.
                // Second pass: Do the actual processing.
                // Having 2 passes allows us to avoid Parallel.ForEach with all required locking.
                using (operationContext.StartOperation(PipExecutorCounter.ObservedInputProcessorPass1InitializeObservationInfosDuration))
                {
                    for (int i = 0; i < observations.Length; i++)
                    {
                        observationInfos[i] = GetObservationInfo(environment, target, observations[i], allowUndeclaredSourceReads);
                    }
                }

                ObservedInputType[] observationTypes = new ObservedInputType[observations.Length];

                // Observations which fail access checks are suppressed (i.e. they do not contribute to
                // the fingerprint).
                bool[] isUnsuppressedObservation = new bool[observations.Length];

                int numAbsentPathProbes = 0, numFileContentReads = 0, numDirectoryEnumerations = 0;
                int numExistingDirectoryProbes = 0, numExistingFileProbes = 0;

                using (operationContext.StartOperation(PipExecutorCounter.ObservedInputProcessorPass2ProcessObservationInfosDuration))
                {
                    // Start the second pass
                    for (int i = 0; i < observations.Length; i++)
                    {
                        TObservation observation = observations[i];
                        ObservationInfo observationInfo = observationInfos[i];
                        AbsolutePath path = observationInfo.Path;
                        ObservationFlags observationFlags = observationInfo.ObservationFlags;
                        FileArtifact fakeArtifact = FileArtifact.CreateSourceFile(path);
                        FileContentInfo? pathContentInfo;

                        using (operationContext.StartOperation(
                            PipExecutorCounter.ObservedInputProcessorTryQuerySealedInputContentDuration,
                            fakeArtifact))
                        {
                            pathContentInfo = await observationInfos[i].FileContentInfoTask;
                        }

                        // TODO: Don't use UntrackedFile for this...
                        if (pathContentInfo.HasValue && !pathContentInfo.Value.HasKnownLength &&
                            pathContentInfo.Value.Hash == WellKnownContentHashes.UntrackedFile)
                        {
                            // This is a HashSourceFile failure.
                            var mountInfo = environment.PathExpander.GetSemanticPathInfo(path);
                            Logger.Log.AbortObservedInputProcessorBecauseFileUntracked(
                                operationContext,
                                pip.Description,
                                path.ToString(pathTable),
                                mountInfo.RootName.IsValid ?
                                    mountInfo.RootName.ToString(environment.Context.StringTable) :
                                    "N/A");

                            status = CombineObservedInputProcessingStatus(status, ObservedInputProcessingStatus.Aborted);
                            invalid++;
                            continue;
                        }

                        bool wasPossiblyBad = possiblyBadAccesses.Contains(FileArtifact.CreateSourceFile(path));

                        if (!allowUndeclaredSourceReads)
                        {
                            // We do not hash the files that have been probed, so we skip the following validation for the file probes.
                            if (!pathContentInfo.HasValue && !wasPossiblyBad && !observationsUnderSourceSealDirectories.Contains(i) && observationFlags.IsHashingRequired())
                            {
                                Contract.Assume(
                                    false,
                                    "Observation is either a file or a directory found to be under a seal directory, although the file may not exist physically, " +
                                    "or possibly bad access (probing or reading a file that is not specified as a dependency), or possibly directory enumeration. " +
                                    GetDiagnosticsInfo(path, pip, pathTable, directoryDependencyContentsFilePaths, sourceDirectoriesTopDirectoryOnly, sourceDirectoriesAllDirectories, observationFlags));
                            }
                        }

                        // TODO: Right now we check TryQuerySealedOrUndeclaredInputContent, and then the VFS if that fails (we assume the two are in agreement!)
                        //       Consider combining responsibilities so that the VFS can additionally provide per-file
                        //       state info, such as (Existence: File, State: Sealed, Hash: 123).
                        // pathContentInfo may be set to AbsentFile for a directory if TreatDirectoryAsAbsentFileOnHashingInputContent flag is specified.
                        // We need to ensure that we recognize it is a directory so the observed input shows up as a directory enumeration
                        // We do this by examining the Existence of the pathContentInfo which will be set by the FileContentManager to see if the path represents
                        // a directory
                        ObservedInputType type;
                        if (pathContentInfo.HasValue && pathContentInfo.Value.Existence != PathExistence.ExistsAsDirectory)
                        {
                            // Path content info may be an absent file, for example, a sealed directory includes a non-existent file.
                            type = pathContentInfo.Value.Hash == WellKnownContentHashes.AbsentFile
                                ? ObservedInputType.AbsentPathProbe
                                : ObservedInputType.FileContentRead;
                        }
                        else
                        {
                            // We tried to find FileContentInfo for the accessed path, but failed. This means that the 1) path is not part of a sealed directory,
                            // but *may* be part of the pip graph (just not sealed) or 2) the path did not require hashing (e.g. a probe). How we proceed here is quite delicate:
                            // - We make decisions based on 'existence' of the path (including if it is a file or a directory).
                            // - If the path is known to the pip graph, we might not have materialized it yet.
                            // - If the path is not known to the pip graph, it might exist in a way that is visible to build processes (e.g. a file not added to a spec).
                            // Specific rules for how to determine existence are an implementation detail of the IObservedInputProcessingEnvironment defined below.
                            Possible<PathExistence> maybeType;
                            using (operationContext.StartOperation(PipExecutorCounter.ObservedInputProcessorTryProbeForExistenceDuration, fakeArtifact))
                            {
                                maybeType = environment.TryProbeAndTrackForExistence(path, pip, isReadOnly: observationsUnderSourceSealDirectories.Contains(i), trackPathExistence: trackFileChanges);
                            }

                            if (!maybeType.Succeeded)
                            {
                                Logger.Log.ScheduleFileAccessCheckProbeFailed(
                                    operationContext,
                                    pip.Description,
                                    path.ToString(pathTable),
                                    maybeType.Failure.DescribeIncludingInnerFailures());

                                ObservedInputAccessCheckFailureAction accessCheckFailureResult = target.OnAccessCheckFailure(
                                    observation,
                                    fromTopLevelDirectory: sourceDirectoriesTopDirectoryOnly.Any(a => a.Contains(pathTable, path, isTopDirectoryOnly: false)));
                                HandleFailureResult(accessCheckFailureResult, ref status, ref invalid);
                                continue;
                            }
                            else
                            {
                                type = MapPathExistenceToObservedInputType(pathTable, path, maybeType.Result, observationFlags);
                            }
                        }

                        observationTypes[i] = type;

                        if (wasPossiblyBad)
                        {
                            if (allowUndeclaredSourceReads)
                            {
                                allowedUndeclaredSourceReads.Add(path);
                            }
                            else if (type == ObservedInputType.FileContentRead || type == ObservedInputType.ExistingFileProbe)
                            {
                                ObservedInputAccessCheckFailureAction accessCheckFailureResult = target.OnAccessCheckFailure(
                                    observation,
                                    fromTopLevelDirectory: sourceDirectoriesTopDirectoryOnly.Any(a => a.Contains(pathTable, path, isTopDirectoryOnly: false)));
                                HandleFailureResult(accessCheckFailureResult, ref status, ref invalid);
                                continue;
                            }
                            else if (target.IsReportableUnexpectedAccess(path))
                            {
                                if (pipFileSystemViewPathIds == null)
                                {
                                    // Lazily populate pipFileSystemViewPathIds if there is at least one reportable unexpected access.
                                    pipFileSystemViewPathIds = processingState.AllDependencyPathIds;
                                    using (operationContext.StartOperation(OperationCounter.ObservedInputProcessorComputePipFileSystemPaths))
                                    {
                                        foreach (var p in directoryDependencyContentsFilePaths)
                                        {
                                            foreach (var pathId in pathTable.EnumerateHierarchyBottomUp(p.GetParent(pathTable).Value))
                                            {
                                                if (!pipFileSystemViewPathIds.Add(pathId))
                                                {
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }

                                if (!pipFileSystemViewPathIds.Contains(path.Value))
                                {
                                    using (operationContext.StartOperation(OperationCounter.ObservedInputProcessorReportUnexpectedAccess))
                                    {
                                        // Pip does not contain the path in its file system view (as file or directory)
                                        // Report to target but don't fail as only unexpected FileContentRead currently fails input
                                        // processing. This is a policy decision which may be changed at some point. At which point,
                                        // this code would need to be updated.

                                        target.ReportUnexpectedAccess(observation, type);
                                    }
                                }
                            }
                        }

                        isUnsuppressedObservation[i] = true;
                    }

                    DirectoryMembershipFilter searchPathFilter;
                    using (operationContext.StartOperation(PipExecutorCounter.ObservedInputProcessorComputeSearchPathsAndFilterDuration))
                    {
                        searchPathFilter = ComputeSearchPathsAndFilter(
                                                ref observedAccessedFileNames,
                                                environment.Context.PathTable,
                                                pip,
                                                target,
                                                observations,
                                                observationTypes,
                                                isUnsuppressedObservation,
                                                searchPaths);
                    }

                    AbsolutePath lastAbsentPath = AbsolutePath.Invalid;

                    // Third and final pass
                    for (int i = 0; i < observations.Length; i++)
                    {
                        if (!isUnsuppressedObservation[i])
                        {
                            continue;
                        }

                        TObservation observation = observations[i];
                        AbsolutePath path = observationInfos[i].Path;

                        // Call GetAwaiter().GetResult() since we awaited above so we know the task has completed successfully
                        FileContentInfo? pathContentInfo = observationInfos[i].FileContentInfoTask.GetAwaiter().GetResult();
                        var type = observationTypes[i];

                        if (type == ObservedInputType.FileContentRead && !pathContentInfo.HasValue)
                        {
                            var flags = observationInfos[i].ObservationFlags;
                            Contract.Assert(false, "If the access is a file content read, then the FileContentInfo cannot be null." +
                                GetDiagnosticsInfo(path, pip, pathTable, directoryDependencyContentsFilePaths, sourceDirectoriesTopDirectoryOnly, sourceDirectoriesAllDirectories, flags));
                        }

                        if (type == ObservedInputType.AbsentPathProbe)
                        {
                            // We need to iterate the observations in the order so that we can first visit the directory and then the child paths under that directory.
                            // CanIgnoreAbsentPathProbe relies on that assumption.
                            if (CanIgnoreAbsentPathProbe(environment.PathExpander, enumeratedDirectories, pathTable, path, lastAbsentPath, isCacheLookup))
                            {
                                numAbsentPathsEliminated++;
                                continue;
                            }
                        }

                        ObservedInput? maybeProposed;
                        switch (type)
                        {
                            case ObservedInputType.AbsentPathProbe:
                                maybeProposed = ObservedInput.CreateAbsentPathProbe(path);

                                // We cannot add path resulting from absent path probe into dynamicallyObservedFiles.
                                // Otherwise, the incremental scheduling cannot distinguish it from dynamic file reads.
                                // In the journal scanning of incremental scheduling state, if there is a newly present path change, but
                                // the path is treated as non-existent (possibly by consulting the pip graph file system),
                                // then we should not throw away the incremental scheduling state. If we add the path into
                                // dynamicallyObservedFiles, then the incremental scheduling state may think that the path is a result
                                // of dynamic file read, and start dirtying affected pips.
                                //
                                // TODO: Uncomment when we fully handle anti-dependency in the incremental scheduling.
                                // dynamicallyObservedAbsentPaths.Add(path);

                                // Record that an absent file probe occurred under the root of a known output directory
                                // We also exclude the probes of paths that we take a dependency on. This is done to cover the case where an upstream
                                // pip produces an absent file (e.g., it produced and deleted a file), and a consuming pip probes this file.
                                // The probe will be classified as AbsentFileProbe, and since there is a dependency between these pips, we allow the probe.
                                if (environment.IsPathUnderOutputDirectory(path) 
                                    && !directoryDependencyContentsFilePaths.Contains(path))
                                {
                                    absentPathProbesUnderNonDependenceOutputDirectories.Add(path);
                                }

                                break;
                            case ObservedInputType.FileContentRead:
                                if (pathContentInfo.Value.Hash.HashType == HashType.Unknown)
                                {
                                    throw Contract.AssertFailure($"Unknown content hash for path '{path.ToString(pathTable)}' and operation {type}");
                                }
                                maybeProposed = ObservedInput.CreateFileContentRead(path, pathContentInfo.Value.Hash);
                                dynamicallyObservedFiles.Add(path);
                                break;
                            case ObservedInputType.ExistingFileProbe:
                                maybeProposed = ObservedInput.CreateExistingFileProbe(path);
                                dynamicallyObservedFiles.Add(path);
                                break;
                            case ObservedInputType.ExistingDirectoryProbe:
                                maybeProposed = ObservedInput.CreateExistingDirectoryProbe(path);

                                // Directory probe is just like file probe.
                                dynamicallyObservedFiles.Add(path);
                                break;
                            case ObservedInputType.DirectoryEnumeration:
                                // TODO: TryQueryDirectoryFingerprint should be in agreement with the VirtualFileSystem somehow.
                                //       Right now, both independently make decisions based on path mountpoint.
                                DirectoryFingerprint? maybeDirectoryFingerprint;
                                bool isSearchPath = searchPaths.Contains(path);
                                string enumeratePatternRegex;
                                DirectoryEnumerationMode mode;

                                using (operationContext.StartOperation(
                                    PipExecutorCounter.ObservedInputProcessorTryQueryDirectoryFingerprintDuration,
                                    DirectoryArtifact.CreateWithZeroPartialSealId(path)))
                                {
                                    enumeratePatternRegex = target.GetEnumeratePatternRegex(observation);

                                    DirectoryMembershipFilter directoryFilter;

                                    if (enumeratePatternRegex == null)
                                    {
                                        // If enumeratePatternRegex is null, then isSearchPath must be true. However, for unit tests, that's not true.
                                        // That's why, I still keep the AllowAllFilter here.
                                        // TODO: Add an assertion here, Assert(isSearchPath).
                                        directoryFilter = isSearchPath ? searchPathFilter : DirectoryMembershipFilter.AllowAllFilter;
                                    }
                                    else
                                    {
                                        var enumeratePatternFilter = RegexDirectoryMembershipFilter.Create(enumeratePatternRegex);
                                        directoryFilter = isSearchPath ? enumeratePatternFilter.Union(searchPathFilter) : enumeratePatternFilter;
                                    }

                                    maybeDirectoryFingerprint = environment.TryQueryDirectoryFingerprint(
                                        path,
                                        pip,
                                        filter: directoryFilter,
                                        isReadOnlyDirectory: observationsUnderSourceSealDirectories.Contains(i),
                                        eventData: new DirectoryMembershipHashedEventData()
                                        {
                                            Directory = path,
                                            IsSearchPath = isSearchPath,
                                            EnumeratePatternRegex = enumeratePatternRegex
                                        },
                                        enumerationMode: out mode,
                                        trackPathExistence: trackFileChanges);

                                    if (mode == DirectoryEnumerationMode.MinimalGraph)
                                    {
                                        minimalGraphUsed = true;
                                    }

                                    enumeratedDirectories.Add(path, (directoryFilter, mode));
                                }

                                if (maybeDirectoryFingerprint.HasValue)
                                {
                                    if (maybeDirectoryFingerprint == DirectoryFingerprint.Zero)
                                    {
                                        // We need to normalize 'empty' directories to look 'absent' since the determination of
                                        // directory vs. absent above is based on the *full graph* + real FS, but the
                                        // 'existential' VFS (either the pip scoped FS or real FS, depending on mount).
                                        // - A directory is in the global VFS, but not the existential VFS
                                        // - A directory is in neither.
                                        // Without some workaround, those two cases would cause oscillation between the Directory and Absent types respectively.
                                        // So, we canonicalize such that a path absent according to the existential FS becomes an absent probe regardless
                                        // TODO: We accomplish this for now by treating the null fingerprint specially; but this is kind of broken since that might mean "directory exists but empty", which can genuinely occur when looking at the real FS.
                                        maybeProposed = ObservedInput.CreateAbsentPathProbe(
                                            path,
                                            isSearchPath: isSearchPath,
                                            isDirectoryPath: true,
                                            directoryEnumeration: true,
                                            enumeratePatternRegex: enumeratePatternRegex);
                                    }
                                    else
                                    {
                                        maybeProposed = ObservedInput.CreateDirectoryEnumeration(
                                            path,
                                            maybeDirectoryFingerprint.Value,
                                            isSearchPath: isSearchPath,
                                            enumeratePatternRegex: enumeratePatternRegex);
                                    }
                                }
                                else
                                {
                                    maybeProposed = null;

                                    // TODO: This shouldn't always be an error.
                                    Logger.Log.PipDirectoryMembershipFingerprintingError(
                                        operationContext,
                                        pip.Description,
                                        path.ToString(pathTable));
                                }

                                break;

                            default:
                                throw Contract.AssertFailure("Unreachable");
                        }

                        if (maybeProposed.HasValue)
                        {
                            ObservedInput proposed = maybeProposed.Value;

                            // This no longer has any function other than being a test hook;
                            target.CheckProposedObservedInput(observation, proposed);

                            observedInputs[valid++] = proposed;

                            if (!proposed.Path.IsValid)
                            {
                                Contract.Assume(proposed.Path.IsValid, "Created an ObservedInput with an invalid path in ObservedInputProcessor line 675. Type:" + proposed.Type.ToString());
                            }

                            switch (proposed.Type)
                            {
                                case ObservedInputType.AbsentPathProbe:
                                    numAbsentPathProbes++;
                                    break;
                                case ObservedInputType.DirectoryEnumeration:
                                    numDirectoryEnumerations++;
                                    break;
                                case ObservedInputType.ExistingDirectoryProbe:
                                    numExistingDirectoryProbes++;
                                    break;
                                case ObservedInputType.FileContentRead:
                                    numFileContentReads++;
                                    break;
                                case ObservedInputType.ExistingFileProbe:
                                    numExistingFileProbes++;
                                    break;
                                default:
                                    Contract.Assert(false, "Unknown ObservedInputType has been encountered: " + type);
                                    break;
                            }
                        }
                        else
                        {
                            status = CombineObservedInputProcessingStatus(status, ObservedInputProcessingStatus.Aborted);
                            invalid++;
                        }
                    }
                }

                if (minimalGraphUsed)
                {
                    environment.Counters.IncrementCounter(PipExecutorCounter.NumPipsUsingMinimalGraphFileSystem);
                }

                var dynamicallyObservedEnumerations = enumeratedDirectories.Keys.ToList();

                environment.Counters.AddToCounter(PipExecutorCounter.NumAbsentPathsEliminated, numAbsentPathsEliminated);
                environment.Counters.AddToCounter(PipExecutorCounter.AbsentPathProbes, numAbsentPathProbes);
                environment.Counters.AddToCounter(PipExecutorCounter.DirectoryEnumerations, numDirectoryEnumerations);
                environment.Counters.AddToCounter(PipExecutorCounter.ExistingDirectoryProbes, numExistingDirectoryProbes);
                environment.Counters.AddToCounter(PipExecutorCounter.FileContentReads, numFileContentReads);
                environment.Counters.AddToCounter(PipExecutorCounter.ExistingFileProbes, numExistingFileProbes);

                if (status == ObservedInputProcessingStatus.Success)
                {
                    Contract.Assume(invalid == 0);
                    Contract.Assume(valid <= observedInputs.Length);
                    Contract.Assume(observedAccessedFileNames.IsValid);

                    if (valid != observedInputs.Length)
                    {
                        // We may have valid < observedInputs.Length due to SuppressAndIgnorePath, e.g. due to monitoring whitelists.
                        Array.Resize(ref observedInputs, valid);
                    }

                    // Note that we validated the sort order of 'observations', and 'observedInputs' is in an equivalent order.
                    return ObservedInputProcessingResult.CreateForSuccess(
                        observedInputs: SortedReadOnlyArray<ObservedInput, ObservedInputExpandedPathComparer>.FromSortedArrayUnsafe(
                            ReadOnlyArray<ObservedInput>.FromWithoutCopy(observedInputs),
                            new ObservedInputExpandedPathComparer(pathComparer)),
                        observedAccessedFileNames: observedAccessedFileNames,
                        dynamicallyObservedFiles: ReadOnlyArray<AbsolutePath>.From(dynamicallyObservedFiles),
                        dynamicallyObservedEnumerations: ReadOnlyArray<AbsolutePath>.From(dynamicallyObservedEnumerations),
                        allowedUndeclaredSourceReads: allowedUndeclaredSourceReads.ToReadOnlySet(),
                        absentPathProbesUnderNonDependenceOutputDirectories: absentPathProbesUnderNonDependenceOutputDirectories.ToReadOnlySet());
                }
                else
                {
                    Contract.Assume(invalid > 0);
                    return ObservedInputProcessingResult.CreateForFailure(
                        status: status,
                        numberOfValidEntries: valid,
                        numberOfInvalidEntries: invalid,
                        dynamicallyObservedFiles: ReadOnlyArray<AbsolutePath>.From(dynamicallyObservedFiles),
                        dynamicallyObservedEnumerations: ReadOnlyArray<AbsolutePath>.From(dynamicallyObservedEnumerations),
                        allowedUndeclaredSourceReads: allowedUndeclaredSourceReads.ToReadOnlySet(),
                        absentPathProbesUnderNonDependenceOutputDirectories: absentPathProbesUnderNonDependenceOutputDirectories.ToReadOnlySet());
                }
            }
        }

        private static bool CanIgnoreAbsentPathProbe(
            SemanticPathExpander pathExpander,
            Dictionary<AbsolutePath, (DirectoryMembershipFilter, DirectoryEnumerationMode)> enumeratedDirectories,
            PathTable pathTable,
            AbsolutePath path,
            AbsolutePath lastAbsentPath,
            bool isCacheLookup)
        {
            if (isCacheLookup)
            {
                return false;
            }

            (DirectoryMembershipFilter directoryMemberShipFilter, DirectoryEnumerationMode directoryEnumerationMode) tuple;

            AbsolutePath parent = path.GetParent(pathTable);
            if (enumeratedDirectories.TryGetValue(parent, out tuple) && tuple.directoryEnumerationMode == DirectoryEnumerationMode.RealFilesystem && tuple.directoryMemberShipFilter.Include(pathTable, path))
            {
                return true;
            }

            // Skip nested absent paths except the uppermost one
            return lastAbsentPath.IsValid && path.IsWithin(pathTable, lastAbsentPath);
        }

        /// <summary>
        /// Temporary until fixing the subtle Bug #1016583, Bug #1016589 regarding processing observations in distributed builds.
        /// </summary>
        private static string GetDiagnosticsInfo(
            AbsolutePath path,
            CacheablePipInfo pip,
            PathTable pathTable,
            HashSet<AbsolutePath> sealContents,
            List<SourceSealWithPatterns> sourceDirectoriesTopDirectoryOnly,
            List<SourceSealWithPatterns> sourceDirectoriesAllDirectories,
            ObservationFlags flags)
        {
            bool isUnderSourceSeal = false;
            for (int j = 0; j < sourceDirectoriesTopDirectoryOnly.Count && !isUnderSourceSeal; j++)
            {
                var sourceDirectory = sourceDirectoriesTopDirectoryOnly[j].Path;
                if (path.GetParent(pathTable) == sourceDirectory)
                {
                    isUnderSourceSeal = true;
                }
            }

            for (int j = 0; j < sourceDirectoriesAllDirectories.Count && !isUnderSourceSeal; j++)
            {
                var sourceDirectory = sourceDirectoriesAllDirectories[j].Path;
                if (path != sourceDirectory && path.IsWithin(pathTable, sourceDirectory))
                {
                    isUnderSourceSeal = true;
                }
            }

            var isUnderSeal = sealContents.Contains(path);

            StringBuilder flagsStr = new StringBuilder();
            if ((flags & ObservationFlags.DirectoryLocation) != 0)
            {
                flagsStr.Append("DirectoryLocation,");
            }

            if ((flags & ObservationFlags.Enumeration) != 0)
            {
                flagsStr.Append("Enumeration,");
            }

            if ((flags & ObservationFlags.FileProbe) != 0)
            {
                flagsStr.Append("FileProbe,");
            }

            return I($"Path: {path.ToString(pathTable)} - PipDescription: {pip.Description} - IsUnderSourceSeal: {isUnderSourceSeal} - IsUnderSeal: {isUnderSeal} - Flags: {flagsStr.ToString()}");
        }

        /// <summary>
        /// Checks if a path is within any of a list of paths. This is not a very performant thing to do. make sure it stays
        /// in the error reporting path only
        /// </summary>
        private static bool IsPathWithinAny(PathTable pathTable, AbsolutePath path, IEnumerable<AbsolutePath> paths)
        {
            foreach (var item in paths)
            {
                if (path.IsWithin(pathTable, item))
                {
                    return true;
                }
            }

            return false;
        }

        private static ObservationInfo GetObservationInfo<TTarget, TEnv, TObservation>(
            TEnv environment,
            TTarget target,
            TObservation observation,
            bool allowUndeclaredSourceReads)
            where TTarget : struct, IObservedInputProcessingTarget<TObservation>
            where TEnv : IObservedInputProcessingEnvironment
        {
            AbsolutePath path = target.GetPathOfObservation(observation);
            var flags = target.GetObservationFlags(observation);
            if (flags.IsHashingRequired())
            {
                var fileContentInfoTask = environment.TryQuerySealedOrUndeclaredInputContent(path, target.Description, allowUndeclaredSourceReads);
                return new ObservationInfo(path, flags, fileContentInfoTask);
            }

            // If flags have FileProbe, DirectoryLocation, or Enumeration, we do not need to hash the path.
            return new ObservationInfo(path, flags, s_nullFileContentInfoTask);
        }

        /// <summary>
        /// Computes the set of paths which use search path enumerations and the accessed file name set under
        /// the search paths.
        /// </summary>
        private static DirectoryMembershipFilter ComputeSearchPathsAndFilter<TTarget, TObservation>(
            ref SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer> observedAccessedFileNames,
            PathTable pathTable,
            CacheablePipInfo pip,
            TTarget target,
            ReadOnlyArray<TObservation> observations,
            ObservedInputType[] observationTypes,
            bool[] isUnsuppressedObservation,
            HashSet<AbsolutePath> searchPaths)
            where TTarget : struct, IObservedInputProcessingTarget<TObservation>
        {
            using (var pooledPathSet = Pools.GetAbsolutePathSet())
            using (var pooledStringIdSet = Pools.GetStringIdSet())
            {
                HashSet<StringId> accessedFileNames = pooledStringIdSet.Instance;
                HashSet<AbsolutePath> visitedPaths = pooledPathSet.Instance;

                bool addFileNamesFromObservations = false;
                if (!observedAccessedFileNames.IsValid)
                {
                    addFileNamesFromObservations = true;
                }
                else
                {
                    foreach (var fileName in observedAccessedFileNames)
                    {
                        accessedFileNames.Add(fileName);
                    }
                }

                for (int i = 0; i < observations.Length; i++)
                {
                    if (!isUnsuppressedObservation[i])
                    {
                        continue;
                    }

                    TObservation observation = observations[i];
                    AbsolutePath path = target.GetPathOfObservation(observation);
                    var type = observationTypes[i];

                    bool isDirectoryEnumeration = type == ObservedInputType.DirectoryEnumeration;

                    if (isDirectoryEnumeration)
                    {
                        bool isSearchPathEnumeration = target.IsSearchPathEnumeration(observation);
                        if (isSearchPathEnumeration)
                        {
                            searchPaths.Add(path);
                        }
                    }

                    if (addFileNamesFromObservations)
                    {
                        AddAccessedFileNames(pathTable, searchPaths, visitedPaths, accessedFileNames, path, isDirectoryEnumeration);
                    }
                }

                if (addFileNamesFromObservations)
                {
                    observedAccessedFileNames = SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer>.SortUnsafe(
                                                accessedFileNames.ToArray(),
                                                new CaseInsensitiveStringIdComparer(pathTable.StringTable));
                }

                if (searchPaths.Count == 0)
                {
                    return DirectoryMembershipFilter.AllowAllFilter;
                }

                foreach (var dependency in pip.Dependencies)
                {
                    var path = dependency.Path;
                    bool isDirectory = false;
                    AddAccessedFileNames(pathTable, searchPaths, visitedPaths, accessedFileNames, path, isDirectory);
                }

                foreach (var dependency in pip.DirectoryDependencies)
                {
                    var path = dependency.Path;
                    bool isDirectory = true;
                    AddAccessedFileNames(pathTable, searchPaths, visitedPaths, accessedFileNames, path, isDirectory);
                }

                var searchPathFilter = new SearchPathDirectoryMembershipFilter(pathTable, accessedFileNames);
                return searchPathFilter;
            }
        }

        /// <summary>
        /// Adds accessed file names for files/directories under the search paths
        /// </summary>
        private static void AddAccessedFileNames(
            PathTable pathTable,
            HashSet<AbsolutePath> searchPaths,
            HashSet<AbsolutePath> visitedPaths,
            HashSet<StringId> accessedFileNamesWithoutExtension,
            AbsolutePath path,
            bool isDirectory)
        {
            if (searchPaths.Count == 0)
            {
                return;
            }

            while (path.IsValid && visitedPaths.Add(path))
            {
                var parent = path.GetParent(pathTable);
                if (searchPaths.Contains(parent))
                {
                    var fileNameWithoutExtension = path.GetName(pathTable);
                    if (!isDirectory)
                    {
                        fileNameWithoutExtension = fileNameWithoutExtension.RemoveExtension(pathTable.StringTable);
                    }

                    accessedFileNamesWithoutExtension.Add(fileNameWithoutExtension.StringId);
                }

                isDirectory = true;
                path = parent;
            }
        }

        private static ObservedInputType MapPathExistenceToObservedInputType(PathTable pathTable, AbsolutePath path, PathExistence pathExistence, ObservationFlags observationFlags)
        {
            switch (pathExistence)
            {
                case PathExistence.Nonexistent:
                    return ObservedInputType.AbsentPathProbe;
                case PathExistence.ExistsAsFile:
                    if ((observationFlags & (ObservationFlags.DirectoryLocation | ObservationFlags.Enumeration)) != 0)
                    {
                        if (FileUtilities.IsDirectorySymlinkOrJunction(path.ToString(pathTable)))
                        {
                            if ((observationFlags & ObservationFlags.Enumeration) != 0)
                            {
                                // Enumeration of directory through directory symlink or junction.
                                return ObservedInputType.DirectoryEnumeration;
                            }
                            else if ((observationFlags & ObservationFlags.DirectoryLocation) != 0)
                            {
                                // Probing of directory through directory symlink or junction.
                                return ObservedInputType.ExistingDirectoryProbe;
                            }
                        }

                        // If the location is a DirectoryLocation and the result of the probe is ExistsAs File,
                        // The directory is not existent. Report it as an absent path probe. The cache/fingerprints
                        // already deal with such probe properly since it is the same as absent path probe in the enumeration case.
                        return ObservedInputType.AbsentPathProbe;
                    }
                    else if ((observationFlags & ObservationFlags.FileProbe) == 0)
                    {
                        // If FileProbe flag does not exist, then this access is a content read.
                        return ObservedInputType.FileContentRead;
                    }
                    else
                    {
                        return ObservedInputType.ExistingFileProbe;
                    }
                case PathExistence.ExistsAsDirectory:
                    if ((observationFlags & ObservationFlags.Enumeration) == 0)
                    {
                        return ObservedInputType.ExistingDirectoryProbe;
                    }
                    else
                    {
                        return ObservedInputType.DirectoryEnumeration;
                    }

                default:
                    throw Contract.AssertFailure("Unhandled PathExistence value");
            }
        }

        /// <summary>
        /// Returns the strongest validation status (success is weaker than mismatch, etc.) of two.
        /// </summary>
        private static ObservedInputProcessingStatus CombineObservedInputProcessingStatus(
            ObservedInputProcessingStatus current,
            ObservedInputProcessingStatus combineWith)
        {
            return (ObservedInputProcessingStatus)Math.Max((int)current, (int)combineWith);
        }

        private static void HandleFailureResult(ObservedInputAccessCheckFailureAction result, ref ObservedInputProcessingStatus status, ref int invalid)
        {
            if (result == ObservedInputAccessCheckFailureAction.Fail)
            {
                invalid++;
                status = CombineObservedInputProcessingStatus(status, ObservedInputProcessingStatus.Mismatched);
            }
            else
            {
                Contract.Assert(result == ObservedInputAccessCheckFailureAction.SuppressAndIgnorePath);
            }
        }

        /// <summary>
        /// Struct used to get detailed info of observation.
        /// </summary>
        private readonly struct ObservationInfo
        {
            /// <summary>
            /// Path of observation.
            /// </summary>
            public readonly AbsolutePath Path;

            /// <summary>
            /// Observation flags (modifiers).
            /// </summary>
            public readonly ObservationFlags ObservationFlags;

            /// <summary>
            /// Task for computing file content info.
            /// </summary>
            /// <remarks>
            /// If an observation is a file and it exists, then file content info cannot be null.
            /// </remarks>
            public readonly Task<FileContentInfo?> FileContentInfoTask;

            public ObservationInfo(
                AbsolutePath path,
                ObservationFlags observationFlags,
                Task<FileContentInfo?> fileContentInfoTask)
            {
                Contract.Requires(path.IsValid);
                Contract.Requires(fileContentInfoTask != null);

                Path = path;
                ObservationFlags = observationFlags;
                FileContentInfoTask = fileContentInfoTask;
            }
        }
    }

    /// <summary>
    /// See <see cref="ObservedInputProcessor"/>.
    /// </summary>
    public interface IObservedInputProcessingTarget<in TObservation>
    {
        string Description { get; }

        AbsolutePath GetPathOfObservation(TObservation observation);

        ObservationFlags GetObservationFlags(TObservation observation);

        bool IsSearchPathEnumeration(TObservation directoryEnumeration);

        /// <summary>
        /// Action to perform on access check failure
        /// </summary>
        /// <param name="observation">the observation</param>
        /// <param name="fromTopLevelDirectory">Whether the observation was nested deeploy under a source sealed directory
        /// that is configured as a top only directory. This scenar ends up hitting the sealed directory codepath due
        /// the access being allowed but we want to report it differently.</param>
        ObservedInputAccessCheckFailureAction OnAccessCheckFailure(TObservation observation, bool fromTopLevelDirectory);

        void ReportUnexpectedAccess(TObservation observation, ObservedInputType observedInputType);

        bool IsReportableUnexpectedAccess(AbsolutePath path);

        /// <summary>
        /// Historically this was used as part of file accesss validation logic. Now that functionality has been moved
        /// but this is still utilized as a test hook.
        /// </summary>
        void CheckProposedObservedInput(TObservation observation, ObservedInput proposedObservedInput);

        string GetEnumeratePatternRegex(TObservation directoryEnumeration);
    }

    /// <summary>
    /// Interface representing all external queries made by <see cref="ObservedInputProcessor"/>.
    /// This is mostly a projection of <see cref="IPipExecutionEnvironment"/> to facilitate testing.
    /// </summary>
    internal interface IObservedInputProcessingEnvironment
    {
        /// <nodoc />
        PipExecutionContext Context { get; }

        /// <summary>
        /// Counters for pips executed in this environment. These counters include aggregate pip and caching performance information.
        /// </summary>
        CounterCollection<PipExecutorCounter> Counters { get; }

        /// <summary>
        /// The scoped execution state.
        /// </summary>
        PipExecutionState.PipScopeState State { get; }

        /// <summary>
        /// Used to retrieve semantic path information
        /// </summary>
        SemanticPathExpander PathExpander { get; }

        /// <summary>
        /// Probes a path for existence
        /// </summary>
        Possible<PathExistence> TryProbeAndTrackForExistence(AbsolutePath path, CacheablePipInfo pipInfo, bool isReadOnly, bool trackPathExistence);

        /// <summary>
        /// Gets the fingerprint of a directory
        /// </summary>
        DirectoryFingerprint? TryQueryDirectoryFingerprint(
            AbsolutePath directoryPath,
            CacheablePipInfo process,
            DirectoryMembershipFilter filter,
            bool isReadOnlyDirectory,
            DirectoryMembershipHashedEventData eventData,
            out DirectoryEnumerationMode enumerationMode,
            bool trackPathExistence);

        /// <summary>
        /// See <see cref="FileContentManager.TryQuerySealedOrUndeclaredInputContentAsync"/>
        /// </summary>
        Task<FileContentInfo?> TryQuerySealedOrUndeclaredInputContent(AbsolutePath sealedPath, string consumerDescription, bool allowUndeclaredSourceReads);

        /// <summary>
        /// See <see cref="BuildXL.Scheduler.Artifacts.FileContentManager.ListSealedDirectoryContents"/>
        /// </summary>
        SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealedDirectoryContents(DirectoryArtifact directoryArtifact);

        /// <summary>
        /// See <see cref="IPipExecutionEnvironment.IsSourceSealedDirectory"/>
        /// </summary>
        bool IsSourceSealedDirectory(DirectoryArtifact directoryArtifact, out bool allDirectories, out ReadOnlyArray<StringId> patterns);

        /// <summary>
        /// Returns whether there is (by walking the path upwards) an output directory -shared or exclusive- containing <paramref name="path"/>
        /// </summary>
        bool IsPathUnderOutputDirectory(AbsolutePath path);
    }

    /// <summary>
    /// Result of <see cref="IObservedInputProcessingTarget{TObservation}.OnAccessCheckFailure"/>
    /// </summary>
    public enum ObservedInputAccessCheckFailureAction
    {
        /// <summary>
        /// This single access-check failure should result in the 'check access' operation failing in aggregate
        /// (with no path -> content hash mappings returned).
        /// </summary>
        Fail,

        /// <summary>
        /// This single access-check failure should be suppressed. The overall 'check access' operation may succeed,
        /// but there will not be a path -> content hash mapping for this path (as if it was not in the set to check).
        /// </summary>
        SuppressAndIgnorePath,
    }

    /// <summary>
    /// Overall status of <see cref="ObservedInputProcessingResult"/>.
    /// </summary>
    public enum ObservedInputProcessingStatus
    {
        /// <summary>
        /// All assertions were validated and had expected fingerprints.
        /// </summary>
        Success = 0,

        /// <summary>
        /// Validation completed, but at least one assertion had an unexpected fingerprint / hash.
        /// </summary>
        Mismatched = 1,

        /// <summary>
        /// Validation was aborted. At least one fingerprint or hash could not be computed.
        /// An error-level event has been logged, and so the calling pip must fail.
        /// </summary>
        Aborted = 2,
    }

    /// <summary>
    /// Result of applying an <see cref="ObservedInputProcessor"/> to observations.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ObservedInputProcessingResult
    {
        /// <summary>
        /// Overall status. Note that some fields and operations are unavailable if this status is not <see cref="ObservedInputProcessingStatus.Success"/>
        /// </summary>
        public readonly ObservedInputProcessingStatus Status;

        /// <summary>
        /// Number of observations found to be valid (note that 'skipped' observations are excluded).
        /// </summary>
        public readonly int NumberOfValidEntries;

        /// <summary>
        /// Number of observations found to be invalid (always 0 on success, and non-zero on failure).
        /// </summary>
        public readonly int NumberOfInvalidEntries;

        /// <summary>
        /// The list of dynamically observed files. i.e., the enumerations that were not in the graph, but should be considered for invalidating incremental scheduling state.
        /// </summary>
        public readonly ReadOnlyArray<AbsolutePath> DynamicallyObservedFiles;

        /// <summary>
        /// The list of dynamically observed enumerations. i.e., the enumerations that were not in the graph, but should be considered for invalidating incremental scheduling state.
        /// </summary>
        public readonly ReadOnlyArray<AbsolutePath> DynamicallyObservedEnumerations;

        /// <summary>
        /// The set of undeclared reads (or probes) that occurred on source files
        /// </summary>
        public readonly IReadOnlySet<AbsolutePath> AllowedUndeclaredSourceReads;

        /// <summary>
        /// The set of absent file probes that occurred under the cone of an output directory.
        /// </summary>
        public readonly IReadOnlySet<AbsolutePath> AbsentPathProbesUnderNonDependenceOutputDirectories;

        private readonly SortedReadOnlyArray<ObservedInput, ObservedInputExpandedPathComparer> m_observedInputs;

        private readonly SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer> m_observedAccessFileNames;

        private ObservedInputProcessingResult(
            ObservedInputProcessingStatus status,
            SortedReadOnlyArray<ObservedInput, ObservedInputExpandedPathComparer> observedInputs,
            SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer> observedAccessFileNames,
            int numberOfValidEntires,
            int numberOfInvalidEntries,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedEnumerations,
            IReadOnlySet<AbsolutePath> allowedUndeclaredSourceReads,
            IReadOnlySet<AbsolutePath> absentPathProbesUnderNonDependenceOutputDirectories)
        {
            Contract.Requires(status != ObservedInputProcessingStatus.Success || observedInputs.IsValid);
            Contract.Requires(status != ObservedInputProcessingStatus.Success || observedAccessFileNames.IsValid);
            Contract.Requires((status != ObservedInputProcessingStatus.Success) || (numberOfInvalidEntries == 0));
            Contract.Requires(dynamicallyObservedFiles.IsValid);
            Contract.Requires(dynamicallyObservedEnumerations.IsValid);
            Contract.Requires(allowedUndeclaredSourceReads != null);
            Contract.Requires(absentPathProbesUnderNonDependenceOutputDirectories != null);

            Status = status;
            NumberOfValidEntries = numberOfValidEntires;
            NumberOfInvalidEntries = numberOfInvalidEntries;
            DynamicallyObservedFiles = dynamicallyObservedFiles;
            DynamicallyObservedEnumerations = dynamicallyObservedEnumerations;
            m_observedInputs = observedInputs;
            m_observedAccessFileNames = observedAccessFileNames;
            AllowedUndeclaredSourceReads = allowedUndeclaredSourceReads;
            AbsentPathProbesUnderNonDependenceOutputDirectories = absentPathProbesUnderNonDependenceOutputDirectories;
        }

        /// <nodoc />
        public static ObservedInputProcessingResult CreateForFailure(
            ObservedInputProcessingStatus status,
            int numberOfValidEntries,
            int numberOfInvalidEntries,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedEnumerations,
            IReadOnlySet<AbsolutePath> allowedUndeclaredSourceReads,
            IReadOnlySet<AbsolutePath> absentPathProbesUnderNonDependenceOutputDirectories)
        {
            Contract.Requires(status != ObservedInputProcessingStatus.Success);
            Contract.Requires(dynamicallyObservedFiles.IsValid);
            Contract.Requires(dynamicallyObservedEnumerations.IsValid);
            Contract.Requires(allowedUndeclaredSourceReads != null);
            Contract.Requires(absentPathProbesUnderNonDependenceOutputDirectories != null);

            return new ObservedInputProcessingResult(
                status,
                default(SortedReadOnlyArray<ObservedInput, ObservedInputExpandedPathComparer>),
                default(SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer>),
                numberOfValidEntires: numberOfValidEntries,
                numberOfInvalidEntries: numberOfInvalidEntries,
                dynamicallyObservedFiles: dynamicallyObservedFiles,
                dynamicallyObservedEnumerations: dynamicallyObservedEnumerations,
                allowedUndeclaredSourceReads: allowedUndeclaredSourceReads,
                absentPathProbesUnderNonDependenceOutputDirectories: absentPathProbesUnderNonDependenceOutputDirectories);
        }

        /// <nodoc />
        public static ObservedInputProcessingResult CreateForSuccess(
            SortedReadOnlyArray<ObservedInput, ObservedInputExpandedPathComparer> observedInputs,
            SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer> observedAccessedFileNames,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedEnumerations,
            IReadOnlySet<AbsolutePath> allowedUndeclaredSourceReads,
            IReadOnlySet<AbsolutePath> absentPathProbesUnderNonDependenceOutputDirectories)
        {
            Contract.Requires(dynamicallyObservedFiles.IsValid);
            Contract.Requires(dynamicallyObservedEnumerations.IsValid);
            Contract.Requires(allowedUndeclaredSourceReads != null);

            return new ObservedInputProcessingResult(
                ObservedInputProcessingStatus.Success,
                observedInputs,
                observedAccessedFileNames,
                numberOfValidEntires: observedInputs.Length,
                numberOfInvalidEntries: 0,
                dynamicallyObservedFiles: dynamicallyObservedFiles,
                dynamicallyObservedEnumerations: dynamicallyObservedEnumerations,
                allowedUndeclaredSourceReads: allowedUndeclaredSourceReads,
                absentPathProbesUnderNonDependenceOutputDirectories: absentPathProbesUnderNonDependenceOutputDirectories);
        }

        /// <summary>
        /// Individual <see cref="ObservedInputs"/> corresponding to the provided operations.
        /// Note that this field may only be accessed for a successful result.
        /// </summary>
        public SortedReadOnlyArray<ObservedInput, ObservedInputExpandedPathComparer> ObservedInputs
        {
            get
            {
                Contract.Requires(Status == ObservedInputProcessingStatus.Success);
                return m_observedInputs;
            }
        }

        /// <summary>
        /// Individual <see cref="ObservedAccessFileNames"/> corresponding to the provided operations.
        /// Note that this field may only be accessed for a successful result.
        /// </summary>
        public SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer> ObservedAccessFileNames
        {
            get
            {
                Contract.Requires(Status == ObservedInputProcessingStatus.Success);
                return m_observedAccessFileNames;
            }
        }

        /// <summary>
        /// Projects the set of paths (from <see cref="ObservedInputs"/>) into an <see cref="ObservedPathSet"/>.
        /// Note that this field may only be performed for a successful result.
        /// </summary>
        public ObservedPathSet GetPathSet([CanBeNull]UnsafeOptions unsafeOptions)
        {
            Contract.Requires(Status == ObservedInputProcessingStatus.Success);

            // Note that we don't deduplicate identical paths here. ObservedPathSet allows duplicates on construction,
            // though it reserves the right to canonicalize them away at any time.
            ObservedPathEntry[] paths = new ObservedPathEntry[m_observedInputs.Length];
            for (int i = 0; i < m_observedInputs.Length; i++)
            {
                paths[i] = ObservedPathEntry.FromObservedInput(m_observedInputs[i]);
            }

            return new ObservedPathSet(
                SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer>.FromSortedArrayUnsafe(
                    ReadOnlyArray<ObservedPathEntry>.FromWithoutCopy(paths),
                    new ObservedPathEntryExpandedPathComparer(m_observedInputs.Comparer.PathComparer)),
                m_observedAccessFileNames,
                unsafeOptions);
        }

        /// <summary>
        /// Computes a strong fingerprint from <see cref="ObservedInputs"/>.
        /// Note that this field may only be performed for a successful result.
        /// </summary>
        public StrongContentFingerprint ComputeStrongFingerprint(PathTable pathTable, WeakContentFingerprint weakFingerprint, ContentHash pathSetHash)
        {
            Contract.Requires(Status == ObservedInputProcessingStatus.Success);

            using (var hasher = StrongContentFingerprint.CreateHashingHelper(
                pathTable,
                recordFingerprintString: false))
            {
                AddStrongFingerprintContent(hasher, weakFingerprint, pathSetHash, m_observedInputs.BaseArray);
                return new StrongContentFingerprint(hasher.GenerateHash());
            }
        }

        /// <summary>
        /// Adds the elements of a strong fingerprint computation to the fingerprinter.
        /// </summary>
        internal static void AddStrongFingerprintContent(IFingerprinter fingerprinter, WeakContentFingerprint weakFingerprint, ContentHash pathSetHash, ReadOnlyArray<ObservedInput> observedInputs)
        {
            fingerprinter.Add("Namespace", (int)StrongContentFingerprintNamespace.ObservedInputs);
            fingerprinter.Add("WeakFingerprint", weakFingerprint.Hash);

            // TODO: the PathSet and weakFingerprint shouldn't really need to be members of the StrongFingerprint.
            // When the cache is queries for the strong fingerprint, it is queried with a triple of:
            // * WeakFingerprint
            // * PathSet hash
            // * Strong fingerprint
            // Previously, the paths were included in the StrongFingerprint here, and now the PathSet hash is being
            // included to preserve the same uniqueness. But it most likely isn't necessary. See StrongContentFingerprint
            // for details.
            fingerprinter.Add(ObservedPathEntryConstants.PathSet, pathSetHash);

            fingerprinter.AddCollection<ObservedInput, ReadOnlyArray<ObservedInput>>(
                ObservedInputConstants.ObservedInputs,
                observedInputs,
                (h, observedInput) =>
                {
                    switch (observedInput.Type)
                    {
                        case ObservedInputType.AbsentPathProbe:
                            h.Add(ObservedInputConstants.AbsentPathProbe, string.Empty);
                            break;
                        case ObservedInputType.FileContentRead:
                            h.Add(ObservedInputConstants.FileContentRead, observedInput.Hash);
                            break;
                        case ObservedInputType.DirectoryEnumeration:
                            h.Add(ObservedInputConstants.DirectoryEnumeration, observedInput.Hash);
                            break;
                        case ObservedInputType.ExistingDirectoryProbe:
                            h.Add(ObservedInputConstants.ExistingDirectoryProbe, string.Empty);
                            break;
                        case ObservedInputType.ExistingFileProbe:
                            h.Add(ObservedInputConstants.ExistingFileProbe, string.Empty);
                            break;
                        default:
                            throw Contract.AssertFailure("Unhandled ObservedInputType");
                    }
                });
        }
    }

    /// <summary>
    /// Adapter from <see cref="IPipExecutionEnvironment"/> to <see cref="IObservedInputProcessingEnvironment"/>.
    /// </summary>
    /// <remarks>
    /// Determining which files to consider when computing a pip's input assertions is quite tricky:
    /// - The real filesystem will vary build over build as output files first don't exist, and then later are created.
    ///     If it were used directory, it would take multiple builds to achieve stable cache hits
    /// - A filesystem based on the build graph could be used because it should be aware of all files that will be produced
    ///     as part of the current build. But this has the problem that the scope of the filesystem changes depending on
    ///     how pips are filtered in the build.
    /// The following is the algorithm for determining the files available to generate input assertions is our best strategy
    /// for a compromise between getting a consistent level of caching regardless of how the build is filtered vs.
    /// rerunning pips when reasonable.
    ///
    /// Assume 3 filesystems:
    /// - realFs: the actual filesystem
    /// - graphFs: the entire graph. This can change build over build
    /// - pipFs: a pip projection of the filesystem that only sees the pip's immediately declared dependencies for enumerations
    ///
    /// InputAssertion ComputeInputAssertionForPath(AbsolutePath path, CacheablePipInfo pip)
    /// {
    ///     bool existsAnywhereAsFile = realFs.ExistsAsAFile(path) || graphFs.ExistsAsFile(path);
    ///     if (existsAnywhereAsFile)
    ///     {
    ///         RequireDeclaredDependency()
    ///         return CreateExistentFileInputAssertion()
    ///     }
    ///     else
    ///     {
    ///         // This could be a directory or an absent file
    ///         var existentialFs = IsMountWriteableByAnyModule(path) ? pipFs.Create(pip) : realFs;
    ///         var fingerprint = ComputeDirectoryFingerprint(path, existentialFs)
    ///         if (finterprint.IsValid)
    ///         {
    ///             return CreateDirectoryFilgerprint(fingerprint)
    ///         }
    ///         else
    ///         {
    ///             // Treat empty directories as absent files
    ///             return CreateAbsentFileInputAssertion()
    ///         }
    ///     }
    /// }
    ///
    /// </remarks>
    internal class ObservedInputProcessingEnvironmentAdapter : IObservedInputProcessingEnvironment, IDisposable
    {
        private static readonly ObjectPool<PipFileSystemView> s_pool = new ObjectPool<PipFileSystemView>(
            () => new PipFileSystemView(),
            state => state.Clear());

        private static readonly ConcurrentBigSet<AbsolutePath> RegexFilterPaths = new ConcurrentBigSet<AbsolutePath>();
        private static readonly ConcurrentBigSet<AbsolutePath> UnionFilterPaths = new ConcurrentBigSet<AbsolutePath>();
        private static readonly ConcurrentBigSet<AbsolutePath> AllowAllFilterPaths = new ConcurrentBigSet<AbsolutePath>();
        private static readonly ConcurrentBigSet<AbsolutePath> SearchPathFilterPaths = new ConcurrentBigSet<AbsolutePath>();

        private FileSystemView FileSystemView => m_env.State.FileSystemView;

        private readonly IPipExecutionEnvironment m_env;
        private readonly PipExecutionState.PipScopeState m_state;
        private PooledObjectWrapper<PipFileSystemView>? m_pooledPipFileSystem;
        private DirectoryMembershipFilter m_directoryMembershipFilter;

        private PipFileSystemView PipFileSystem => m_pooledPipFileSystem?.Instance;

        public PathTable PathTable => m_env.Context.PathTable;

        public PipExecutionContext Context => m_env.Context;

        public CounterCollection<PipExecutorCounter> Counters => m_env.Counters;

        public PipExecutionState.PipScopeState State => m_state;

        public SemanticPathExpander PathExpander => m_state.PathExpander;

        public ObservedInputProcessingEnvironmentAdapter(
            IPipExecutionEnvironment environment,
            PipExecutionState.PipScopeState state)
        {
            m_env = environment;
            m_state = state;
            m_directoryMembershipFilter = null;
            m_pooledPipFileSystem = null;
        }

        public void Dispose()
        {
            m_pooledPipFileSystem?.Dispose();
        }

        public Possible<PathExistence> TryProbeAndTrackForExistence(AbsolutePath path, CacheablePipInfo pipInfo, bool isReadOnly, bool trackPathExistence = true)
        {
            // ****************** CAUTION *********************
            // The logic below is replicated conservatively in IncrementalSchedulingState.ProcessNewlyPresentPath.
            // Any change here should be implemented conservatively there.
            // ************************************************
            var mountInfo = PathExpander.GetSemanticPathInfo(path);
            if (mountInfo.IsValid && !mountInfo.AllowHashing)
            {
                return PathExistence.Nonexistent;
            }

            // PathExistence gets cached for the duration of the build. This is acceptable because:
            // 1. PathExistence checks not only look at the filesystem but also at files that will eventually be produced
            //      by other pips in the build.
            // 2. Processes are required to declare their outputs. So if a process produces a file not in the build graph
            //      and thus not correctly represented by this caching, the producing pip will fail the build anyway.
            // 3. Tracking file accesses does not need to happen multiple times within the same build.
            //
            // There are potentially some issues with this approach:
            // * Files created outside of the build may not be represented if they are created after the build starts.
            // * Processes that previously probed for nonexistent files that are newly produced but undeclared, won't
            //     be run. This is case #2 described above. The build will still fail and the subsequent build will cause
            //     the consuming pip to rerun
            // In general, these issues are acceptable since querying the filesystem every time wouldn't necessarily produce
            // better results. There'd still be an inherent race.

            Possible<PathExistence> existence;

            // Check if path will be eventually produced as a file/directory by querying 'output file system'
            // TODO: Should the file content manager be queried about eventual production (i.e. lazy symlink creation)?
            existence = FileSystemView.GetExistence(path, FileSystemViewMode.Output);

            // NOTE: We don't check success of existence from produced file system as it should never return failure
            if (existence.Result != PathExistence.Nonexistent)
            {
                // Produced file system shows the path as existent, so use that result
                return existence;
            }

            bool hasBuildOutputs = mountInfo.HasPotentialBuildOutputs && !isReadOnly;

            // If path is not eventually produced, query real file system
            existence = FileSystemView.GetExistence(path, FileSystemViewMode.Real, isReadOnly: !hasBuildOutputs, cachePathExistence: trackPathExistence);

            if (existence.Succeeded && (existence.Result == PathExistence.Nonexistent || existence.Result == PathExistence.ExistsAsDirectory) && hasBuildOutputs)
            {
                var fullGraphExistExistence = FileSystemView.GetExistence(path, FileSystemViewMode.FullGraph);
                if (fullGraphExistExistence.Result == PathExistence.ExistsAsDirectory)
                {
                    return PathExistence.ExistsAsDirectory;
                }
                else if (fullGraphExistExistence.Result == PathExistence.ExistsAsFile)
                {
                    // The file is a source file so we return non-existent. This is to match legacy behavior where non-existence is returned in this case
                    // which returns NonExistent even though the real file system existence could also be ExistsAsDirectory
                    // TODO: Consider whether we should use the real file system existence which could be Nonexistent OR ExistsAsDirectory
                    return PathExistence.Nonexistent;
                }
                else
                {
                    // ExistsAsDirectory is handled before querying real file system
                    Contract.Assert(fullGraphExistExistence.Result == PathExistence.Nonexistent);
                    return PathExistence.Nonexistent;
                }
            }

            // Return the real file system existence
            return existence;
        }

        /// <inheritdoc />
        public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealedDirectoryContents(DirectoryArtifact directoryArtifact)
        {
            return m_env.State.FileContentManager.ListSealedDirectoryContents(directoryArtifact);
        }

        /// <inheritdoc />
        public bool IsSourceSealedDirectory(DirectoryArtifact directoryArtifact, out bool allDirectories, out ReadOnlyArray<StringId> patterns)
        {
            return m_env.IsSourceSealedDirectory(directoryArtifact, out allDirectories, out patterns);
        }

        /// <inheritdoc />
        public bool IsPathUnderOutputDirectory(AbsolutePath path)
        {
            return m_env.PipGraphView.IsPathUnderOutputDirectory(path, out _);
        }

        /// <summary>
        /// Determines the way a directory should be enumerated based on the directory. This is where the logic for switching
        /// between various filesystem rules lives
        /// </summary>
        internal DirectoryEnumerationMode DetermineEnumerationModeAndRule(AbsolutePath directoryPath, out DirectoryMembershipFingerprinterRule rule)
        {
            return DetermineEnumerationModeAndRule(directoryPath, isReadOnlyDirectory: false, rule: out rule);
        }

        /// <summary>
        /// Determines the way a directory should be enumerated based on the directory. This is where the logic for switching
        /// between various filesystem rules lives
        /// </summary>
        internal DirectoryEnumerationMode DetermineEnumerationModeAndRule(AbsolutePath directoryPath, bool isReadOnlyDirectory, out DirectoryMembershipFingerprinterRule rule)
        {
            Contract.Assume(directoryPath.IsValid);

            // Enumerations to directories that are under un-hashable mounts do not contribute to fingerprints
            // Note: If a path isn't within a mount, it gets the DefaultFingerprint as well. This was the existing
            // behavior at the time of the time of the refactoring but is something that might need to change.
            SemanticPathInfo mountInfo = PathExpander.GetSemanticPathInfo(directoryPath);
            if (!mountInfo.AllowHashing || (!mountInfo.IsReadable && !mountInfo.IsWritable))
            {
                rule = null;
                return DirectoryEnumerationMode.DefaultFingerprint;
            }

            // If the config says to always use the minimal graph, honor it
            if (m_env.Configuration.Sandbox.FileSystemMode == BuildXL.Utilities.Configuration.FileSystemMode.AlwaysMinimalGraph)
            {
                rule = null;
                return DirectoryEnumerationMode.MinimalGraph;
            }

            // Now determine which graph based enumeration mode could be used, if we determine to use the graph
            DirectoryEnumerationMode graphEnumerationMode;
            switch (m_env.Configuration.Sandbox.FileSystemMode)
            {
                case FileSystemMode.RealAndMinimalPipGraph:
                    graphEnumerationMode = DirectoryEnumerationMode.MinimalGraph;
                    break;
                case FileSystemMode.RealAndPipGraph:
                    // If the directoryPath is under an opaque directory, then we always use the minimal graph, so
                    // all artifacts from direct dependencies (both static and dynamic content) will be used
                    graphEnumerationMode = m_env.PipGraphView.IsPathUnderOutputDirectory(directoryPath, out _) ?
                        DirectoryEnumerationMode.MinimalGraph :
                        DirectoryEnumerationMode.FullGraph;
                    break;
                default:
                    Contract.Assert(false, "Unknown FileSystemMode");
                    throw new BuildXLException(string.Empty);
            }

            // If the directory has build outputs, the graph enumeration should be used
            if (mountInfo.HasPotentialBuildOutputs && !isReadOnlyDirectory)
            {
                rule = null;
                return graphEnumerationMode;
            }

            // If there is a rule for this directory path, get it.
            if (m_state.DirectoryMembershipFingerprinterRuleSet == null || !m_state.DirectoryMembershipFingerprinterRuleSet.TryGetRule(directoryPath, out rule))
            {
                rule = null;
            }

            // Check if there is a rule preventing filesystem based enumerations
            if (rule != null && rule.DisableFilesystemEnumeration)
            {
                return graphEnumerationMode;
            }
            else
            {
                // Otherwise we can use the real filesystem
                return DirectoryEnumerationMode.RealFilesystem;
            }
        }

        /// <inheritdoc/>
        public DirectoryFingerprint? TryQueryDirectoryFingerprint(
            AbsolutePath directoryPath,
            CacheablePipInfo process,
            DirectoryMembershipFilter filter,
            bool isReadOnlyDirectory,
            DirectoryMembershipHashedEventData eventData,
            out DirectoryEnumerationMode enumerationMode,
            bool trackPathExistence = true)
        {
            Contract.Assume(directoryPath.IsValid);
            Contract.Assume(process != null);
            Contract.Assume(filter != null);
            Contract.Assert(m_directoryMembershipFilter == null, "This method may not be called concurrently");

            try
            {
                m_directoryMembershipFilter = filter;

                DirectoryMembershipFingerprinterRule rule = null;
                enumerationMode = DetermineEnumerationModeAndRule(directoryPath, isReadOnlyDirectory, out rule);

                // If (1) the enumeration does not use the search-based filter and (2) the directory is not enumerated via the minimal graph, cache the fingerprint.
                bool cacheableFingerprint = !eventData.IsSearchPath && enumerationMode != DirectoryEnumerationMode.MinimalGraph;

                if (filter == DirectoryMembershipFilter.AllowAllFilter && AllowAllFilterPaths.Add(directoryPath))
                {
                    Counters.IncrementCounter(PipExecutorCounter.UniqueDirectoriesAllowAllFilter);
                }
                else if (filter is RegexDirectoryMembershipFilter && RegexFilterPaths.Add(directoryPath))
                {
                    Counters.IncrementCounter(PipExecutorCounter.UniqueDirectoriesRegexFilter);
                }
                else if (filter is SearchPathDirectoryMembershipFilter && SearchPathFilterPaths.Add(directoryPath))
                {
                    Counters.IncrementCounter(PipExecutorCounter.UniqueDirectoriesSearchPathFilter);
                }
                else if (filter is UnionDirectoryMembershipFilter && UnionFilterPaths.Add(directoryPath))
                {
                    Counters.IncrementCounter(PipExecutorCounter.UniqueDirectoriesUnionFilter);
                }

                DirectoryFingerprint? result;
                switch (enumerationMode)
                {
                    case DirectoryEnumerationMode.DefaultFingerprint:
                        result = DirectoryFingerprint.Zero;
                        break;

                    case DirectoryEnumerationMode.FullGraph:
                        using (Counters.StartStopwatch(PipExecutorCounter.FullGraphDirectoryEnumerationsDuration))
                        {
                            eventData.IsStatic = true;
                            result = m_env.State.DirectoryMembershipFingerprinter.TryComputeDirectoryFingerprint(
                                directoryPath,
                                process,
                                TryEnumerateDirectoryWithFullGraph,
                                cacheableFingerprint: cacheableFingerprint,
                                rule: rule,
                                eventData: eventData);
                            Counters.IncrementCounter(PipExecutorCounter.FullGraphDirectoryEnumerations);

                            break;
                        }
                    case DirectoryEnumerationMode.MinimalGraph:
                        using (Counters.StartStopwatch(PipExecutorCounter.MinimalGraphDirectoryEnumerationsDuration))
                        {
                            eventData.IsStatic = true;

                            result = m_env.State.DirectoryMembershipFingerprinter.TryComputeDirectoryFingerprint(
                                directoryPath,
                                process,
                                EnumerateDirectoryWithMinimalPipGraph,
                                cacheableFingerprint: cacheableFingerprint,
                                rule: rule,
                                eventData: eventData);
                            Counters.IncrementCounter(PipExecutorCounter.MinimalGraphDirectoryEnumerations);

                            break;
                        }

                    default:
                        Contract.Assume(enumerationMode == DirectoryEnumerationMode.RealFilesystem);

                        var enumerateFunc = trackPathExistence ? (Func<EnumerationRequest, PathExistence?>) TryEnumerateAndTrackDirectoryWithFilesystem : TryEnumerateDirectoryWithFilesystem;
                        using (Counters.StartStopwatch(PipExecutorCounter.RealFilesystemDirectoryEnumerationsDuration))
                        {
                            eventData.IsStatic = false;

                            result = m_env.State.DirectoryMembershipFingerprinter.TryComputeDirectoryFingerprint(
                                directoryPath,
                                process,
                                enumerateFunc,
                                cacheableFingerprint: cacheableFingerprint,
                                rule: rule,
                                eventData: eventData);
                            Counters.IncrementCounter(PipExecutorCounter.RealFilesystemDirectoryEnumerations);

                            break;
                        }
                }

                return result;
            }
            finally
            {
                m_directoryMembershipFilter = null;
            }
        }

        public Task<FileContentInfo?> TryQuerySealedOrUndeclaredInputContent(AbsolutePath sealedPath, string consumerDescription, bool allowUndeclaredSourceReads)
        {
            return m_env.State.FileContentManager.TryQuerySealedOrUndeclaredInputContentAsync(sealedPath, consumerDescription, allowUndeclaredSourceReads);
        }

        private Action<AbsolutePath, string> FilteredHandledEntry(AbsolutePath directory, Action<AbsolutePath, string> handleEntry)
        {
            var filter = m_directoryMembershipFilter;
            var context = Context;
            var counters = Counters;
            return (member, fileName) =>
            {
                bool isFilterPassing;
                using (counters.StartStopwatch(PipExecutorCounter.DirectoryEnumerationFilterDuration))
                {
                    isFilterPassing = filter.Include(member.GetName(context.PathTable), fileName);
                }

                if (isFilterPassing)
                {
                    handleEntry(member, fileName);
                }
            };
        }

        #region Methods for performing directory enumeration

        private PathExistence? TryEnumerateAndTrackDirectoryWithFilesystem(EnumerationRequest request)
        {
            return TryEnumerateDirectory(request, FileSystemViewMode.Real);
        }

        private PathExistence? TryEnumerateDirectoryWithFilesystem(EnumerationRequest request)
        {
            return TryEnumerateDirectory(request, FileSystemViewMode.Real, trackPathExistence: false);
        }

        private PathExistence? TryEnumerateDirectoryWithFullGraph(EnumerationRequest request)
        {
            return TryEnumerateDirectory(request, FileSystemViewMode.FullGraph, trackPathExistence: false);
        }

        /// <summary>
        /// Attempts to enumerate a directory.
        /// </summary>
        /// <param name="request">
        /// Details of how to enumerate the directory.
        /// </param>
        /// <param name="mode">
        /// What BuildXL <see cref="FileSystemViewMode"/> to use.
        /// </param>
        /// <param name="trackPathExistence">
        /// Whether the existence of paths discovered during the enumeration should be tracked to optimize scheduling in future builds.
        /// </param>
        private PathExistence? TryEnumerateDirectory(EnumerationRequest request, FileSystemViewMode mode, bool trackPathExistence = true)
        {
            var directoryContents = GetEnumerationResult(request, mode, trackPathExistence: trackPathExistence);

            var path = request.DirectoryPath;
            var handleEntry = FilteredHandledEntry(path, request.HandleEntry);

            if (!directoryContents.IsValid)
            {
                Logger.Log.DirectoryFingerprintingFilesystemEnumerationFailed(Events.StaticContext, path.ToString(PathTable), "The error was given before");
                return null;
            }

            foreach (var tuple in directoryContents.Members)
            {
                handleEntry(tuple.Item1, tuple.Item2);
            }

            return directoryContents.Existence;
        }

        private DirectoryEnumerationResult GetEnumerationResult(EnumerationRequest request, FileSystemViewMode mode, bool trackPathExistence = true)
        {
            var path = request.DirectoryPath;
            var pathTable = Context.PathTable;

            Lazy<DirectoryEnumerationResult> lazyDirectoryContents;
            if (!request.CachedDirectoryContents.TryGetValue(path, out lazyDirectoryContents))
            {
                var fileSystemView = FileSystemView;
                lazyDirectoryContents = Lazy.Create(() =>
                {
                    List<DirectoryMemberEntry> members = null;
                    var existence = fileSystemView.TryEnumerateDirectory(path, mode, (name, childPath, childExistence) =>
                    {
                        members = members ?? new List<DirectoryMemberEntry>();
                        members.Add((childPath, name));
                    },
                    cachePathExistence: trackPathExistence);

                    var resultMembers = (IReadOnlyList<DirectoryMemberEntry>)members ?? CollectionUtilities.EmptyArray<DirectoryMemberEntry>();
                    return existence.Succeeded ? new DirectoryEnumerationResult(existence.Result, resultMembers) : DirectoryEnumerationResult.Invalid;
                });

                request.CachedDirectoryContents.AddItem(path, lazyDirectoryContents);
            }

            DirectoryEnumerationResult directoryContents = lazyDirectoryContents.Value;
            return directoryContents;
        }

        private PathExistence? EnumerateDirectoryWithMinimalPipGraph(EnumerationRequest request)
        {
            var path = request.DirectoryPath;
            var handleEntry = FilteredHandledEntry(path, request.HandleEntry);

            InitializePipFileSystem(request.PipInfo);

            return PipFileSystem.EnumerateDirectory(PathTable, path, handleEntry);
        }

        private void InitializePipFileSystem(CacheablePipInfo process)
        {
            if (PipFileSystem != null)
            {
                return;
            }

            m_pooledPipFileSystem = s_pool.GetInstance();

            PipFileSystem.Initialize(PathTable);

            foreach (var input in process.Dependencies)
            {
                PipFileSystem.AddPath(PathTable, input.Path);
            }

            foreach (var output in process.Outputs)
            {
                PipFileSystem.AddPath(PathTable, output.Path);
            }

            foreach (var directoryDependency in process.DirectoryDependencies)
            {
                PipFileSystem.AddSealDirectoryContents(m_env, directoryDependency);
            }
        }

        #endregion
    }
}
