// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Ipc;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Performance;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;
using Logger = BuildXL.Scheduler.Tracing.Logger;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Defines graph of pips and allows adding Pips with validation.
    /// </summary>
    public sealed partial class PipGraph
    {
        public class Builder : PipGraphBase, IPipGraphBuilder
        {
            /// <summary>
            /// Lazily initialized BuildXL server IPC moniker.
            /// </summary>
            private readonly Lazy<IIpcMoniker> m_lazyApiServerMoniker;

            /// <summary>
            /// Creates a new moniker if it hasn't already been created; otherwise returns the previously created one.
            /// </summary>
            public IIpcMoniker GetApiServerMoniker() => m_lazyApiServerMoniker.Value;

            private static readonly SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> s_emptySealContents =
                SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(
                    CollectionUtilities.EmptyArray<FileArtifact>(),
                    OrdinalFileArtifactComparer.Instance);

            private IScheduleConfiguration ScheduleConfiguration => m_configuration.Schedule;

            private readonly NodeId m_dummyHashSourceFileNode;

            private readonly IConfiguration m_configuration;

            private WindowsOsDefaults m_windowsOsDefaults;
            private MacOsDefaults m_macOsDefaults;
            private readonly object m_osDefaultLock = new object();

            #region State

            /// <summary>
            /// Set of those file artifacts (where <see cref="FileArtifact.IsOutputFile" />) for which there exists a consuming pip.
            /// Typically an artifact can be used as input multiple times, but this is disallowed for artifacts which are re-written
            /// (only the re-writer can consume the artifacts).
            /// </summary>
            /// <remarks>
            /// TODO: This may go away entirely when we support swapping in prior versions of an artifact (with optimistic locks) as in
            /// CloudMake.
            /// Maintained by <see cref="AddInput" /> and <see cref="AddOutput" />
            /// </remarks>
            private readonly ConcurrentBigSet<FileArtifact> m_outputFileArtifactsUsedAsInputs;

            /// <summary>
            ///     A multi-value map from service PipId to its client PipIds.
            /// </summary>
            private readonly ConcurrentBigMap<PipId, ConcurrentBigSet<PipId>> m_servicePipClients;

            /// <summary>
            ///     A mapping of Service PipId to corresponding Shutdown PipId (<see cref="BuildXL.Pips.Operations.Process.ShutdownProcessPipId"/>).
            /// </summary>
            private readonly ConcurrentBigMap<PipId, ServiceInfo> m_servicePipToServiceInfoMap;

            /// <summary>
            /// Set of temporary outputs.
            /// </summary>
            private readonly ConcurrentBigSet<FileArtifact> m_temporaryOutputFiles;

            /// <summary>
            /// A mapping of untracked paths or scopes to corresponding PipIds
            /// </summary>
            private readonly ConcurrentBigMap<AbsolutePath, PipId> m_untrackedPathsAndScopes;


            /// <summary>
            /// A mapping of source file to a corresponding PipId.
            /// A source file can be declared by multiple process pips, so the first producing pip processed is arbitrarily saved.
            /// </summary>
            private readonly ConcurrentBigMap<AbsolutePath, PipId> m_sourceFiles;

            /// <summary>
            /// Logging context used for error reporting during PipGraph construction.
            /// </summary>
            private LoggingContext LoggingContext { get; }

            /// <summary>
            /// Logger instance
            /// </summary>
            private Logger Logger { get; }

            /// <summary>
            /// Mutable version of the Dataflow graph. This will be null when the graph is cached.
            /// </summary>
            public readonly MutableDirectedGraph MutableDataflowGraph;

            /// <summary>
            /// Manages locking of paths and pips for pip graph and scheduler
            /// </summary>
            internal readonly LockManager LockManager;

            /// <summary>
            /// The names of temp environment variables
            /// </summary>
            private readonly HashSet<StringId> m_tempEnvironmentVariables;

            /// <summary>
            /// The immutable pip graph. This is stored to allow Build() to be called multiple times and return the same instance
            /// </summary>
            private PipGraph m_immutablePipGraph;

            /// <summary>
            /// Value indicating if the constructed graph is valid.
            /// </summary>
            private bool m_isValidConstructedGraph = true;

            /// <inheritdoc />
            [Pure]
            public bool IsImmutable => m_immutablePipGraph != null || !m_isValidConstructedGraph;

            private readonly PipGraphStaticFingerprints m_pipStaticFingerprints = new PipGraphStaticFingerprints();

            #endregion State

            /// <summary>
            /// Mapping of path names to <see cref="SealedDirectoryTable" />s representing the full / partial
            /// <see cref="SealDirectory" />
            /// pips rooted at particular paths.
            /// </summary>
            /// <remarks>
            /// Enumerating these mappings can be used to find seals containing particular files.
            /// Internaly exposes the seal directory table which is used by <see cref="PatchablePipGraph"/> to
            /// mark 'start' and 'finish' of graph patching.
            /// </remarks>
            internal SealedDirectoryTable SealDirectoryTable { get; }

            private readonly CounterCollection<PipGraphCounter> m_counters = new CounterCollection<PipGraphCounter>();

            private bool ShouldComputePipStaticFingerprints => ScheduleConfiguration.ComputePipStaticFingerprints;

            private readonly PipStaticFingerprinter m_pipStaticFingerprinter;

            /// <summary>
            /// Class constructor
            /// </summary>
            public Builder(
                PipTable pipTable,
                PipExecutionContext context,
                Logger logger,
                LoggingContext loggingContext,
                IConfiguration configuration,
                SemanticPathExpander semanticPathExpander,
                string fingerprintSalt = null,
                DirectoryMembershipFingerprinterRuleSet directoryMembershipFingerprinterRules = null)
                : base(pipTable, context, semanticPathExpander, new MutableDirectedGraph())
            {
                MutableDataflowGraph = (MutableDirectedGraph)DataflowGraph;
                Logger = logger;
                LoggingContext = loggingContext;

                m_tempEnvironmentVariables = new HashSet<StringId>(
                    BuildParameters
                        .DisallowedTempVariables
                        .Select(tmpVar => StringId.Create(Context.StringTable, tmpVar)));

                SealDirectoryTable = new SealedDirectoryTable(Context.PathTable);
                m_outputFileArtifactsUsedAsInputs = new ConcurrentBigSet<FileArtifact>();
                m_servicePipClients = new ConcurrentBigMap<PipId, ConcurrentBigSet<PipId>>();
                m_servicePipToServiceInfoMap = new ConcurrentBigMap<PipId, ServiceInfo>();
                m_temporaryOutputFiles = new ConcurrentBigSet<FileArtifact>();
                m_untrackedPathsAndScopes = new ConcurrentBigMap<AbsolutePath, PipId>();
                m_sourceFiles = new ConcurrentBigMap<AbsolutePath, PipId>();

                m_lazyApiServerMoniker = Lazy.Create(() => IpcFactory.GetProvider().CreateNewMoniker());

                LockManager = new LockManager();

                // Prime the dummy provenance since its creation requires adding a string to the TokenText table, which gets frozen after scheduling
                // is complete. GetDummyProvenance may be called during execution (after the schedule phase)
                GetDummyProvenance();

                m_configuration = configuration;

                var extraFingerprintSalts = new ExtraFingerprintSalts(
                    configuration,
                    PipFingerprintingVersion.TwoPhaseV2,
                    fingerprintSalt ?? string.Empty,
                    searchPathToolsHash: directoryMembershipFingerprinterRules?.ComputeSearchPathToolsHash());

                m_pipStaticFingerprinter = new PipStaticFingerprinter(
                    context.PathTable,
                    GetSealDirectoryFingerprint,
                    GetDirectoryProducerFingerprint,
                    extraFingerprintSalts,
                    semanticPathExpander)
                {
                    FingerprintTextEnabled = configuration.Schedule.LogPipStaticFingerprintTexts
                };

                m_dummyHashSourceFileNode = PipId.DummyHashSourceFilePipId.ToNodeId();
            }

            /// <summary>
            /// Marks the pip graph as complete and subsequently immutable.
            /// Following this, <see cref="IsImmutable"/> is set.
            /// </summary>
            public PipGraph Build()
            {
                using (LockManager.AcquireGlobalExclusiveLock())
                {
                    MutableDataflowGraph.Seal();

                    if (!IsImmutable)
                    {
                        StringId apiServerMonikerId = m_lazyApiServerMoniker.IsValueCreated
                            ? StringId.Create(Context.StringTable, m_lazyApiServerMoniker.Value.Id)
                            : StringId.Invalid;

                        var semistableProcessFingerprint =
                            PerformanceDataUtilities.ComputeGraphSemistableFingerprint(LoggingContext, PipTable, Context.PathTable);

                        var pipGraphState = new SerializedState(
                            values: Values,
                            specFiles: SpecFiles,
                            modules: Modules,
                            pipProducers: PipProducers,
                            opaqueDirectoryProducers: OutputDirectoryProducers,
                            outputDirectoryRoots: OutputDirectoryRoots,
                            compositeSharedOpaqueProducers: CompositeOutputDirectoryProducers,
                            sourceSealedDirectoryRoots: SourceSealedDirectoryRoots,
                            temporaryPaths: TemporaryPaths,
                            sealDirectoryNodes: SealDirectoryTable.FinishAndMarkReadOnly(),
                            rewritingPips: RewritingPips,
                            rewrittenPips: RewrittenPips,
                            latestWriteCountsByPath: LatestWriteCountsByPath,
                            servicePipClients: m_servicePipClients,
                            apiServerMoniker: apiServerMonikerId,

                            // If there are N paths in the path table (including AbsolutePath.Invalid), the path table count will be N and the value
                            // of the last added absolute path will be N - 1. Therefore, the max absolute path should be N - 1.
                            // Capture this here so we know that all paths < PathTable.Count are valid to use with serialized pip graph.
                            maxAbsolutePath: Context.PathTable.Count - 1,
                            semistableProcessFingerprint: semistableProcessFingerprint,
                            pipStaticFingerprints: m_pipStaticFingerprints);

                        m_immutablePipGraph = new PipGraph(
                            pipGraphState,
                            MutableDataflowGraph,
                            PipTable,
                            Context,
                            SemanticPathExpander);

                        if (!ScheduleConfiguration.UnsafeDisableGraphPostValidation && !IsValidGraph())
                        {
                            m_isValidConstructedGraph = false;
                            return null;
                        }

                        m_counters.LogAsStatistics("PipGraph.Builder", LoggingContext);
                    }
                }

                return m_immutablePipGraph;
            }

            /// <inheritdoc />
            public override NodeId GetSealedDirectoryNode(DirectoryArtifact directoryArtifact)
            {
                SealDirectoryTable.TryGetSealForDirectoryArtifact(directoryArtifact, out PipId pipId);
                return pipId.ToNodeId();
            }

            /// <inheritdoc />
            public bool ApplyCurrentOsDefaults(ProcessBuilder processBuilder)
            {
                if (OperatingSystemHelper.IsUnixOS)
                {
                    if (m_macOsDefaults == null)
                    {
                        lock(m_osDefaultLock)
                        {
                            if (m_macOsDefaults == null) 
                            {
                                m_macOsDefaults = new MacOsDefaults(Context.PathTable, this);
                            }
                        }
                    }

                    return m_macOsDefaults.ProcessDefaults(processBuilder);
                }
                else
                {
                    if (m_windowsOsDefaults == null)
                    {
                        lock(m_osDefaultLock)
                        {
                            if (m_windowsOsDefaults == null) 
                            {
                                m_windowsOsDefaults = new WindowsOsDefaults(Context.PathTable);
                            }
                        }
                    }

                    return m_windowsOsDefaults.ProcessDefaults(processBuilder);
                }
            }


            #region Validation

            private bool IsValidGraph()
            {
                Contract.Requires(LockManager.HasGlobalExclusiveAccess);

                using (m_counters.StartStopwatch(PipGraphCounter.GraphPostValidation))
                {
                    return ValidateSealDirectoryConstruction() && ValidateTempPaths();
                }
            }

            /// <summary>
            /// Validates no pip has declared artifacts in any declared temp directories.
            /// Temp directories are guaranteed cleaned before pips run, and non-deterministically cleaned after runs, so it is not safe to place anything required within temps.
            /// </summary>
            private bool ValidateTempPaths()
            {
                // Make sure no items will be added to graph so it's safe to iterate over concurrent set
                Contract.Requires(IsImmutable);

                var success = true;
                if (!ValidateTempPathsHelper(out var invalidTempPath, out var invalidTempProducerNode, out var invalidArtifactPath, out var invalidArtifactProducerNode))
                {
                    success = false;
                    LogTempValidationError(invalidTempPath, invalidTempProducerNode, invalidArtifactPath, invalidArtifactProducerNode);
                }

                return success;
            }

            private bool ValidateTempPathsHelper(out AbsolutePath invalidTempPath, out NodeId invalidTempProducerNode, out AbsolutePath invalidArtifactPath, out NodeId invalidArtifactProducerNode)
            {
                foreach (var kvp in TemporaryPaths)
                {
                    invalidTempPath = kvp.Key;
                    invalidTempProducerNode = kvp.Value.ToNodeId();
                    // Search for declared artifacts from the temp path down to make sure no pip declared an artifact within a temp directory
                    foreach (var childPathId in Context.PathTable.EnumerateHierarchyTopDown(invalidTempPath.Value).Concat(new HierarchicalNameId[] { invalidTempPath.Value }))
                    {
                        var childPath = new AbsolutePath(childPathId);
                        // Source files are tracked independently from other build artifacts to make sure the the process pip that declared a dependency on the source file is correctly associated
                        // Otherwise, it's possible that an intermediate HashSourceFile pip is reported back as the associated pip
                        if (m_sourceFiles.ContainsKey(childPath))
                        {
                            invalidArtifactPath = childPath;
                            var declaringNodeFound = m_sourceFiles.TryGetValue(childPath, out var invalidArtifactProducerPipId);
                            invalidArtifactProducerNode = declaringNodeFound ? invalidArtifactProducerPipId.ToNodeId() : NodeId.Invalid;
                            return false;
                        }
                        // If there is a producer for the temp's child path, then a pip in the build expects that path to exist as a build artifact, which makes this an invalid temp path
                        else if (TryFindProducerForPath(childPath, out invalidArtifactProducerNode) 
                            && !m_temporaryOutputFiles.Contains(FileArtifact.CreateOutputFile(childPath)) /* Temp files paths will return a producing node, so if the child path itself is a temp file, just skip it */)
                        {
                            invalidArtifactPath = childPath;
                            return false;
                        }
                    }
                }

                invalidTempPath = AbsolutePath.Invalid;
                invalidTempProducerNode = NodeId.Invalid;
                invalidArtifactPath = AbsolutePath.Invalid;
                invalidArtifactProducerNode = NodeId.Invalid;
                return true;
            }

            /// <summary>
            /// Logs a descriptive error when temp validation fails.
            /// </summary>
            private void LogTempValidationError(AbsolutePath invalidTempPath, NodeId invalidTempProducerNode, AbsolutePath invalidArtifactPath, NodeId invalidArtifactProducerNode)
            {
                // Overlapping paths found, log a descriptive error
                string artifactProducerNode = "Node not found";
                Location artifactProducerLocation = default;
                if (invalidArtifactProducerNode != NodeId.Invalid)
                {
                    var artifactProducerPipId = invalidArtifactProducerNode.ToPipId();
                    artifactProducerNode = m_immutablePipGraph.GetPipFromPipId(artifactProducerPipId).GetDescription(Context);
                    artifactProducerLocation = m_immutablePipGraph.GetPipFromPipId(artifactProducerPipId).Provenance.Token.ToLogLocation(Context.PathTable);
                }

                var fullArtifactPath = invalidArtifactPath.ToString(Context.PathTable);

                string tempProducerNode = "Node not found";
                Location tempProducerLocation = default;
                if (invalidTempProducerNode != NodeId.Invalid)
                {
                    var tempProducerPipId = invalidTempProducerNode.ToPipId();
                    tempProducerNode = m_immutablePipGraph.GetPipFromPipId(tempProducerPipId).GetDescription(Context);
                    tempProducerLocation = m_immutablePipGraph.GetPipFromPipId(tempProducerPipId).Provenance.Token.ToLogLocation(Context.PathTable);
                }

                var fullTempPath = invalidTempPath.ToString(Context.PathTable);

                Logger.Log.InvalidGraphSinceArtifactPathOverlapsTempPath(LoggingContext, tempProducerLocation, fullTempPath, tempProducerNode, artifactProducerLocation, fullArtifactPath, artifactProducerNode);
            }

            /// <summary>
            /// Given a path, tries to find a node in the build that produces that path as a build artifact.
            /// </summary>
            /// <param name="path">
            /// <see cref="AbsolutePath"/> of the path.
            /// </param>
            /// <param name="producerNode">
            /// If a producer node is found, the producer's Node ID; otherwise, <see cref="NodeId.Invalid"/>.
            /// </param>
            /// <returns>
            /// If a producer node is found, true; otherwise, false.
            /// </returns>
            private bool TryFindProducerForPath(AbsolutePath path, out NodeId producerNode)
            {
                producerNode = TryGetOriginalProducerForPath(path);

                // Try to look for shared opaque directory producer which are tracked separately
                if (producerNode == NodeId.Invalid)
                {
                    foreach (var kvp in SealDirectoryTable.GetSealedDirectories(path))
                    {
                        producerNode = kvp.Value.ToNodeId();
                    }
                }

                return producerNode != NodeId.Invalid;
            }

            private bool ValidateSealDirectoryConstruction()
            {
                Contract.Requires(LockManager.HasGlobalExclusiveAccess);

                var visitedOutputDirectories = new ConcurrentDictionary<HierarchicalNameId, bool>();
                var visitedSourceSealDirectories = new ConcurrentDictionary<HierarchicalNameId, bool>();
                var visitedFullSealDirectories = new ConcurrentDictionary<HierarchicalNameId, bool>();
                var queuePool = new ObjectPool<Queue<HierarchicalNameId>>(
                    () => new Queue<HierarchicalNameId>(),
                    queue => queue.Clear());
                int errorCount = 0;

                Parallel.ForEach(
                    m_immutablePipGraph.GetSealDirectoriesByKind(PipQueryContext.PipGraphPostValidation, kind => true).ToList(),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = ScheduleConfiguration.MaxProcesses,
                    },
                    sealDirectory =>
                    {
                        bool isValid;

                        switch (sealDirectory.Kind)
                        {
                            case SealDirectoryKind.Opaque:
                                isValid = EnsureOutputDirectoryDoesNotClashWithOtherArtifactsAndDoesNotHaveChild(queuePool, visitedOutputDirectories, sealDirectory);
                                break;
                            case SealDirectoryKind.SharedOpaque:
                                isValid = EnsureSharedOpaqueDirectoriesHaveNoDisallowedChildren(queuePool, visitedOutputDirectories, sealDirectory);
                                break;
                            case SealDirectoryKind.Full:
                                isValid = EnsureFullSealDirectoriesCoverAllPathsUnderneath(queuePool, visitedFullSealDirectories, sealDirectory);
                                break;
                            case SealDirectoryKind.SourceAllDirectories:
                            case SealDirectoryKind.SourceTopDirectoryOnly:
                                isValid = EnsureSourceSealDirectoryHasNoOutputs(queuePool, visitedSourceSealDirectories, sealDirectory);
                                break;
                            default:
                                return;
                        }

                        if (!isValid)
                        {
                            Interlocked.Increment(ref errorCount);
                        }
                    });

                return errorCount == 0;
            }

            /// <summary>
            /// A shared opaque directory is not allowed to contain:
            /// - exclusive (non-shared) opaque directories
            /// - fully sealed directories
            /// </summary>
            private bool EnsureSharedOpaqueDirectoriesHaveNoDisallowedChildren(
                ObjectPool<Queue<HierarchicalNameId>> queuePool,
                ConcurrentDictionary<HierarchicalNameId, bool> visited,
                SealDirectory sealDirectory)
            {
                Contract.Requires(queuePool != null);
                Contract.Requires(visited != null);
                Contract.Requires(sealDirectory != null);
                Contract.Requires(sealDirectory.Kind == SealDirectoryKind.SharedOpaque);

                // In case of a composite shared opaque, restrictions are checked for its elements
                // already
                if (sealDirectory.IsComposite)
                {
                    return true;
                }

                int errorCount = 0;
                var directory = sealDirectory.Directory;
                var outputDirectoryProducerNode = OutputDirectoryProducers[directory];
                var outputDirectoryProducer = PipTable.HydratePip(
                    outputDirectoryProducerNode.ToPipId(),
                    PipQueryContext.PipGraphPostValidation);
                var outputDirectoryProducerProvenance = outputDirectoryProducer.Provenance ?? GetDummyProvenance();

                AbsolutePath directoryPath = directory.Path;

                if (!visited.TryAdd(directory.Path.Value, true))
                {
                    return true;
                }

                errorCount = EnsureSharedOpaqueDirectoriesHaveNoExclusiveOpaquesNorFullySealedDirectories(
                    queuePool,
                    visited,
                    directory,
                    outputDirectoryProducerProvenance,
                    outputDirectoryProducer,
                    errorCount);

                return errorCount == 0;
            }

            /// <summary>
            /// Under a shared opaque we don't allow:
            /// - exclusive opaque directory (their delete-all-before-run semantics doesn't play well with sharing)
            /// - fully sealed directories (nothing under a fully sealed is supposed to change)
            /// </summary>
            /// <returns>Number of disallowed artifacts found under the given directory</returns>
            private int EnsureSharedOpaqueDirectoriesHaveNoExclusiveOpaquesNorFullySealedDirectories(
                ObjectPool<Queue<HierarchicalNameId>> queuePool,
                ConcurrentDictionary<HierarchicalNameId, bool> visited,
                DirectoryArtifact directory,
                PipProvenance outputDirectoryProducerProvenance,
                Pip outputDirectoryProducer,
                int errorCount)
            {
                if (PathIsExclusiveOpaqueOrFullySealed(directory.Path, directory, outputDirectoryProducerProvenance, outputDirectoryProducer))
                {
                    return 1;
                }

                using (var wrappedQueue = queuePool.GetInstance())
                {
                    var queue = wrappedQueue.Instance;

                    queue.Enqueue(directory.Path.Value);

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();

                        foreach (var child in Context.PathTable.EnumerateImmediateChildren(current))
                        {
                            var childAsPath = new AbsolutePath(child);
                            var childError = PathIsExclusiveOpaqueOrFullySealed(childAsPath, directory, outputDirectoryProducerProvenance, outputDirectoryProducer);

                            if (!childError && visited.TryAdd(child, true))
                            {
                                queue.Enqueue(child);
                            }

                            if (childError)
                            {
                                ++errorCount;
                            }
                        }
                    }
                }

                return errorCount;
            }

            private bool PathIsExclusiveOpaqueOrFullySealed(AbsolutePath path, DirectoryArtifact directory, PipProvenance outputDirectoryProducerProvenance, Pip outputDirectoryProducer)
            {
                foreach (var sealedDirectoryAndProducer in SealDirectoryTable.GetSealedDirectories(path))
                {
                    var directoryArtifact = sealedDirectoryAndProducer.Key;

                    // Exclusive opaque directories are blocked
                    if (directoryArtifact.IsOutputDirectory() && !directoryArtifact.IsSharedOpaque)
                    {
                        LogInvalidGraphSinceSharedOpaqueDirectoryContainsExclusiveOpaqueDirectory(
                            sealedDirectoryAndProducer,
                            directory,
                            outputDirectoryProducerProvenance,
                            outputDirectoryProducer,
                            path);
                        return true;
                    }

                    // Fully sealed directories are blocked (partial sealed are ok)
                    if (PipTable.GetSealDirectoryKind(sealedDirectoryAndProducer.Value) == SealDirectoryKind.Full)
                    {
                        LogInvalidGraphSinceOutputDirectoryContainsOrCoincidesSealedDirectory(
                            sealedDirectoryAndProducer,
                            directory,
                            outputDirectoryProducerProvenance,
                            outputDirectoryProducer);
                        return true;
                    }
                }

                return false;
            }

            private void LogInvalidGraphSinceOutputDirectoryContainsOrCoincidesSealedDirectory(
                KeyValuePair<DirectoryArtifact, PipId> sealedDirectoryAndProducer,
                DirectoryArtifact outputDirectory,
                PipProvenance outputDirectoryProducerProvenance,
                Pip outputDirectoryProducer)
            {
                if (!OutputDirectoryProducers.TryGetValue(sealedDirectoryAndProducer.Key, out var producerChildNode))
                {
                    producerChildNode = sealedDirectoryAndProducer.Value.ToNodeId();
                }

                var sealedDirectoryProducer = PipTable.HydratePip(
                    producerChildNode.ToPipId(),
                    PipQueryContext.PipGraphPostValidation);

                if (sealedDirectoryAndProducer.Key.Path == outputDirectory.Path)
                {
                    Logger.Log.ScheduleFailInvalidGraphSinceOutputDirectoryCoincidesSealedDirectory(
                        LoggingContext,
                        outputDirectoryProducerProvenance.Token.Path.ToString(Context.PathTable),
                        outputDirectoryProducerProvenance.Token.Line,
                        outputDirectoryProducerProvenance.Token.Position,
                        outputDirectory.Path.ToString(Context.PathTable),
                        outputDirectoryProducer.GetDescription(Context),
                        sealedDirectoryAndProducer.Key.Path.ToString(Context.PathTable),
                        sealedDirectoryProducer.GetDescription(Context));
                }
                else
                {
                    Logger.Log.ScheduleFailInvalidGraphSinceOutputDirectoryContainsSealedDirectory(
                        LoggingContext,
                        outputDirectoryProducerProvenance.Token.Path.ToString(Context.PathTable),
                        outputDirectoryProducerProvenance.Token.Line,
                        outputDirectoryProducerProvenance.Token.Position,
                        outputDirectory.Path.ToString(Context.PathTable),
                        outputDirectoryProducer.GetDescription(Context),
                        sealedDirectoryAndProducer.Key.Path.ToString(Context.PathTable),
                        sealedDirectoryProducer.GetDescription(Context));
                }
            }

            private void LogInvalidGraphSinceSharedOpaqueDirectoryContainsExclusiveOpaqueDirectory(
                KeyValuePair<DirectoryArtifact, PipId> exclusiveOpaqueDirectoryAndProducer,
                DirectoryArtifact directory,
                PipProvenance sharedOpaqueProducerProvenance,
                Pip sharedOpaqueProducer,
                AbsolutePath childAsPath)
            {
                // Error because the shared opaque directory contains an exclusive opaque directory.
                if (!OutputDirectoryProducers.TryGetValue(exclusiveOpaqueDirectoryAndProducer.Key, out var exclusiveOpaqueProducerChildNode))
                {
                    exclusiveOpaqueProducerChildNode = exclusiveOpaqueDirectoryAndProducer.Value.ToNodeId();
                }

                var exclusiveOpaqueProducer = PipTable.HydratePip(
                    exclusiveOpaqueProducerChildNode.ToPipId(),
                    PipQueryContext.PipGraphPostValidation);

                Logger.Log.ScheduleFailInvalidGraphSinceSharedOpaqueDirectoryContainsExclusiveOpaqueDirectory(
                    LoggingContext,
                    sharedOpaqueProducerProvenance.Token.Path.ToString(Context.PathTable),
                    sharedOpaqueProducerProvenance.Token.Line,
                    sharedOpaqueProducerProvenance.Token.Position,
                    directory.Path.ToString(Context.PathTable),
                    sharedOpaqueProducer.GetDescription(Context),
                    childAsPath.ToString(Context.PathTable),
                    exclusiveOpaqueProducer.GetDescription(Context));
            }

            private bool EnsureOutputDirectoryDoesNotClashWithOtherArtifactsAndDoesNotHaveChild(
                ObjectPool<Queue<HierarchicalNameId>> queuePool,
                ConcurrentDictionary<HierarchicalNameId, bool> visited,
                SealDirectory sealDirectory)
            {
                Contract.Requires(queuePool != null);
                Contract.Requires(visited != null);
                Contract.Requires(sealDirectory != null);
                Contract.Requires(sealDirectory.Kind == SealDirectoryKind.Opaque);

                int errorCount = 0;
                var directory = sealDirectory.Directory;
                var outputDirectoryProducerNode = OutputDirectoryProducers[directory];
                var outputDirectoryProducer = PipTable.HydratePip(
                    outputDirectoryProducerNode.ToPipId(),
                    PipQueryContext.PipGraphPostValidation);
                var outputDirectoryProducerProvenance = outputDirectoryProducer.Provenance ?? GetDummyProvenance();

                using (var wrappedQueue = queuePool.GetInstance())
                {
                    var queue = wrappedQueue.Instance;

                    if (visited.TryAdd(directory.Path.Value, true))
                    {
                        queue.Enqueue(directory.Path.Value);
                    }

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();

                        bool currentError = false;
                        var currentAsPath = new AbsolutePath(current);

                        NodeId producerChildNode;
                        int latestRewriteCount;
                        if (LatestWriteCountsByPath.TryGetValue(currentAsPath, out latestRewriteCount))
                        {
                            for (int i = 0; i <= latestRewriteCount; ++i)
                            {
                                // Validate against all possible rewrite counts because outputs with different rewrite counts
                                // are generated by different pips.
                                var currentAsFile = new FileArtifact(currentAsPath, i);

                                if (PipProducers.TryGetValue(currentAsFile, out producerChildNode))
                                {
                                    if (currentAsFile.Path == directory.Path)
                                    {
                                        // Error because output directory coincides with a file.
                                        if (currentAsFile.IsSourceFile)
                                        {
                                            Logger.Log.ScheduleFailInvalidGraphSinceOutputDirectoryCoincidesSourceFile(
                                                LoggingContext,
                                                outputDirectoryProducerProvenance.Token.Path.ToString(Context.PathTable),
                                                outputDirectoryProducerProvenance.Token.Line,
                                                outputDirectoryProducerProvenance.Token.Position,
                                                directory.Path.ToString(Context.PathTable),
                                                outputDirectoryProducer.GetDescription(Context),
                                                currentAsPath.ToString(Context.PathTable));
                                        }
                                        else
                                        {
                                            // Error because the output directory contains an output, but the output file has a different
                                            // producer from the producer of the output directory itself.
                                            var outputFileProducer = PipTable.HydratePip(
                                                producerChildNode.ToPipId(),
                                                PipQueryContext.PipGraphPostValidation);

                                            Logger.Log.ScheduleFailInvalidGraphSinceOutputDirectoryCoincidesOutputFile(
                                                LoggingContext,
                                                outputDirectoryProducerProvenance.Token.Path.ToString(Context.PathTable),
                                                outputDirectoryProducerProvenance.Token.Line,
                                                outputDirectoryProducerProvenance.Token.Position,
                                                directory.Path.ToString(Context.PathTable),
                                                outputDirectoryProducer.GetDescription(Context),
                                                currentAsPath.ToString(Context.PathTable),
                                                outputFileProducer.GetDescription(Context));
                                        }

                                        currentError = true;
                                        break;
                                    }

                                    if (currentAsFile.IsSourceFile)
                                    {
                                        // Error because the output directory contains a source file.
                                        Logger.Log.ScheduleFailInvalidGraphSinceOutputDirectoryContainsSourceFile(
                                            LoggingContext,
                                            outputDirectoryProducerProvenance.Token.Path.ToString(Context.PathTable),
                                            outputDirectoryProducerProvenance.Token.Line,
                                            outputDirectoryProducerProvenance.Token.Position,
                                            directory.Path.ToString(Context.PathTable),
                                            outputDirectoryProducer.GetDescription(Context),
                                            currentAsPath.ToString(Context.PathTable));

                                        currentError = true;
                                    }
                                    else if (producerChildNode != outputDirectoryProducerNode)
                                    {
                                        // Error because the output directory contains an output, but the output file has a different
                                        // producer from the producer of the output directory itself.
                                        var outputFileProducer = PipTable.HydratePip(
                                            producerChildNode.ToPipId(),
                                            PipQueryContext.PipGraphPostValidation);

                                        Logger.Log.ScheduleFailInvalidGraphSinceOutputDirectoryContainsOutputFile(
                                            LoggingContext,
                                            outputDirectoryProducerProvenance.Token.Path.ToString(Context.PathTable),
                                            outputDirectoryProducerProvenance.Token.Line,
                                            outputDirectoryProducerProvenance.Token.Position,
                                            directory.Path.ToString(Context.PathTable),
                                            outputDirectoryProducer.GetDescription(Context),
                                            currentAsPath.ToString(Context.PathTable),
                                            outputFileProducer.GetDescription(Context));

                                        currentError = true;
                                    }
                                }
                            }
                        }

                        foreach (var sealedDirectoryAndProducer in SealDirectoryTable.GetSealedDirectories(currentAsPath))
                        {
                            if (sealedDirectoryAndProducer.Key != directory)
                            {
                                LogInvalidGraphSinceOutputDirectoryContainsOrCoincidesSealedDirectory(
                                    sealedDirectoryAndProducer,
                                    directory,
                                    outputDirectoryProducerProvenance,
                                    outputDirectoryProducer);

                                currentError = true;
                            }
                        }

                        if (!currentError)
                        {
                            foreach (var child in Context.PathTable.EnumerateImmediateChildren(current))
                            {
                                if (visited.TryAdd(child, true))
                                {
                                    queue.Enqueue(child);
                                }
                            }
                        }

                        if (currentError)
                        {
                            ++errorCount;
                        }
                    }
                }

                return errorCount == 0;
            }

            private bool EnsureSourceSealDirectoryHasNoOutputs(
                ObjectPool<Queue<HierarchicalNameId>> queuePool,
                ConcurrentDictionary<HierarchicalNameId, bool> visited,
                SealDirectory sealDirectory)
            {
                Contract.Requires(queuePool != null);
                Contract.Requires(visited != null);
                Contract.Requires(sealDirectory != null);
                Contract.Requires(sealDirectory.IsSealSourceDirectory);

                int errorCount = 0;
                var directory = sealDirectory.Directory;
                var sealDirectoryProvenance = sealDirectory.Provenance ?? GetDummyProvenance();

                using (var wrappedQueue = queuePool.GetInstance())
                {
                    var queue = wrappedQueue.Instance;

                    if (visited.TryAdd(directory.Path.Value, true))
                    {
                        queue.Enqueue(directory.Path.Value);
                    }

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();

                        bool currentError = false;
                        var currentAsPath = new AbsolutePath(current);

                        NodeId producerChildNode;
                        int latestRewriteCount;
                        if (LatestWriteCountsByPath.TryGetValue(currentAsPath, out latestRewriteCount))
                        {
                            var currentAsFile = new FileArtifact(currentAsPath, latestRewriteCount);

                            if (currentAsFile.IsSourceFile)
                            {
                                if (currentAsFile.Path == directory.Path)
                                {
                                    Logger.Log.ScheduleFailInvalidGraphSinceSourceSealedDirectoryCoincidesSourceFile(
                                        LoggingContext,
                                        sealDirectoryProvenance.Token.Path.ToString(Context.PathTable),
                                        sealDirectoryProvenance.Token.Line,
                                        sealDirectoryProvenance.Token.Position,
                                        directory.Path.ToString(Context.PathTable),
                                        currentAsPath.ToString(Context.PathTable));

                                    currentError = true;
                                }
                            }
                            else
                            {
                                LogScheduleFailInvalidGraphSinceSourceSealedDirectoryCoincidesOrContainsOutputFile(
                                    directory,
                                    sealDirectoryProvenance,
                                    currentAsFile);
                                currentError = true;
                            }
                        }

                        foreach (var sealedDirectoryAndProducer in SealDirectoryTable.GetSealedDirectories(currentAsPath))
                        {
                            // Error because the output directory contains a sealed directory, that can potentially be another output directory.
                            if (OutputDirectoryProducers.TryGetValue(sealedDirectoryAndProducer.Key, out producerChildNode))
                            {
                                var sealedDirectoryProducer = PipTable.HydratePip(
                                    producerChildNode.ToPipId(),
                                    PipQueryContext.PipGraphPostValidation);

                                if (sealedDirectoryAndProducer.Key.Path == directory.Path)
                                {
                                    Logger.Log.ScheduleFailInvalidGraphSinceOutputDirectoryCoincidesSealedDirectory(
                                        LoggingContext,
                                        sealedDirectoryProducer.Provenance.Token.Path.ToString(Context.PathTable),
                                        sealedDirectoryProducer.Provenance.Token.Line,
                                        sealedDirectoryProducer.Provenance.Token.Position,
                                        sealedDirectoryAndProducer.Key.Path.ToString(Context.PathTable),
                                        sealedDirectoryProducer.GetDescription(Context),
                                        directory.Path.ToString(Context.PathTable),
                                        sealDirectory.GetDescription(Context));
                                }
                                else
                                {
                                    Logger.Log.ScheduleFailInvalidGraphSinceSourceSealedDirectoryContainsOutputDirectory(
                                        LoggingContext,
                                        sealDirectoryProvenance.Token.Path.ToString(Context.PathTable),
                                        sealDirectoryProvenance.Token.Line,
                                        sealDirectoryProvenance.Token.Position,
                                        directory.Path.ToString(Context.PathTable),
                                        currentAsPath.ToString(Context.PathTable),
                                        sealedDirectoryProducer.GetDescription(Context));
                                }

                                currentError = true;
                            }
                        }

                        if (!currentError)
                        {
                            foreach (var child in Context.PathTable.EnumerateImmediateChildren(current))
                            {
                                if (visited.TryAdd(child, true))
                                {
                                    queue.Enqueue(child);
                                }
                            }
                        }

                        if (currentError)
                        {
                            ++errorCount;
                        }
                    }
                }

                return errorCount == 0;
            }

            private NodeId LogScheduleFailInvalidGraphSinceSourceSealedDirectoryCoincidesOrContainsOutputFile(
                DirectoryArtifact directory,
                PipProvenance sealDirectoryProvenance,
                FileArtifact outputFile)
            {
                NodeId producerChildNode;
                bool getProducer = PipProducers.TryGetValue(outputFile, out producerChildNode);
                Contract.Assert(getProducer);

                var outputFileProducer = PipTable.HydratePip(
                    producerChildNode.ToPipId(),
                    PipQueryContext.PipGraphPostValidation);

                if (outputFile.Path == directory.Path)
                {
                    Logger.Log.ScheduleFailInvalidGraphSinceSourceSealedDirectoryCoincidesOutputFile(
                        LoggingContext,
                        sealDirectoryProvenance.Token.Path.ToString(Context.PathTable),
                        sealDirectoryProvenance.Token.Line,
                        sealDirectoryProvenance.Token.Position,
                        directory.Path.ToString(Context.PathTable),
                        outputFile.Path.ToString(Context.PathTable),
                        outputFileProducer.GetDescription(Context));
                }
                else
                {
                    Logger.Log.ScheduleFailInvalidGraphSinceSourceSealedDirectoryContainsOutputFile(
                        LoggingContext,
                        sealDirectoryProvenance.Token.Path.ToString(Context.PathTable),
                        sealDirectoryProvenance.Token.Line,
                        sealDirectoryProvenance.Token.Position,
                        directory.Path.ToString(Context.PathTable),
                        outputFile.Path.ToString(Context.PathTable),
                        outputFileProducer.GetDescription(Context));
                }

                return producerChildNode;
            }

            private bool EnsureFullSealDirectoriesCoverAllPathsUnderneath(
                ObjectPool<Queue<HierarchicalNameId>> queuePool,
                ConcurrentDictionary<HierarchicalNameId, bool> visited,
                SealDirectory sealDirectory)
            {
                Contract.Requires(queuePool != null);
                Contract.Requires(visited != null);
                Contract.Requires(sealDirectory != null);
                Contract.Requires(sealDirectory.Kind == SealDirectoryKind.Full);

                int errorCount = 0;

                var directory = sealDirectory.Directory;
                var sealDirectoryProvenance = sealDirectory.Provenance ?? GetDummyProvenance();

                using (var wrappedQueue = queuePool.GetInstance())
                using (var wrappedSet = Pools.GetFileArtifactSet())
                {
                    var queue = wrappedQueue.Instance;

                    if (visited.TryAdd(directory.Path.Value, true))
                    {
                        queue.Enqueue(directory.Path.Value);
                    }

                    HashSet<FileArtifact> fullSealContents = wrappedSet.Instance;
                    foreach (var item in sealDirectory.Contents)
                    {
                        fullSealContents.Add(item);
                    }

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();

                        foreach (var child in Context.PathTable.EnumerateImmediateChildren(current))
                        {
                            bool childError = false;
                            var childAsPath = new AbsolutePath(child);

                            int latestRewriteCount;
                            if (!LatestWriteCountsByPath.TryGetValue(childAsPath, out latestRewriteCount))
                            {
                                // Since paths that get validated are created by enumerating the path table, intermediate directory paths
                                // will be included in addition to file paths. Those intermediate directories won't have any producing
                                // pip and should therefore be omitted from the validation, but we need to keep following down to their children.
                                //
                                // For example:
                                //      SealedDirectory: c:\foo
                                //      Files:           c:\foo\1.txt, c:\foo\bar\2.txt
                                // c:\foo\bar will be a path that gets checked since it exists in the PathTable. But that's a directory
                                // so it shouldn't flag a warning about not having a fully specified sealed directory.
                                if (visited.TryAdd(child, true))
                                {
                                    queue.Enqueue(child);
                                }

                                continue;
                            }

                            var childAsLatestFileVersion = new FileArtifact(childAsPath, latestRewriteCount);

                            if (!fullSealContents.Contains(childAsLatestFileVersion))
                            {
                                NodeId pipReferencingUnsealedFile;

                                if (!PipProducers.TryGetValue(childAsLatestFileVersion, out pipReferencingUnsealedFile))
                                {
                                    Contract.Assume(false, "Should have found a producer for the referenced path.");
                                }

                                if (pipReferencingUnsealedFile.IsValid)
                                {
                                    var pip = PipTable.HydratePip(
                                        pipReferencingUnsealedFile.ToPipId(),
                                        PipQueryContext.PipGraphPostValidation);

                                    // Ignore this for Source files, they should be okay.
                                    if (pip.PipType != PipType.HashSourceFile)
                                    {
                                        Logger.Log.InvalidGraphSinceFullySealedDirectoryIncomplete(
                                            LoggingContext,
                                            sealDirectoryProvenance.Token.Path.ToString(Context.PathTable),
                                            sealDirectoryProvenance.Token.Line,
                                            sealDirectoryProvenance.Token.Position,
                                            directory.Path.ToString(Context.PathTable),
                                            pip.GetDescription(Context),
                                            childAsPath.ToString(Context.PathTable));

                                        childError = true;
                                    }
                                }
                            }

                            if (!childError && visited.TryAdd(child, true))
                            {
                                queue.Enqueue(child);
                            }

                            if (childError)
                            {
                                ++errorCount;
                            }
                        }
                    }
                }

                return errorCount == 0;
            }

            private bool IsValidIpc(IpcPip ipcPip, LockManager.PathAccessGroupLock pathAccessLock)
            {
                var semanticPathExpander = SemanticPathExpander.GetModuleExpander(ipcPip.Provenance.ModuleId);

                if (ipcPip.FileDependencies.Any(f => !IsValidInputFileArtifact(pathAccessLock, f, ipcPip, semanticPathExpander)))
                {
                    return false;
                }

                if (!CheckServicePipDependencies(ipcPip.ServicePipDependencies))
                {
                    Logger.Log.ScheduleFailAddPipDueToInvalidServicePipDependency(
                        LoggingContext,
                        file: string.Empty,
                        line: 0,
                        column: 0,
                        pipSemiStableHash: ipcPip.MessageBody.GetHashCode(),
                        pipDescription: ipcPip.GetDescription(Context),
                        pipValueId: ipcPip.PipId.ToString());
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Validates process pip.
            /// </summary>
            private bool IsValidProcess(
                Process process,
                LockManager.PathAccessGroupLock pathAccessLock,
                out Dictionary<AbsolutePath, FileArtifact> dependenciesByPath,
                out Dictionary<AbsolutePath, FileArtifact> outputsByPath)
            {
                Contract.Requires(process != null, "Argument process cannot be null");

                var semanticPathExpander = SemanticPathExpander.GetModuleExpander(process.Provenance.ModuleId);
                dependenciesByPath = new Dictionary<AbsolutePath, FileArtifact>(process.Dependencies.Length);
                outputsByPath = new Dictionary<AbsolutePath, FileArtifact>(process.FileOutputs.Length);
                var outputDirectorySet = new HashSet<AbsolutePath>();


                // Process dependencies.
                foreach (FileArtifact dependency in process.Dependencies)
                {
                    if (!dependenciesByPath.TryGetValue(dependency.Path, out FileArtifact existingDependencyOnPath))
                    {
                        if (!IsValidInputFileArtifact(pathAccessLock, dependency, process, semanticPathExpander))
                        {
                            return false;
                        }

                        dependenciesByPath.Add(dependency.Path, dependency);
                    }
                    else
                    {
                        Contract.Assume(existingDependencyOnPath != dependency, "Should not contain duplicates");
                        LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidInputDueToMultipleConflictingRewriteCounts, process, dependency);
                        return false;
                    }
                }

                Contract.Assert(dependenciesByPath.ContainsKey(process.Executable.Path), "Dependency set must contain the executable.");
                Contract.Assert(
                    !process.StandardInput.IsFile || dependenciesByPath.ContainsKey(process.StandardInput.File.Path),
                    "Dependency set must contain the standard input.");

                foreach (PipId pipId in process.OrderDependencies)
                {
                    if (!PipTable.IsValid(pipId))
                    {
                        Contract.Assume(false, "Invalid pip id");
                        return false;
                    }
                }

                // Process outputs

                // Every pip must have at least one output artifact
                if (process.FileOutputs.Length == 0 && process.DirectoryOutputs.Length == 0)
                {
                    LogEventWithPipProvenance(Logger.ScheduleFailAddProcessPipProcessDueToNoOutputArtifacts, process);
                    return false;
                }

                // Temp file outputs are included in FileOutputs (whereas temp directories are excluded from DirectoryOutputs),
                // so its possible that the length of FileOutputs only reflects temp files.
                // If there are no DirectoryOutputs, make sure there is at least one FileOutput that is NOT temporary.
                bool hasOneRequiredOutput = process.DirectoryOutputs.Length > 0;

                foreach (FileArtifactWithAttributes outputWithAttributes in process.FileOutputs)
                {
                    FileArtifact output = outputWithAttributes.ToFileArtifact();
                    if (!outputsByPath.TryGetValue(output.Path, out FileArtifact existingOutputToPath))
                    {
                        if (!dependenciesByPath.TryGetValue(output.Path, out FileArtifact correspondingInput))
                        {
                            correspondingInput = FileArtifact.Invalid;
                        }

                        if (!IsValidOutputFileArtifact(pathAccessLock, output, correspondingInput, process, semanticPathExpander))
                        {
                            return false;
                        }

                        outputsByPath.Add(output.Path, output);
                    }
                    else
                    {
                        Contract.Assume(existingOutputToPath != output, "Should not contain duplicates");

                        LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidOutputDueToMultipleConflictingRewriteCounts, process, output);

                        return false;
                    }

                    hasOneRequiredOutput |= !outputWithAttributes.IsTemporaryOutputFile;
                }

                if (!hasOneRequiredOutput)
                {
                    LogEventWithPipProvenance(Logger.ScheduleFailAddProcessPipProcessDueToNoOutputArtifacts, process);
                    return false;
                }

                foreach (var inputDirectory in process.DirectoryDependencies)
                {
                    if (!SealDirectoryTable.TryGetSealForDirectoryArtifact(inputDirectory, out _))
                    {
                        LogEventWithPipProvenance(
                            Logger.SourceDirectoryUsedAsDependency,
                            process,
                            inputDirectory.Path);
                        return false;
                    }
                }

                foreach (var directory in process.DirectoryOutputs)
                {
                    if (outputsByPath.ContainsKey(directory.Path))
                    {
                        LogEventWithPipProvenance(
                            Logger.ScheduleFailAddPipInvalidOutputSinceOutputIsBothSpecifiedAsFileAndDirectory,
                            process,
                            directory.Path);
                        return false;
                    }

                    if (!IsValidOutputDirectory(directory, process, semanticPathExpander))
                    {
                        return false;
                    }

                    outputDirectorySet.Add(directory.Path);
                }

                // TODO: no explicit inputs are allowed in OD dependencies.

                // Validate temp directory environment variables
                if (process.EnvironmentVariables.IsValid)
                {
                    foreach (EnvironmentVariable environmentVariable in process.EnvironmentVariables)
                    {
                        if (m_tempEnvironmentVariables.Contains(environmentVariable.Name))
                        {
                            if (!ValidateTempDirectory(process, environmentVariable, semanticPathExpander))
                            {
                                return false;
                            }
                        }
                    }
                }

                if (!CheckServicePipDependencies(process.ServicePipDependencies))
                {
                    LogEventWithPipProvenance(Logger.ScheduleFailAddPipDueToInvalidServicePipDependency, process);
                    return false;
                }

                if (process.PreserveOutputWhitelist.IsValid && process.PreserveOutputWhitelist.Length > 0)
                {
                    if (!process.AllowPreserveOutputs)
                    {
                        LogEventWithPipProvenance(Logger.ScheduleFailAddPipDueToInvalidAllowPreserveOutputsFlag, process);
                        return false;
                    }

                    foreach (var whitelistPath in process.PreserveOutputWhitelist)
                    {
                        if (!outputsByPath.ContainsKey(whitelistPath) && !outputDirectorySet.Contains(whitelistPath))
                        {
                            LogEventWithPipProvenance(Logger.ScheduleFailAddPipDueToInvalidPreserveOutputWhitelist, process);
                            return false;
                        }
                    }
                }

                Contract.Assert(
                    !process.StandardOutput.IsValid || outputsByPath.ContainsKey(process.StandardOutput.Path),
                    "Output set must contain the standard output file, if specified.");
                Contract.Assert(
                    !process.StandardError.IsValid || outputsByPath.ContainsKey(process.StandardError.Path),
                    "Output set must contain the standard error file, if specified.");

                return true;
            }

            private bool CheckServicePipDependencies(ReadOnlyArray<PipId> servicePipDependencies)
            {
                Contract.Requires(servicePipDependencies.IsValid);

                // check service pip dependencies are service pips (and have already been added)
                foreach (PipId servicePipId in servicePipDependencies)
                {
                    if (!m_servicePipToServiceInfoMap.ContainsKey(servicePipId))
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// Checks if copy-file pip is valid.
            /// </summary>
            private bool IsValidCopyFile(CopyFile copyFile, LockManager.PathAccessGroupLock pathAccessLock)
            {
                Contract.Requires(copyFile != null, "Argument copyFile cannot be null");

                var semanticPathExpander = SemanticPathExpander.GetModuleExpander(copyFile.Provenance.ModuleId);

                if (copyFile.Source.Path == copyFile.Destination.Path)
                {
                    LogEventWithPipProvenance(Logger.ScheduleFailAddCopyFilePipDueToSameSourceAndDestinationPath, copyFile, copyFile.Destination);
                    return false;
                }

                if (!IsValidInputFileArtifact(pathAccessLock, copyFile.Source, copyFile, semanticPathExpander))
                {
                    return false;
                }

                if (!IsValidOutputFileArtifact(pathAccessLock, copyFile.Destination, FileArtifact.Invalid, copyFile, semanticPathExpander))
                {
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Checks if write-file pip is valid.
            /// </summary>
            private bool IsValidWriteFile(WriteFile writeFile, LockManager.PathAccessGroupLock pathAccessLock)
            {
                Contract.Requires(writeFile != null, "Argument writeFile cannot be null");

                var semanticPathExpander = SemanticPathExpander.GetModuleExpander(writeFile.Provenance.ModuleId);

                if (!IsValidOutputFileArtifact(pathAccessLock, writeFile.Destination, FileArtifact.Invalid, writeFile, semanticPathExpander))
                {
                    return false;
                }

                // It doesn't make much sense to allow WriteFile to rewrite things. WriteFile pips have no dependencies, so there's no
                // way to constrain their scheduling (a rewriting WriteFile would run as soon as the previous version was written,
                // so why write the previous version at all?). Note that this is not true for CopyFile, since it has an input edge.
                if (writeFile.Destination.RewriteCount != 1)
                {
                    LogEventWithPipProvenance(Logger.ScheduleFailAddWriteFilePipSinceOutputIsRewritten, writeFile, writeFile.Destination);
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Checks if a given file artifact is a valid source file artifact.
            /// </summary>
            /// <param name="pathAccessLock">the access lock acquired by the enclosing operation for read access to the file</param>
            /// <param name="input">Artifact that has been specified as an input of the pip</param>
            /// <param name="pip">The pip which has specified the given output</param>
            /// <param name="semanticPathExpander">The semantic path expander for the pip</param>
            /// <remarks>
            /// The path read lock must be held when calling this method.
            /// </remarks>
            private bool IsValidInputFileArtifact(LockManager.PathAccessGroupLock pathAccessLock, FileArtifact input, Pip pip, SemanticPathExpander semanticPathExpander)
            {
                Contract.Requires(pathAccessLock.HasReadAccess(input.Path));
                Contract.Requires(input.IsValid, "Argument input must be a valid file artifact");
                Contract.Requires(pip != null, "Argument pip cannot be null");

                SemanticPathInfo semanticPathInfo = semanticPathExpander.GetSemanticPathInfo(input);
                if (semanticPathInfo.IsValid && !semanticPathInfo.IsReadable)
                {
                    LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidInputUnderNonReadableRoot, pip, input, semanticPathInfo.Root);
                    return false;
                }

                /*/* TODO:410334 - Current builds have lots of source files being incorrectly registered under output mounts.
                if (semanticPathInfo.IsValid && semanticPathInfo.IsScrubbable && input.IsSourceFile)
                {
                    LogEventWithPipProvenance(Events.Log.ScheduleFailAddPipInvalidSourceInputUnderScrubbableRoot, pip, input, semanticPathInfo.Root);
                    return false;
                }*/

                FileArtifact latestExistingArtifact = TryGetLatestFileArtifactForPath(input.Path);
                bool hasBeenUsed = latestExistingArtifact.IsValid;

                if (hasBeenUsed)
                {
                    if (input.IsSourceFile && !latestExistingArtifact.IsSourceFile)
                    {
                        PipId latestProducerId = PipProducers[latestExistingArtifact].ToPipId();
                        Pip latestProducer = PipTable.HydratePip(latestProducerId, PipQueryContext.PipGraphIsValidInputFileArtifact1);
                        LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidInputSincePathIsWrittenAndThusNotSource, pip, latestProducer, input);
                        return false;
                    }

                    if (IsTemporaryOutput(latestExistingArtifact))
                    {
                        // Output artifact should not be temporary output of the pip
                        PipId latestProducerId = PipProducers[latestExistingArtifact].ToPipId();
                        Pip latestProducer = PipTable.HydratePip(latestProducerId, PipQueryContext.PipGraphIsValidInputFileArtifact2);
                        LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidInputSinceCorespondingOutputIsTemporary, pip, latestProducer, input);
                        return false;
                    }

                    if (latestExistingArtifact != input)
                    {
                        PipId latestProducerId = PipProducers[latestExistingArtifact].ToPipId();
                        Pip latestProducer = PipTable.HydratePip(latestProducerId, PipQueryContext.PipGraphIsValidInputFileArtifact3);
                        LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidInputSinceInputIsRewritten, pip, latestProducer, input);
                        return false;
                    }
                }
                else
                {
                    Contract.Assume(
                        !input.IsOutputFile,
                        "Output artifact has no producer. This should be impossible by construction, since creating an output file artifact is supposed to require scheduling a producer.");
                }

                return true;
            }

            /// <summary>
            /// Returns true if specified <paramref name="fileArtifact" /> was used as a temporary output artifact by
            /// one of the pips.
            /// </summary>
            private bool IsTemporaryOutput(FileArtifact fileArtifact)
            {
                return fileArtifact.IsValid && m_temporaryOutputFiles.Contains(fileArtifact);
            }

            /// <summary>
            /// Verifies that the given path is under a writable root
            /// </summary>
            private static bool IsWritablePath(AbsolutePath path, SemanticPathExpander semanticPathExpander, out SemanticPathInfo semanticPathInfo)
            {
                semanticPathInfo = semanticPathExpander.GetSemanticPathInfo(path);
                return !semanticPathInfo.IsValid || semanticPathInfo.IsWritable;
            }

            /// <summary>
            /// Validates that a temp directory environment variable value is a valid path and is under  a writable root
            /// </summary>
            private bool ValidateTempDirectory(Pip pip, in EnvironmentVariable tempEnvironmentVariable, SemanticPathExpander semanticPathExpander)
            {
                AbsolutePath path;
                string pathString = tempEnvironmentVariable.Value.ToString(Context.PathTable);
                if (!AbsolutePath.TryCreate(Context.PathTable, pathString, out path))
                {
                    LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidTempDirectoryInvalidPath, pip, pathString, tempEnvironmentVariable.Name);
                    return false;
                }

                SemanticPathInfo semanticPathInfo;
                if (!IsWritablePath(path, semanticPathExpander, out semanticPathInfo))
                {
                    LogEventWithPipProvenance(
                        Logger.ScheduleFailAddPipInvalidTempDirectoryUnderNonWritableRoot,
                        pip,
                        path,
                        semanticPathInfo.Root,
                        tempEnvironmentVariable.Name);
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Checks if a given file artifact is a valid output file artifact.
            /// </summary>
            /// <param name="pathAccessLock">the access lock acquired by the enclosing operation for write access to the file</param>
            /// <param name="output">Artifact that has been specified as an output of the pip</param>
            /// <param name="correspondingInput">An artifact with the same path that is used as input to the pip (if present)</param>
            /// <param name="pip">The pip which has specified the given output</param>
            /// <param name="semanticPathExpander">the semantic path information for the pip</param>
            /// <remarks>
            /// The path write lock must be held when calling this method.
            /// </remarks>
            private bool IsValidOutputFileArtifact(
                LockManager.PathAccessGroupLock pathAccessLock,
                FileArtifact output,
                FileArtifact correspondingInput,
                Pip pip,
                SemanticPathExpander semanticPathExpander)
            {
                Contract.Requires(pathAccessLock.HasWriteAccess(output.Path));
                Contract.Requires(output.IsValid, "Argument output must be a valid file artifact");
                Contract.Requires(pip != null, "Argument pip cannot be null");

                if (!output.IsOutputFile)
                {
                    Contract.Assume(output.IsSourceFile);
                    LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidOutputSinceOutputIsSource, pip, output);
                    return false;
                }

                SemanticPathInfo semanticPathInfo;
                if (!IsWritablePath(output.Path, semanticPathExpander, out semanticPathInfo))
                {
                    LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidOutputUnderNonWritableRoot, pip, output, semanticPathInfo.Root);
                    return false;
                }

                // We cannot schedule new writes to a directory which has been fully sealed (this is dual to requiring an exhaustive set
                // of contents when sealing the directory in the first place).
                // This doesn't mean that an opaque directory can't have an explicit output. Such an explicit output is allowed so long as
                // both the opaque directory and the explicit output is produced by the same pip.
                // Note that we don't allow an explicit output under any kind of source sealed directory. Producing such an output
                // can alter the membership of the directory.
                // Shared opaque directories are an exception: any pip can write declared outputs under any shared opaque directory. This
                // is mainly because shared opaque directories are not really 'sealed' beyond dynamically observed writes that constitutes
                // a version of the content of the directory attributed to a given pip
                if (IsPathInsideFullySealedDirectory(output, pip))
                {
                    return false;
                }

                FileArtifact latestExistingArtifact = TryGetLatestFileArtifactForPath(output.Path);
                bool hasBeenUsed = latestExistingArtifact.IsValid;

                if ((hasBeenUsed && latestExistingArtifact.IsSourceFile) || (correspondingInput.IsValid && correspondingInput.IsSourceFile))
                {
                    // Can't rewrite a source file.
                    // TODO:[3089]: We should instead detect this error by enforcing that source files never occur under the output root,
                    //              and that outputs only occur under the output root. Though note that that would
                    //              break the QuickBuild + MSBuild + CoreXT case where most teams at MSFT
                    //              build outputs into $(OutDir) within the enlistment, at least for local
                    //              builds. For cloud builds the outdir is virtualized and breaking on
                    //              writes to the enlistment make sense.
                    LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidOutputSinceOutputIsSource, pip, output);
                    return false;
                }

                if (hasBeenUsed && latestExistingArtifact.IsOutputFile)
                {
                    // The output artifact refers to a path which has already been written (possibly with a different rewrite count).
                    // The following cases are for validating rewrite chains, to the extent we support them.
                    NodeId producingNodeId = PipProducers[latestExistingArtifact];
                    PipId producingPipId = producingNodeId.ToPipId();

                    if (correspondingInput.IsValid)
                    {
                        if (correspondingInput.RewriteCount + 1 != output.RewriteCount)
                        {
                            // We don't allow time-travel when rewriting (e.g. using version N as input to generate version N + 2).
                            LogEventWithPipProvenance(Logger.ScheduleFailAddPipRewrittenOutputMismatchedWithInput, pip, output);
                            return false;
                        }
                    }

                    if (latestExistingArtifact.RewriteCount == 1 && output.RewriteCount == 1)
                    {
                        // Simple double write.
                        Pip producingPip = PipTable.HydratePip(producingPipId, PipQueryContext.PipGraphIsValidOutputFileArtifactRewrite1);
                        LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidOutputDueToSimpleDoubleWrite, pip, producingPip, output);
                        return false;
                    }

                    if (latestExistingArtifact.RewriteCount >= output.RewriteCount)
                    {
                        // Can only rewrite the latest version.
                        Pip producingPip = PipTable.HydratePip(producingPipId, PipQueryContext.PipGraphIsValidOutputFileArtifactRewrite2);
                        LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidOutputSinceRewritingOldVersion, pip, producingPip, output);
                        return false;
                    }

                    if ((output.RewriteCount - latestExistingArtifact.RewriteCount) > 1)
                    {
                        // We skipped a version, so the created pip invented a write count somehow.
                        LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidOutputSinceOutputHasUnexpectedlyHighWriteCount, pip, output);
                        return false;
                    }

                    if (m_outputFileArtifactsUsedAsInputs.Contains(latestExistingArtifact))
                    {
                        // Only the final output of a rewrite chain can be used as an input to arbitrary pips (can't swap in old versions).
                        PipId sealingPipId = SealDirectoryTable.TryFindSealDirectoryPipContainingFileArtifact(PipTable, latestExistingArtifact);

                        if (!sealingPipId.IsValid)
                        {
                            // TODO: Would be nice to indicate the related consumer here
                            LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidOutputSincePreviousVersionUsedAsInput, pip, output);
                        }
                        else
                        {
                            var sealingPip =
                                (SealDirectory)PipTable.HydratePip(sealingPipId, PipQueryContext.PipGraphIsValidOutputFileArtifactSealing2);
                            LogEventWithPipProvenance(
                                Logger.ScheduleFailAddPipInvalidOutputSinceFileHasBeenPartiallySealed,
                                pip,
                                sealingPip,
                                output,
                                sealingPip.Directory);
                        }

                        return false;
                    }

                    Pip latestProducingPip = PipTable.HydratePip(producingPipId, PipQueryContext.PipGraphIsValidOutputFileArtifactRewrite3);
                    Process latestProducingProcess = latestProducingPip as Process;
                    Process currentProducingProcess = pip as Process;

                    if ((latestProducingProcess != null && latestProducingProcess.AllowPreserveOutputs)
                        || (currentProducingProcess != null && currentProducingProcess.AllowPreserveOutputs))
                    {
                        // Log for rewriting preserved output.
                        LogEventWithPipProvenance(Logger.ScheduleAddPipInvalidOutputDueToRewritingPreservedOutput, pip, latestProducingPip, output);
                    }
                }
                else
                {
                    // Here, we've established that this artifact has not been used before at all, so we need only perform validation for single-writes.
                    if (output.RewriteCount > 1)
                    {
                        // This path has not been seen before, yet we see a write-count greater than one. Versions are missing.
                        LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidOutputSinceOutputHasUnexpectedlyHighWriteCount, pip, output);
                        return false;
                    }
                }

                Contract.Assert(latestExistingArtifact.RewriteCount + 1 == output.RewriteCount);

                return true;
            }

            /// <summary>
            /// Checks that no reads/writes are performed inside a fully sealed directory.
            /// </summary>
            /// <remarks>
            /// Shared opaque directories are not considered fully sealed directory
            /// </remarks>
            /// <param name="path">File/Directory artifact to check.</param>
            /// <param name="pip">Producing pip (used for error reporting only).</param>
            private bool IsPathInsideFullySealedDirectory(AbsolutePath path, Pip pip)
            {
                DirectoryArtifact fullSealArtifact = SealDirectoryTable.TryFindFullySealedDirectoryArtifactForFile(path);
                if (fullSealArtifact.IsValid && !fullSealArtifact.IsSharedOpaque)
                {
                    SealDirectoryTable.TryGetSealForDirectoryArtifact(fullSealArtifact, out PipId sealingPipId);
                    Contract.Assert(sealingPipId.IsValid);

                    Pip sealingPip = PipTable.HydratePip(sealingPipId, PipQueryContext.PipGraphIsValidOutputFileArtifactSealing1);
                    LogEventWithPipProvenance(
                        Logger.ScheduleFailAddPipInvalidOutputSinceDirectoryHasBeenSealed,
                        pip,
                        sealingPip,
                        FileArtifact.CreateSourceFile(path),
                        fullSealArtifact.Path);

                    return true;
                }

                return false;
            }

            /// <summary>
            /// Checks to see if this is a valid output directory.
            /// </summary>
            /// <remarks>
            /// - An output directory must have not been produced by another pip.
            /// - An output directory must be under a writable mount
            /// </remarks>
            private bool IsValidOutputDirectory(DirectoryArtifact directory, Pip pip, SemanticPathExpander semanticPathExpander)
            {
                NodeId producingPipNode;

                // An output directory must have not been produced by another pip.
                if (OutputDirectoryProducers.TryGetValue(directory, out producingPipNode))
                {
                    Pip producingPip = PipTable.HydratePip(producingPipNode.ToPipId(), PipQueryContext.PipGraphIsValidOutputDirectory1);
                    LogEventWithPipProvenance(
                        Logger.ScheduleFailAddPipInvalidOutputSinceDirectoryHasBeenProducedByAnotherPip,
                        pip,
                        producingPip,
                        directory.Path);

                    return false;
                }

                // An output directory must be under a writable mount
                if (!IsWritablePath(directory.Path, semanticPathExpander, out var semanticPathInfo))
                {
                    LogEventWithPipProvenance(Logger.ScheduleFailAddPipInvalidOutputUnderNonWritableRoot, pip, directory.Path, semanticPathInfo.Root);
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Checks to see if this is a valid dynamic seal directory.
            /// </summary>
            /// <remarks>
            /// A directory sealed dynamically must not exist within another fully sealed or output directory. They may not overlap with another dynamically sealed directory.
            /// </remarks>
            private bool IsValidSealedOutputDirectory(DirectoryArtifact directory, Pip pip)
            {
                DirectoryArtifact fullSealArtifact = SealDirectoryTable.TryFindFullySealedContainingDirectoryArtifact(directory);

                if (fullSealArtifact.IsValid)
                {
                    SealDirectoryTable.TryGetSealForDirectoryArtifact(fullSealArtifact, out PipId sealingPipId);
                    Contract.Assert(sealingPipId.IsValid);

                    Pip sealingPip = PipTable.HydratePip(sealingPipId, PipQueryContext.PipGraphIsValidOutputFileArtifactSealing1);
                    LogEventWithPipProvenance(
                        Logger.ScheduleFailAddPipInvalidOutputSinceDirectoryHasBeenSealed,
                        pip,
                        sealingPip,
                        FileArtifact.CreateSourceFile(directory.Path),
                        fullSealArtifact.Path);
                    return false;
                }

                return true;
            }

            #endregion Validation

            #region Pip Addition

            private NodeId CreateNodeForPip(Pip pip)
            {
                Contract.Requires(!IsImmutable);

                NodeId node = MutableDataflowGraph.CreateNode();
                PipId pipId = PipTable.Add(node.Value, pip);
                Contract.Assert(pipId.ToNodeId() == node);

                return node;
            }

            /// <summary>
            /// Adds a copy-file pip into this schedule.
            /// </summary>
            public bool AddCopyFile(CopyFile copyFile, PipId valuePipId = default)
            {
                Contract.Requires(copyFile != null, "Argument copyFile cannot be null");
                Contract.Assert(!IsImmutable);

                using (LockManager.PathAccessGroupLock pathAccessLock = LockManager.AcquirePathAccessLock(copyFile))
                {
                    if (PipExists(copyFile))
                    {
                        return true;
                    }

                    if (!IsValidCopyFile(copyFile, pathAccessLock))
                    {
                        return false;
                    }

                    // Possibly create a source pip (note that all nodes are created in a topological order).
                    if (copyFile.Source.IsSourceFile)
                    {
                        EnsurePipExistsForSourceArtifact(pathAccessLock, copyFile.Source);
                    }

                    NodeId copyFileNode = CreateNodeForPip(copyFile);
                    AddOutput(pathAccessLock, copyFileNode, copyFile.Destination.WithAttributes(FileExistence.Required));
                    using (var edgeScope = MutableDataflowGraph.AcquireExclusiveIncomingEdgeScope(copyFileNode))
                    {
                        AddInput(pathAccessLock, copyFileNode, copyFile.Source, edgeScope: edgeScope);
                    }

                    // Link to value pip
                    if (valuePipId.IsValid)
                    {
                        AddPipProducerConsumerDependency(copyFileNode, valuePipId.ToNodeId(), ignoreTopologicalCheck: true);
                    }
                    else
                    {
                        AddValueDependency(copyFileNode, copyFile.Provenance);
                    }
                }

                ComputeAndStorePipStaticFingerprint(copyFile);

                return true;
            }

            /// <summary>
            /// Adds a write-file pip into this schedule.
            /// </summary>
            public bool AddWriteFile(WriteFile writeFile, PipId valuePipId = default)
            {
                Contract.Requires(writeFile != null, "Argument writeFile cannot be null");
                Contract.Assert(!IsImmutable);

                using (LockManager.PathAccessGroupLock pathAccessLock = LockManager.AcquirePathAccessLock(writeFile))
                {
                    if (PipExists(writeFile))
                    {
                        return true;
                    }

                    if (!IsValidWriteFile(writeFile, pathAccessLock))
                    {
                        return false;
                    }

                    NodeId writeFileNode = CreateNodeForPip(writeFile);

                    AddOutput(pathAccessLock, writeFileNode, writeFile.Destination.WithAttributes(FileExistence.Required));

                    // Link to value pip
                    if (valuePipId.IsValid)
                    {
                        AddPipProducerConsumerDependency(writeFileNode, valuePipId.ToNodeId(), ignoreTopologicalCheck: true);
                    }
                    else
                    {
                        AddValueDependency(writeFileNode, writeFile.Provenance);
                    }
                }

                ComputeAndStorePipStaticFingerprint(writeFile);

                return true;
            }

            /// <summary>
            /// Add a process pip into this schedule.
            /// </summary>
            public bool AddProcess(Process process, PipId valuePipId = default)
            {
                Contract.Requires(process != null, "Argument process cannot be null");
                Contract.Assert(!IsImmutable);

                using (LockManager.PathAccessGroupLock pathAccessLock = LockManager.AcquirePathAccessLock(process))
                {
                    if (PipExists(process))
                    {
                        return true;
                    }

                    Dictionary<AbsolutePath, FileArtifact> dependenciesByPath;
                    Dictionary<AbsolutePath, FileArtifact> outputsByPath;

                    if (!IsValidProcess(process, pathAccessLock, out dependenciesByPath, out outputsByPath))
                    {
                        return false;
                    }

                    // CreateSourceFile all needed source pips (note that all nodes are created in a topological order).
                    foreach (FileArtifact dependency in process.Dependencies)
                    {
                        if (dependency.IsSourceFile)
                        {
                            EnsurePipExistsForSourceArtifact(pathAccessLock, dependency);
                        }
                    }

                    // Sets the PipId, references to process.PipId should not be made before here
                    NodeId processNode = CreateNodeForPip(process);

                    var edgeScope = process.IsStartOrShutdownKind || process.ServiceInfo?.Kind == ServicePipKind.ServiceFinalization
                        ? null
                        : MutableDataflowGraph.AcquireExclusiveIncomingEdgeScope(processNode);
                    using (edgeScope)
                    {
                        // Process dependencies.
                        foreach (FileArtifact dependency in process.Dependencies)
                        {
                            if (dependency.IsSourceFile)
                            {
                                m_sourceFiles.TryAdd(dependency.Path, process.PipId);
                            }
                            AddInput(pathAccessLock, processNode, dependency, edgeScope: edgeScope);
                        }

                        // Process order dependencies.
                        foreach (PipId orderDependency in process.OrderDependencies)
                        {
                            AddPipToPipDependency(processNode, orderDependency, edgeScope);
                        }

                        // Process service dependencies.
                        foreach (PipId serviceDependency in process.ServicePipDependencies)
                        {
                            ProcessServicePipDependency(process.PipId, processNode, serviceDependency, edgeScope);
                        }

                        foreach (DirectoryArtifact directoryDependency in process.DirectoryDependencies)
                        {
                            AddDirectoryInput(pathAccessLock, processNode, directoryDependency, edgeScope);
                        }

                        // Process outputs.
                        foreach (FileArtifactWithAttributes output in process.FileOutputs)
                        {
                            AddOutput(pathAccessLock, processNode, output, edgeScope);

                            FileArtifact rewrittenInput;
                            if (dependenciesByPath.TryGetValue(output.Path, out rewrittenInput))
                            {
                                RewritingPips.Add(process.PipId);

                                NodeId rewrittenProducer;
                                if (PipProducers.TryGetValue(rewrittenInput, out rewrittenProducer))
                                {
                                    RewrittenPips.Add(rewrittenProducer.ToPipId());
                                }
                            }
                        }

                        // Process temp directories.
                        if (process.TempDirectory.IsValid)
                        {
                            TemporaryPaths.TryAdd(process.TempDirectory, process.PipId);
                        }

                        foreach (var tempDirectory in process.AdditionalTempDirectories)
                        {
                            TemporaryPaths.TryAdd(tempDirectory, process.PipId);
                        }
                    }

                    foreach (var directory in process.DirectoryOutputs)
                    {
                        OutputDirectoryProducers.Add(directory, processNode);
                        OutputDirectoryRoots.AddOrUpdate(
                            key: directory.Path,
                            data: directory.IsSharedOpaque,
                            addValueFactory: (p, isShared) => isShared,
                            updateValueFactory: (p, isShared, oldValue) => oldValue || isShared);
                    }

                    // Link to value pip
                    if (valuePipId.IsValid)
                    {
                        AddPipProducerConsumerDependency(processNode, valuePipId.ToNodeId(), ignoreTopologicalCheck: true);
                    }
                    else
                    {
                        AddValueDependency(processNode, process.Provenance);
                    }

                    // If this is a service pip, remember its ServiceInfo
                    if (process.IsService)
                    {
                        m_servicePipToServiceInfoMap[process.PipId] = process.ServiceInfo;
                    }

                    // Collect all untracked paths and scopes
                    foreach (var untrackedPath in process.UntrackedPaths)
                    {
                        m_untrackedPathsAndScopes[untrackedPath] = process.PipId;
                    }

                    foreach (var untrackedScope in process.UntrackedScopes)
                    {
                        m_untrackedPathsAndScopes[untrackedScope] = process.PipId;
                    }
                }

                ComputeAndStorePipStaticFingerprint(process);

                // Seal output directories unless we are patching graph (in which case
                // the patching procedure will add those SealDirectory pips)
                if (!SealDirectoryTable.IsPatching)
                {
                    for (int i = 0; i < process.DirectoryOutputs.Length; i++)
                    {
                        var directory = process.DirectoryOutputs[i];
                        if (!SealDirectoryOutput(process, directory, directoryOutputsIndex: i))
                        {
                            return false;
                        }
                    }

                    // Re-validate to ensure that sealed directory outputs are not within each other.
                    foreach (var directory in process.DirectoryOutputs)
                    {
                        if (!IsValidSealedOutputDirectory(directory, process))
                        {
                            return false;
                        }
                    }

                    // Shared opaque validations
                    if (process.DirectoryOutputs.Length != 0)
                    {
                        var sharedOpaqueDirectories =
                            process.DirectoryOutputs
                                .Where(directoryArtifact => directoryArtifact.IsSharedOpaque)
                                .Select(directoryArtifact => directoryArtifact.Path)
                                .ToReadOnlySet();

                        // Validate that shared directories coming from the same pip are not pairwise nested
                        foreach (var sharedOpaqueDirectory in sharedOpaqueDirectories)
                        {
                            if (!IsValidSharedOpaqueDirectory(sharedOpaqueDirectory, process, sharedOpaqueDirectories))
                            {
                                return false;
                            }
                        }

                        if (sharedOpaqueDirectories.Count > 0 && m_configuration.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs != PreserveOutputsMode.Disabled)
                        {
                            LogEventWithPipProvenance(
                                Logger.PreserveOutputsDoNotApplyToSharedOpaques,
                                process);
                        }
                    }
                }

                return true;
            }

            /// <summary>
            /// Validates that there are no pair of shared opaque directories declared in the same pip that are nested within in each other.
            /// </summary>
            private bool IsValidSharedOpaqueDirectory(AbsolutePath sharedOpaqueDirectory, Process processPip, IReadOnlySet<AbsolutePath> sharedOpaqueDirectories)
            {
                // We start the search from the parent of the shared opaque directory, if that exists
                var parentSharedOpaqueDirectory = sharedOpaqueDirectory.GetParent(Context.PathTable);
                if (!parentSharedOpaqueDirectory.IsValid)
                {
                    return true;
                }

                foreach (var current in Context.PathTable.EnumerateHierarchyBottomUp(parentSharedOpaqueDirectory.Value))
                {
                    var parentAsPath = new AbsolutePath(current);
                    if (sharedOpaqueDirectories.Contains(parentAsPath))
                    {
                        LogEventWithPipProvenance(
                            Logger.ScheduleFailAddPipInvalidSharedOpaqueDirectoryDueToOverlap,
                            processPip,
                            sharedOpaqueDirectory,
                            parentAsPath);
                        return false;
                    }
                }

                return true;
            }

            public bool AddIpcPip(IpcPip ipcPip, PipId valuePipId = default)
            {
                Contract.Requires(ipcPip != null, "Argument pip cannot be null");
                using (LockManager.PathAccessGroupLock pathAccessLock = LockManager.AcquirePathAccessLock(ipcPip))
                {
                    if (PipExists(ipcPip))
                    {
                        return true;
                    }

                    if (!IsValidIpc(ipcPip, pathAccessLock))
                    {
                        return false;
                    }

                    // ensure HashSourceFile pips exists for source dependencies
                    foreach (FileArtifact dependency in ipcPip.FileDependencies)
                    {
                        if (dependency.IsSourceFile)
                        {
                            EnsurePipExistsForSourceArtifact(pathAccessLock, dependency);
                        }
                    }

                    NodeId node = CreateNodeForPip(ipcPip);

                    var edgeScope = ipcPip.IsServiceFinalization ? null : MutableDataflowGraph.AcquireExclusiveIncomingEdgeScope(node);
                    using (edgeScope)
                    {
                        // process file dependencies
                        foreach (FileArtifact dependency in ipcPip.FileDependencies)
                        {
                            AddInput(pathAccessLock, node, dependency, edgeScope: edgeScope);
                        }

                        // process service dependencies.
                        foreach (PipId serviceDependency in ipcPip.ServicePipDependencies)
                        {
                            ProcessServicePipDependency(ipcPip.PipId, node, serviceDependency, edgeScope: edgeScope);
                        }

                        foreach (DirectoryArtifact directoryDependency in ipcPip.DirectoryDependencies)
                        {
                            AddDirectoryInput(pathAccessLock, node, directoryDependency, edgeScope);
                        }

                        // process output file
                        AddOutput(pathAccessLock, node, FileArtifactWithAttributes.FromFileArtifact(ipcPip.OutputFile, FileExistence.Required), edgeScope: edgeScope);
                    }

                    // Link to value pip
                    if (valuePipId.IsValid)
                    {
                        AddPipProducerConsumerDependency(node, valuePipId.ToNodeId(), ignoreTopologicalCheck: true);
                    }
                    else
                    {
                        AddValueDependency(node, ipcPip.Provenance);
                    }
                }

                return true;
            }

            /// <summary>
            /// Records the edge between the pip and the value associated with the provenance
            /// </summary>
            private void AddValueDependency(NodeId node, PipProvenance provenance)
            {
                Contract.Requires(!IsImmutable);
                Contract.Requires(node.IsValid);
                Contract.Requires(provenance != null);

                var tupleKey = (provenance.OutputValueSymbol, provenance.QualifierId, provenance.Token.Path);
                NodeId valueNode;
                bool nodeFound = Values.TryGetValue(tupleKey, out valueNode);

                // TODO: Reconcile this. I cannot enable this contract assumption.
                // Contract.Assume(nodeFound, "Must have had the value node already registered for this pip. This should have been done when the PipConstructionHelper was created.");
                if (nodeFound)
                {
                    AddPipProducerConsumerDependency(node, valueNode, ignoreTopologicalCheck: true);
                }
            }

            /// <inheritdoc />
            public bool AddOutputValue(ValuePip value)
            {
                Contract.Requires(value != null, "Argument outputValue cannot be null");
                Contract.Assert(!IsImmutable);

                using (LockManager.AcquireGlobalSharedLockIfApplicable())
                {
                    if (PipExists(value))
                    {
                        Contract.Assume(
                            false,
                            "Output values should only be evaluated once per qualifier and be added " +
                            "before any value to value dependencies. Therefore adding a ValuePip for an output should never collide");
                    }

                    var tupleKey = (value.Symbol, value.Qualifier, value.SpecFile.Path);
                    NodeId valueNode = Values.GetOrAdd(tupleKey, (value, this), (key, data) => CreateValuePip(data)).Item.Value;

                    // Find parent specfile node
                    NodeId specFileNode;
                    bool specFileNodeFound = SpecFiles.TryGetValue(value.SpecFile, out specFileNode);
                    if (!specFileNodeFound)
                    {
                        string valueFullName = value.Symbol.ToString(Context.SymbolTable);
                        string owningSpecFile = value.SpecFile.Path.ToString(Context.PathTable);

                        Contract.Assert(
                            false,
                            I($"Missing owning specfile node '{owningSpecFile}' for this value '{valueFullName}'. Did you call AddSpecFile properly?"));
                    }

                    AddPipProducerConsumerDependency(valueNode, specFileNode, ignoreTopologicalCheck: true);
                }

                return true;
            }

            /// <inheritdoc />
            public bool AddValueValueDependency(in ValuePip.ValueDependency valueDependency)
            {
                Contract.Requires(valueDependency.ParentIdentifier.IsValid);
                Contract.Requires(valueDependency.ChildIdentifier.IsValid);
                Contract.Assert(!IsImmutable);

                using (LockManager.AcquireGlobalSharedLockIfApplicable())
                {
                    var parentTuple = (
                        valueDependency.ParentIdentifier,
                        valueDependency.ParentQualifier,
                        valueDependency.ParentLocation.Path);
                    var childTuple = (
                        valueDependency.ChildIdentifier,
                        valueDependency.ChildQualifier,
                        valueDependency.ChildLocation.Path);

                    NodeId parentNode =
                        Values.GetOrAdd(
                            parentTuple,
                            (valueDependency.ParentLocation, this),
                            (key, data) => CreateValuePip(key, data)).Item.Value;
                    NodeId childNode =
                        Values.GetOrAdd(
                            childTuple,
                            (valueDependency.ChildLocation, this),
                            (key, data) => CreateValuePip(key, data)).Item.Value;
                    AddPipProducerConsumerDependency(childNode, parentNode, ignoreTopologicalCheck: true);
                }

                return true;
            }

            private static NodeId CreateValuePip((ValuePip valuePip, Builder builder) data)
            {
                return data.builder.CreateNodeForPip(data.valuePip);
            }

            private static NodeId CreateValuePip((FullSymbol symbol, QualifierId qualifierId, AbsolutePath path) key, in (LocationData location, Builder builder) data)
            {
                return CreateValuePip((new ValuePip(key.symbol, key.qualifierId, data.location), data.builder));
            }

            /// <inheritdoc />
            public bool AddSpecFile(SpecFilePip specFile)
            {
                Contract.Requires(specFile != null, "Argument specFile cannot be null");
                Contract.Assert(!IsImmutable);

                using (LockManager.AcquireGlobalSharedLockIfApplicable())
                {
                    if (PipExists(specFile))
                    {
                        return true;
                    }

                    if (SpecFiles.ContainsKey(specFile.SpecFile))
                    {
                        // Caller is responsible for handling and reporting this failure.
                        return false;
                    }

                    NodeId specFileNode = CreateNodeForPip(specFile);
                    SpecFiles.Add(specFile.SpecFile, specFileNode);

                    // Find the parent module
                    NodeId owningModuleNode;
                    bool parentModuleFound = Modules.TryGetValue(specFile.OwningModule, out owningModuleNode);

                    if (!parentModuleFound)
                    {
                        var specFilePath = specFile.SpecFile.Path.ToString(Context.PathTable);
                        Contract.Assert(
                            false,
                            I($"Missing owning module for this specfile '{specFilePath}'. Did you call AddModule properly?"));
                    }

                    AddPipProducerConsumerDependency(specFileNode, owningModuleNode, ignoreTopologicalCheck: true);
                }

                return true;
            }

            /// <inheritdoc />
            public bool AddModule(ModulePip module)
            {
                Contract.Requires(module != null, "Argument module cannot be null");
                Contract.Assert(!IsImmutable);

                using (LockManager.AcquireGlobalSharedLockIfApplicable())
                {
                    if (PipExists(module))
                    {
                        return true;
                    }

                    if (Modules.ContainsKey(module.Module))
                    {
                        // Caller is responsible for handling and reporting this failure.
                        return false;
                    }

                    NodeId moduleNode = CreateNodeForPip(module);
                    Modules.Add(module.Module, moduleNode);
                }

                return true;
            }

            /// <inheritdoc />
            public bool AddModuleModuleDependency(ModuleId moduleId, ModuleId dependency)
            {
                Contract.Assert(!IsImmutable);

                using (LockManager.AcquireGlobalSharedLockIfApplicable())
                {
                    NodeId moduleNode;
                    if (!Modules.TryGetValue(moduleId, out moduleNode))
                    {
                        // Caller is responsible for handling and reporting this failure.
                        return false;
                    }

                    NodeId dependencyNode;
                    if (!Modules.TryGetValue(dependency, out dependencyNode))
                    {
                        // Caller is responsible for handling and reporting this failure.
                        return false;
                    }

                    AddPipProducerConsumerDependency(dependencyNode, moduleNode, ignoreTopologicalCheck: true);
                }

                return true;
            }

            /// <inheritdoc/>
            public DirectoryArtifact ReserveSharedOpaqueDirectory(AbsolutePath directoryArtifactRoot)
            {
                return SealDirectoryTable.CreateSharedOpaqueDirectoryWithNewSealId(directoryArtifactRoot);
            }

            /// <inheritdoc />
            public DirectoryArtifact AddSealDirectory(SealDirectory sealDirectory, PipId valuePipId = default)
            {
                Contract.Requires(sealDirectory != null);
                Contract.Assert(!IsImmutable);

                var semanticPathExpander = SemanticPathExpander.GetModuleExpander(sealDirectory.Provenance.ModuleId);

                AbsolutePath root = sealDirectory.DirectoryRoot;
                if (sealDirectory.Kind.IsSourceSeal())
                {
                    var semanticPathInfo = semanticPathExpander.GetSemanticPathInfo(root);
                    if (!semanticPathInfo.IsValid)
                    {
                        LogEventWithPipProvenance(
                            Logger.ScheduleFailAddPipInvalidSealDirectorySourceNotUnderMount,
                            sealDirectory,
                            root);
                        return DirectoryArtifact.Invalid;
                    }

                    if (!semanticPathInfo.IsReadable)
                    {
                        LogEventWithPipProvenance(
                            Logger.ScheduleFailAddPipInvalidSealDirectorySourceNotUnderReadableMount,
                            sealDirectory,
                            root,
                            semanticPathInfo.Root,
                            semanticPathInfo.RootName.StringId);
                        return DirectoryArtifact.Invalid;
                    }
                }

                foreach (FileArtifact artifact in sealDirectory.Contents)
                {
                    if (!artifact.IsValid || !artifact.Path.IsWithin(Context.PathTable, root))
                    {
                        LogEventWithPipProvenance(
                            Logger.ScheduleFailAddPipInvalidSealDirectoryContentSinceNotUnderRoot,
                            sealDirectory,
                            artifact,
                            root);
                        return DirectoryArtifact.Invalid;
                    }
                }

                DirectoryArtifact artifactForNewSeal;

                using (LockManager.PathAccessGroupLock pathAccessLock = LockManager.AcquirePathAccessLock(sealDirectory))
                {
                    Contract.Assume(!PipExists(sealDirectory), "Attempted to schedule a pip twice");
                    Contract.Assume( (sealDirectory.Kind == SealDirectoryKind.SharedOpaque && sealDirectory.IsInitialized) ||
                        (sealDirectory.Kind != SealDirectoryKind.SharedOpaque && SealDirectoryTable.IsPatching == sealDirectory.IsInitialized),
                        "A shared opaque directory is always initialized. Otherwise, if patching -> sealDirectory must already be initialized; otherwise, it must not be initialized");

                    foreach (FileArtifact artifact in sealDirectory.Contents)
                    {
                        // Note that IsValidInputFileArtifact logs its own errors.
                        if (!IsValidInputFileArtifact(pathAccessLock, artifact, sealDirectory, semanticPathExpander))
                        {
                            return DirectoryArtifact.Invalid;
                        }
                    }

                    // We're now committed to sealing the directory.

                    // CreateSourceFile all needed source pips (note that all nodes are created in a topological order).
                    foreach (FileArtifact artifact in sealDirectory.Contents)
                    {
                        if (artifact.IsSourceFile)
                        {
                            // Lazy source hashing: We often defer hashing sealed source files until the first access. See TryQuerySealedPathContentHash.
                            // Note that an eagerly-running pip may already exist for this artifact, in which case it remains eager.
                            EnsurePipExistsForSourceArtifact(pathAccessLock, artifact);
                        }
                    }

                    // The directory being sealed possibly already has one or more seals. We create a view collection if not.
                    // We're registering a new view in the collection; this generates a new ID.
                    // The DirectoryArtifact ends up unique since it combines the directory and a unique-within-directory seal id.
                    // Note that we do not collapse identical views into one artifact / ID.

                    // For the shared dynamic case, the seal directory has
                    // already been initialized
                    if (sealDirectory.Kind == SealDirectoryKind.SharedOpaque)
                    {
                        Contract.Assume(sealDirectory.Directory.IsSharedOpaque);
                        artifactForNewSeal = sealDirectory.Directory;
                    }
                    else
                    {
                        // For the regular dynamic case, the directory artifact is always
                        // created with sealId 0. For other cases, we reserve it
                        artifactForNewSeal = sealDirectory.Kind == SealDirectoryKind.Opaque
                            ? OutputDirectory.Create(sealDirectory.DirectoryRoot)
                            : SealDirectoryTable.ReserveDirectoryArtifact(sealDirectory);
                        sealDirectory.SetDirectoryArtifact(artifactForNewSeal);
                    }

                    Contract.Assume(
                        sealDirectory.IsInitialized,
                        "Pip must be fully initialized (by assigning a seal ID and artifact) before creating a node for it (and adding it to the pip table)");

                    // CreateSourceFile a node for the pip and add it to the pip table. This assigns a PipId.
                    NodeId sealDirectoryNode = CreateNodeForPip(sealDirectory);
                    Contract.Assume(sealDirectory.PipId.IsValid);

                    // Now that we have assigned a PipId and a DirectoryArtifact, sealDirectory is complete and immutable.
                    // We can now establish the directory artifact -> pip ID mapping.
                    SealDirectoryTable.AddSeal(sealDirectory);

                    // For the case of composite directories, there is no process pip that produces them, so
                    // we keep the equivalent of OutputDirectoryProducers in CompositeOutputDirectoryProducers.
                    // So we update it here, once the pip id has been assigned
                    if (sealDirectory.IsComposite)
                    {
                        CompositeOutputDirectoryProducers.Add(sealDirectory.Directory, sealDirectoryNode);
                    }

                    using (var edgeScope = MutableDataflowGraph.AcquireExclusiveIncomingEdgeScope(sealDirectoryNode))
                    {
                        if (!sealDirectory.Kind.IsDynamicKind())
                        {
                            foreach (FileArtifact artifact in sealDirectory.Contents)
                            {
                                // Lazy source hashing: Maybe we created a lazy source pip. This edge should not make it eager. See above.
                                AddInput(
                                    pathAccessLock,
                                    sealDirectoryNode,
                                    artifact,
                                    edgeScope: edgeScope);
                            }
                        }
                        else
                        {
                            // If the seal directory is a composite one, then there is no process producing it
                            if (!sealDirectory.IsComposite)
                            {
                                NodeId producerNode;
                                if (!OutputDirectoryProducers.TryGetValue(artifactForNewSeal, out producerNode))
                                {
                                    Contract.Assert(false, I($"Producer of output directory '{artifactForNewSeal.Path.ToString(Context.PathTable)}' must have been added"));
                                }

                                if (!MutableDataflowGraph.ContainsNode(producerNode))
                                {
                                    Contract.Assert(
                                        false,
                                        I($"Producer of output directory '{artifactForNewSeal.Path.ToString(Context.PathTable)}' must have been added to the mutable data flow graph"));
                                }

                                AddPipProducerConsumerDependency(producerNode, sealDirectoryNode, ignoreTopologicalCheck: false, edgeScope: edgeScope);
                            }
                            else
                            {
                                // If the seal directory is composed of other seal directories, we add a producer-consumer edge for each of them
                                foreach (var directoryElement in sealDirectory.ComposedDirectories)
                                {
                                    // The directory to compose should be a shared opaque. This is the only
                                    // kind of composite directory we support for now
                                    if (!directoryElement.IsSharedOpaque)
                                    {
                                        LogEventWithPipProvenance(
                                            Logger.ScheduleFailAddPipInvalidComposedSealDirectoryIsNotSharedOpaque,
                                            sealDirectory,
                                            root,
                                            directoryElement.Path);
                                        return DirectoryArtifact.Invalid;
                                    }

                                    // The directory to compose should be within the proposed root
                                    if (!directoryElement.Path.IsWithin(Context.PathTable, artifactForNewSeal.Path))
                                    {
                                        LogEventWithPipProvenance(
                                            Logger.ScheduleFailAddPipInvalidComposedSealDirectoryNotUnderRoot,
                                            sealDirectory,
                                            root,
                                            directoryElement.Path);
                                        return DirectoryArtifact.Invalid;
                                    }

                                    // First check if the element is a regular shared opaque, i.e. it is part of the directory outputs
                                    // populated by process pips
                                    if (!OutputDirectoryProducers.TryGetValue(directoryElement, out NodeId directoryElementProducer))
                                    {
                                        // Otherwise, the element has to be a composite shared opaque
                                        if (!CompositeOutputDirectoryProducers.TryGetValue(directoryElement, out directoryElementProducer))
                                        {
                                            Contract.Assert(false, I($"Producer of output directory '{directoryElement.Path.ToString(Context.PathTable)}' must have been added"));
                                        }
                                    }

                                    AddPipProducerConsumerDependency(directoryElementProducer, sealDirectoryNode, ignoreTopologicalCheck: false, edgeScope: edgeScope);
                                }
                            }
                        }
                    }

                    // Link to value pip
                    if (valuePipId.IsValid)
                    {
                        AddPipProducerConsumerDependency(sealDirectoryNode, valuePipId.ToNodeId(), ignoreTopologicalCheck: true);
                    }
                    else
                    {
                        AddValueDependency(sealDirectoryNode, sealDirectory.Provenance);
                    }

                    // Update the source sealed directory root map
                    // If a directory artifact for the corresponding root was already added, then we
                    // don't try to store it again since we don't care about keeping all of them, just
                    // a directory artifact is enough for user-facing reporting purposes
                    if (sealDirectory.Kind.IsSourceSeal())
                    {
                        SourceSealedDirectoryRoots.TryAdd(root, artifactForNewSeal);
                    }
                }

                ComputeAndStorePipStaticFingerprint(sealDirectory);

                return artifactForNewSeal;
            }

            /// <summary>
            /// Ensures that a <see cref="HashSourceFile" /> pip exists for the given source artifact.
            /// </summary>
            /// <remarks>
            /// Source file artifacts are 'produced' (actually, hashed) by internal HashSourceFile pips.
            /// We do not know about source files until they are first found as an input, so we JIT up a corresponding pip
            /// if needed (or re-use the existing one).
            /// </remarks>
            private void EnsurePipExistsForSourceArtifact(LockManager.PathAccessGroupLock pathAccessLock, FileArtifact artifact)
            {
                Contract.Requires(!IsImmutable);
                Contract.Requires(pathAccessLock.HasReadAccess(artifact.Path));
                Contract.Requires(artifact.IsSourceFile);

                if (PipProducers.ContainsKey(artifact))
                {
                    return;
                }

                using (pathAccessLock.AcquirePathInnerExclusiveLock(artifact.Path))
                {
                    if (PipProducers.ContainsKey(artifact))
                    {
                        return;
                    }

                    // Note that this will fail if this path has already been used as an input (but that should have failed validation already).
                    SetLatestFileArtifactForPath(artifact, expectFirstVersion: true);

                    NodeId node;
                    if (ScheduleConfiguration.SkipHashSourceFile)
                    {
                        node = m_dummyHashSourceFileNode;
                    }
                    else
                    {
                        var sourceFileArtifactPip = new HashSourceFile(artifact);
                        node = CreateNodeForPip(sourceFileArtifactPip);
                    }

                    PipProducers.Add(artifact, node);
                }
            }

            private void AddPipToPipDependency(NodeId pipNodeAfter, PipId pipIdBefore, MutableDirectedGraph.EdgeScope edgeScope)
            {
                Contract.Requires(!IsImmutable);
                Contract.Requires(MutableDataflowGraph.ContainsNode(pipNodeAfter));
                Contract.Requires(PipTable.IsValid(pipIdBefore));

                // TODO: This doesn't change any state for rewrite validation. Oops.
                Pip pipBefore = PipTable.HydratePip(pipIdBefore, PipQueryContext.PipGraphAddPipToPipDependency);
                AddPipProducerConsumerDependency(pipBefore.PipId.ToNodeId(), pipNodeAfter, edgeScope: edgeScope);
            }

            private void ProcessServicePipDependency(PipId serviceClientPipId, NodeId serviceClientNode, PipId servicePipId, MutableDirectedGraph.EdgeScope edgeScope)
            {
                Contract.Requires(!IsImmutable);
                Contract.Requires(PipTable.IsValid(servicePipId));

                // remember service client
                var getOrAddResult = m_servicePipClients.GetOrAdd(servicePipId, new ConcurrentBigSet<PipId>());
                getOrAddResult.Item.Value.Add(serviceClientPipId);

                AddPipProducerConsumerDependency(servicePipId.ToNodeId(), serviceClientNode, edgeScope: edgeScope);

                // add edges to finalization pips
                foreach (var finalizationPipId in m_servicePipToServiceInfoMap[servicePipId].FinalizationPipIds)
                {
                    AddPipProducerConsumerDependency(serviceClientNode, finalizationPipId.ToNodeId(), ignoreTopologicalCheck: true);
                }
            }

            /// <summary>
            /// Adds producer-consumer dependency between pips.
            /// </summary>
            /// <remarks>
            /// This method conceptually adds a directed edge from the consumer to producer.
            /// </remarks>
            private void AddPipProducerConsumerDependency(
                NodeId producerNode,
                NodeId consumerNode,
                bool isLightEdge = false,
                bool ignoreTopologicalCheck = false,
                MutableDirectedGraph.EdgeScope edgeScope = null)
            {
                Contract.Requires(!IsImmutable);
                Contract.Requires(MutableDataflowGraph != null, "There should be a reference to the MutableDirectedGraph in order to add a dependency.");
                Contract.Requires(
                    MutableDataflowGraph.ContainsNode(producerNode),
                    "Argument producerNode must exist in the supporting dependency graph");
                Contract.Requires(
                    MutableDataflowGraph.ContainsNode(consumerNode),
                    "Argument consumerNode must exist in the supporting dependency graph");

                Contract.Assume(ignoreTopologicalCheck || consumerNode.Value >= producerNode.Value, "Node IDs must form a topological order for some graph traversals.");

                if (edgeScope != null)
                {
                    edgeScope.AddEdge(producerNode, isLight: isLightEdge);
                }
                else
                {
                    MutableDataflowGraph.AddEdge(producerNode, consumerNode, isLight: isLightEdge);
                }
            }

            /// <summary>
            /// Adds an input to a pip node. This registers a dependency edge if necessary and updates related bookkeeping.
            /// </summary>
            /// <param name="pathAccessLock">the access lock acquired by the enclosing operation for read access to the file</param>
            /// <param name="consumerNode">The node consuming <paramref name="inputArtifact" /></param>
            /// <param name="inputArtifact">The input dependency</param>
            /// <param name="edgeScope">Optional. The edge scope for adding a dependency edge to the producer.</param>
            /// <remarks>
            /// The path read lock must be held when calling this method. It is assumed (but not verified)
            /// that the input relation is valid; see <see cref="IsValidInputFileArtifact" />.
            /// </remarks>
            private void AddInput(
                LockManager.PathAccessGroupLock pathAccessLock,
                NodeId consumerNode,
                FileArtifact inputArtifact,
                MutableDirectedGraph.EdgeScope edgeScope = null)
            {
                Contract.Requires(!IsImmutable);
                Contract.Requires(pathAccessLock.HasReadAccess(inputArtifact.Path));
                Contract.Requires(inputArtifact.IsValid);
                Contract.Requires(MutableDataflowGraph.ContainsNode(consumerNode), "Argument consumerNode must exist in the supporting dependency graph");

                // In these branches we ensure that producerNode is set to the pip node that produces inputArtifact.
                NodeId producerNode;
                if (inputArtifact.IsSourceFile)
                {
                    if (ScheduleConfiguration.SkipHashSourceFile)
                    {
                        return;
                    }

                    bool sourceProducerFound = PipProducers.TryGetValue(inputArtifact, out producerNode);
                    Contract.Assume(sourceProducerFound, "Missing HashSourceFile producer; forgot to call EnsurePipExistsForSourceArtifact?");

                    PipId sourceProducerId = producerNode.ToPipId();
                    Contract.Assume(PipTable.GetPipType(sourceProducerId) == PipType.HashSourceFile);
                }
                else if (inputArtifact.IsOutputFile)
                {
                    m_outputFileArtifactsUsedAsInputs.Add(inputArtifact);
                    producerNode = PipProducers[inputArtifact];
                }
                else
                {
                    throw Contract.AssertFailure("Unexpected artifact type");
                }

                Contract.Assert(producerNode != NodeId.Invalid);

                Contract.Assume(MutableDataflowGraph.ContainsNode(producerNode));
                AddPipProducerConsumerDependency(producerNode, consumerNode, isLightEdge: inputArtifact.IsSourceFile, edgeScope: edgeScope);
            }

            /// <summary>
            /// Adds a directory input to a pip node. This registers a dependency edge if necessary and updates related bookkeeping.
            /// </summary>
            /// <param name="pathAccessLock">the access lock acquired by the enclosing operation for read access to the directory</param>
            /// <param name="consumerNode">The node consuming <paramref name="directory" /></param>
            /// <param name="directory">The input dependency</param>
            /// <param name="edgeScope">Optional. The edge scope for adding a dependency edge to the producer.</param>
            /// <remarks>
            /// The path read lock must be held when calling this method.
            /// </remarks>
            private void AddDirectoryInput(
                LockManager.PathAccessGroupLock pathAccessLock,
                NodeId consumerNode,
                DirectoryArtifact directory,
                MutableDirectedGraph.EdgeScope edgeScope)
            {
                Contract.Requires(!IsImmutable);
                Contract.Requires(pathAccessLock.HasReadAccess(directory.Path));

                // Each sealed directory gets its own node. That means we don't care here what the contents are (and maybe we don't know them yet).
                SealDirectoryTable.TryGetSealForDirectoryArtifact(directory, out PipId producerId);
                Contract.Assert(producerId.IsValid);

                NodeId producerNode = producerId.ToNodeId();
                Contract.Assume(MutableDataflowGraph.ContainsNode(producerNode));

                AddPipProducerConsumerDependency(producerNode, consumerNode, edgeScope: edgeScope);
            }

            /// <summary>
            /// Seals output directory.
            /// </summary>
            private bool SealDirectoryOutput(Process producer, DirectoryArtifact directory, int directoryOutputsIndex)
            {
                Contract.Requires(!IsImmutable);
                Contract.Requires(OutputDirectory.IsOutputDirectory(directory));

                var dynamicKind = directory.IsSharedOpaque ? SealDirectoryKind.SharedOpaque : SealDirectoryKind.Opaque;

                var provenance = producer.Provenance?.CloneWithSaltedSemiStableHash(HashCodeHelper.Combine(
                    directoryOutputsIndex,
                    (int)PipType.SealDirectory,
                    (int)dynamicKind));

                var sealedDirectory = new SealDirectory(directory.Path, s_emptySealContents, dynamicKind, provenance, producer.Tags, patterns: ReadOnlyArray<StringId>.Empty);
                // For the case of shared dynamic directory, the directory artifact already
                // has the proper seal id, so the seal directory can be initialized here
                if (dynamicKind == SealDirectoryKind.SharedOpaque)
                {
                    sealedDirectory.SetDirectoryArtifact(directory);
                }

                var directoryArtifact = AddSealDirectory(sealedDirectory);
                return directoryArtifact.IsValid;
            }

            /// <summary>
            /// Adds an output to a pip node. This registers a dependency edge if necessary (from prior versions of a rewritten output)
            /// and updates related bookkeeping. Note that all outputs of a pip must be added before its inputs.
            /// </summary>
            /// <param name="pathAccessLock">the access lock acquired by the enclosing operation for write access to the file</param>
            /// <param name="producerNode">The node consuming <paramref name="outputArtifact" /></param>
            /// <param name="outputArtifact">The output produced by the node</param>
            /// <param name="edgeScope">Optional. The edge scope for adding a dependency edge to the producer of the rewritten file</param>
            /// <remarks>
            /// The path write lock must be held when calling this method. It is assumed (but not verified)
            /// that the input relation is valid; see <see cref="IsValidOutputFileArtifact" />.
            /// </remarks>
            private void AddOutput(
                LockManager.PathAccessGroupLock pathAccessLock,
                NodeId producerNode,
                FileArtifactWithAttributes outputArtifact,
                MutableDirectedGraph.EdgeScope edgeScope = null)
            {
                Contract.Requires(!IsImmutable);
                Contract.Requires(pathAccessLock.HasWriteAccess(outputArtifact.Path));
                Contract.Requires(outputArtifact.IsValid);
                Contract.Requires(
                    MutableDataflowGraph.ContainsNode(producerNode),
                    "Argument producerNode must exist in the supporting dependency graph");

                Contract.Assume(!outputArtifact.IsSourceFile);

                FileArtifact existingLatestVersion = TryGetLatestFileArtifactForPath(outputArtifact.Path);
                bool hasExistingVersion = existingLatestVersion.IsValid;

                Contract.Assume(
                    !hasExistingVersion || existingLatestVersion.RewriteCount == outputArtifact.RewriteCount - 1,
                    "Output artifact should have failed validation (incorrect rewrite count).");

                // This node is rewriting an existing output. We must ensure that this node is not scheduled until the prior version is written.
                // To do so, we ensure that an edge exists from the last version's producer to this new producer (it may already exist due to an input dependency
                // or another output dependency). Note that adding this edge cannot possibly introduce a cycle, since this node is presumably in the process of being
                // added (thus no other nodes can yet depend on its outputs; in other words, nodes are added in a topological order).
                if (outputArtifact.RewriteCount > 1)
                {
                    Contract.Assume(hasExistingVersion);
                    Contract.Assume(
                        PipProducers[existingLatestVersion] != producerNode,
                        "Shouldn't already have registered an output dependency from this node to this path");

                    // TODO: I disabled the following assertion because
                    // TODO: (1) this check should be part of validation, and
                    // TODO: (2) to allow rewriting source, I need to add input before output (and this sounds more natural).
                    // Outputs must be added before inputs only to support the following assertion.
                    // Contract.Assume(
                    //    !m_outputFileArtifactsUsedAsInputs.Contains(existingLatestVersion),
                    //    "Previous version is already an input (rewrite chains can't fork); should have failed output validation");
                    AddInput(pathAccessLock, producerNode, existingLatestVersion, edgeScope: edgeScope);
                }

                FileArtifact simpleOutputArtifact = outputArtifact.ToFileArtifact();

                SetLatestFileArtifactForPath(simpleOutputArtifact);
                PipProducers.Add(simpleOutputArtifact, producerNode);

                if (outputArtifact.IsTemporaryOutputFile)
                {
                    // Storing temporary files separately to be able to validate inputs properly
                    m_temporaryOutputFiles.Add(outputArtifact.ToFileArtifact());
                    TemporaryPaths.Add(outputArtifact.Path, producerNode.ToPipId());
                }
            }

            /// <summary>
            /// Sets the latest file artifact (version) for a path.
            /// </summary>
            /// <remarks>
            /// The graph lock need not be held when calling this method.
            /// </remarks>
            private void SetLatestFileArtifactForPath(FileArtifact artifact, bool expectFirstVersion = false)
            {
                Contract.Requires(artifact.IsValid);
                Contract.Requires(artifact.RewriteCount >= 0);

                AbsolutePath path = artifact.Path;
                int rewriteCount = artifact.RewriteCount;

                if (expectFirstVersion)
                {
                    LatestWriteCountsByPath.Add(path, rewriteCount);
                }
                else
                {
                    LatestWriteCountsByPath[path] = rewriteCount;
                }
            }

            private void ComputeAndStorePipStaticFingerprint(Pip pip)
            {
                Contract.Requires(pip != null);

                if (!ShouldComputePipStaticFingerprints)
                {
                    return;
                }

                string fingerprintText = null;

                ContentFingerprint fingerprint = m_pipStaticFingerprinter.FingerprintTextEnabled
                    ? m_pipStaticFingerprinter.ComputeWeakFingerprint(pip, out fingerprintText)
                    : m_pipStaticFingerprinter.ComputeWeakFingerprint(pip);

                m_pipStaticFingerprints.AddFingerprint(pip, fingerprint);

                if (fingerprintText != null)
                {
                    Logger.Log.PipStaticFingerprint(LoggingContext, pip.GetDescription(Context), fingerprint.ToString(), fingerprintText);
                }
            }

            private ContentFingerprint GetSealDirectoryFingerprint(DirectoryArtifact directory)
            {
                Contract.Requires(directory.IsValid);

                return SealDirectoryTable.TryGetSealForDirectoryArtifact(directory, out PipId pipId)
                       && PipTable.HydratePip(pipId, PipQueryContext.GetSealDirectoryFingerprint) is SealDirectory sealDirectory
                       && m_pipStaticFingerprints.TryGetFingerprint(sealDirectory, out ContentFingerprint fingerprint)
                       ? fingerprint
                       : ContentFingerprint.Zero;
            }

            private ContentFingerprint GetDirectoryProducerFingerprint(DirectoryArtifact directory)
            {
                Contract.Requires(directory.IsValid);

                return OutputDirectoryProducers.TryGetValue(directory, out NodeId nodeId)
                    && m_pipStaticFingerprints.TryGetFingerprint(nodeId.ToPipId(), out ContentFingerprint fingerprint)
                    ? fingerprint
                    : ContentFingerprint.Zero;
            }

            #endregion Pip Addition

            #region Event Logging

            private delegate void PipProvenanceEvent(
                LoggingContext loggingContext,
                string file,
                int line,
                int column,
                long pipSemiStableHash,
                string pipDesc,
                string pipValueId);

            private delegate void PipProvenanceEventWithFilePath(
                LoggingContext loggingContext,
                string file,
                int line,
                int column,
                long pipSemiStableHash,
                string pipDesc,
                string pipValueId,
                string filePath);

            // Handy for errors related to sealed directories, since there is a directory root associated with the file.
            private delegate void PipProvenanceEventWithFilePathAndDirectoryPath(
                LoggingContext loggingContext,
                string file,
                int line,
                int column,
                long pipSemiStableHash,
                string pipDesc,
                string pipValueId,
                string filePath,
                string directoryPath);

            private delegate void PipProvenanceEventWithDirectoryPath(
                LoggingContext loggingContext,
                string file,
                int line,
                int column,
                long pipSemiStableHash,
                string pipDesc,
                string pipValueId,
                string directoryPath);

            private delegate void PipProvenanceEventWithTwoDirectoryPaths(
                LoggingContext loggingContext,
                string file,
                int line,
                int column,
                long pipSemiStableHash,
                string pipDesc,
                string pipValueId,
                string directoryPath,
                string anotherDirectoryPath);

            private delegate void PipProvenanceEventWithDirectoryPathAndName(
                LoggingContext loggingContext,
                string file,
                int line,
                int column,
                long pipSemiStableHash,
                string pipDesc,
                string pipValueId,
                string directoryPath,
                string name);

            private delegate void PipProvenanceEventWithDirectoryPathAndRootPathAndName(
                LoggingContext loggingContext,
                string file,
                int line,
                int column,
                long pipSemiStableHash,
                string pipDesc,
                string pipValueId,
                string directoryPath,
                string rootPath,
                string name);

            private delegate void PipProvenanceEventWithDirectoryPathAndRootPath(
                LoggingContext loggingContext,
                string file,
                int line,
                int column,
                long pipSemiStableHash,
                string pipDesc,
                string pipValueId,
                string directoryPath,
                string rootPath);

            private delegate void PipProvenanceEventWithFilePathAndRelatedPip(
                LoggingContext loggingContext,
                string file,
                int line,
                int column,
                long pipSemiStableHash,
                string pipDesc,
                string pipValueId,
                string outputFile,
                long producingPipSemiStableHash,
                string producingPipDesc,
                string producingPipValueId);

            // Handy for errors related to sealed directories, since there is a directory root associated with the file.
            private delegate void PipProvenanceEventWithFilePathAndDirectoryPathAndRelatedPip(
                LoggingContext loggingContext,
                string file,
                int line,
                int column,
                long pipSemiStableHash,
                string pipDesc,
                string pipValueId,
                string outputFile,
                string directoryPath,
                long producingPipSemiStableHash,
                string producingPipDesc,
                string producingPipValueId);

            private PipProvenance m_dummyProvenance;

            private PipProvenance GetDummyProvenance()
            {
                Contract.Ensures(Contract.Result<PipProvenance>() != null);
                return m_dummyProvenance = m_dummyProvenance ?? PipProvenance.CreateDummy(Context);
            }

            private void LogEventWithPipProvenance(PipProvenanceEvent pipEvent, Pip pip)
            {
                Contract.Requires(pipEvent != null);
                Contract.Requires(pip != null);

                PipProvenance provenance = pip.Provenance ?? GetDummyProvenance();
                pipEvent(
                    LoggingContext,
                    provenance.Token.Path.ToString(Context.PathTable),
                    provenance.Token.Line,
                    provenance.Token.Position,
                    provenance.SemiStableHash,
                    pip.GetDescription(Context),
                    provenance.OutputValueSymbol.ToString(Context.SymbolTable));
            }

            private void LogEventWithPipProvenance(PipProvenanceEventWithFilePath pipEvent, Pip pip, FileArtifact relatedArtifact)
            {
                Contract.Requires(pipEvent != null);
                Contract.Requires(pip != null);
                Contract.Requires(relatedArtifact.IsValid);

                PipProvenance provenance = pip.Provenance ?? GetDummyProvenance();
                pipEvent(
                    LoggingContext,
                    provenance.Token.Path.ToString(Context.PathTable),
                    provenance.Token.Line,
                    provenance.Token.Position,
                    provenance.SemiStableHash,
                    pip.GetDescription(Context),
                    provenance.OutputValueSymbol.ToString(Context.SymbolTable),
                    relatedArtifact.Path.ToString(Context.PathTable));
            }

            private void LogEventWithPipProvenance(
                PipProvenanceEventWithFilePathAndDirectoryPath pipEvent,
                Pip pip,
                FileArtifact relatedArtifact,
                AbsolutePath directoryPath)
            {
                Contract.Requires(pipEvent != null);
                Contract.Requires(pip != null);
                Contract.Requires(relatedArtifact.IsValid);
                Contract.Requires(directoryPath.IsValid);

                PipProvenance provenance = pip.Provenance ?? GetDummyProvenance();
                pipEvent(
                    LoggingContext,
                    provenance.Token.Path.ToString(Context.PathTable),
                    provenance.Token.Line,
                    provenance.Token.Position,
                    provenance.SemiStableHash,
                    pip.GetDescription(Context),
                    provenance.OutputValueSymbol.ToString(Context.SymbolTable),
                    relatedArtifact.Path.ToString(Context.PathTable),
                    directoryPath.ToString(Context.PathTable));
            }

            private void LogEventWithPipProvenance(
                PipProvenanceEventWithDirectoryPath pipEvent,
                Pip pip,
                AbsolutePath relatedPath)
            {
                Contract.Requires(pipEvent != null);
                Contract.Requires(pip != null);
                Contract.Requires(relatedPath.IsValid);

                PipProvenance provenance = pip.Provenance ?? GetDummyProvenance();
                pipEvent(
                    LoggingContext,
                    provenance.Token.Path.ToString(Context.PathTable),
                    provenance.Token.Line,
                    provenance.Token.Position,
                    provenance.SemiStableHash,
                    pip.GetDescription(Context),
                    provenance.OutputValueSymbol.ToString(Context.SymbolTable),
                    relatedPath.ToString(Context.PathTable));
            }

            private void LogEventWithPipProvenance(
                PipProvenanceEventWithTwoDirectoryPaths pipEvent,
                Pip pip,
                AbsolutePath relatedPath,
                AbsolutePath anotherRelatedPath)
            {
                Contract.Requires(pipEvent != null);
                Contract.Requires(pip != null);
                Contract.Requires(relatedPath.IsValid);

                PipProvenance provenance = pip.Provenance ?? GetDummyProvenance();
                pipEvent(
                    LoggingContext,
                    provenance.Token.Path.ToString(Context.PathTable),
                    provenance.Token.Line,
                    provenance.Token.Position,
                    provenance.SemiStableHash,
                    pip.GetDescription(Context),
                    provenance.OutputValueSymbol.ToString(Context.SymbolTable),
                    relatedPath.ToString(Context.PathTable),
                    anotherRelatedPath.ToString(Context.PathTable));
            }

            private void LogEventWithPipProvenance(
                PipProvenanceEventWithDirectoryPathAndName pipEvent,
                Pip pip,
                string directoryPath,
                StringId name)
            {
                Contract.Requires(pipEvent != null);
                Contract.Requires(pip != null);
                Contract.Requires(!string.IsNullOrEmpty(directoryPath));
                Contract.Requires(name.IsValid);

                PipProvenance provenance = pip.Provenance ?? GetDummyProvenance();
                pipEvent(
                    LoggingContext,
                    provenance.Token.Path.ToString(Context.PathTable),
                    provenance.Token.Line,
                    provenance.Token.Position,
                    provenance.SemiStableHash,
                    pip.GetDescription(Context),
                    provenance.OutputValueSymbol.ToString(Context.SymbolTable),
                    directoryPath,
                    Context.StringTable.GetString(name));
            }

            private void LogEventWithPipProvenance(
                PipProvenanceEventWithDirectoryPathAndRootPathAndName pipEvent,
                Pip pip,
                AbsolutePath relatedPath,
                AbsolutePath rootPath,
                StringId name)
            {
                Contract.Requires(pipEvent != null);
                Contract.Requires(pip != null);
                Contract.Requires(rootPath.IsValid);
                Contract.Requires(relatedPath.IsValid);
                Contract.Requires(name.IsValid);

                PipProvenance provenance = pip.Provenance ?? GetDummyProvenance();
                pipEvent(
                    LoggingContext,
                    provenance.Token.Path.ToString(Context.PathTable),
                    provenance.Token.Line,
                    provenance.Token.Position,
                    provenance.SemiStableHash,
                    pip.GetDescription(Context),
                    provenance.OutputValueSymbol.ToString(Context.SymbolTable),
                    relatedPath.ToString(Context.PathTable),
                    rootPath.ToString(Context.PathTable),
                    Context.StringTable.GetString(name));
            }

            private void LogEventWithPipProvenance(
                PipProvenanceEventWithFilePathAndRelatedPip pipEvent,
                Pip pip,
                Pip relatedPip,
                FileArtifact relatedArtifact)
            {
                Contract.Requires(pipEvent != null);
                Contract.Requires(pip != null);
                Contract.Requires(relatedPip != null);
                Contract.Requires(relatedArtifact.IsValid);

                PipProvenance provenance = pip.Provenance ?? GetDummyProvenance();
                PipProvenance provenanceForRelated = relatedPip.Provenance ?? GetDummyProvenance();
                pipEvent(
                    LoggingContext,
                    provenance.Token.Path.ToString(Context.PathTable),
                    provenance.Token.Line,
                    provenance.Token.Position,
                    provenance.SemiStableHash,
                    pip.GetDescription(Context),
                    provenance.OutputValueSymbol.ToString(Context.SymbolTable),
                    relatedArtifact.Path.ToString(Context.PathTable),
                    provenanceForRelated.SemiStableHash,
                    relatedPip.GetDescription(Context),
                    relatedPip.Provenance.OutputValueSymbol.ToString(Context.SymbolTable));
            }

            private void LogEventWithPipProvenance(
                PipProvenanceEventWithFilePathAndRelatedPip pipEvent,
                Pip pip,
                Pip relatedPip,
                AbsolutePath relatedPath)
            {
                Contract.Requires(pipEvent != null);
                Contract.Requires(pip != null);
                Contract.Requires(relatedPip != null);
                Contract.Requires(relatedPath.IsValid);

                PipProvenance provenance = pip.Provenance ?? GetDummyProvenance();
                PipProvenance provenanceForRelated = relatedPip.Provenance ?? GetDummyProvenance();
                pipEvent(
                    LoggingContext,
                    provenance.Token.Path.ToString(Context.PathTable),
                    provenance.Token.Line,
                    provenance.Token.Position,
                    provenance.SemiStableHash,
                    pip.GetDescription(Context),
                    provenance.OutputValueSymbol.ToString(Context.SymbolTable),
                    relatedPath.ToString(Context.PathTable),
                    provenanceForRelated.SemiStableHash,
                    relatedPip.GetDescription(Context),
                    relatedPip.Provenance.OutputValueSymbol.ToString(Context.SymbolTable));
            }

            private void LogEventWithPipProvenance(
                PipProvenanceEventWithFilePathAndDirectoryPathAndRelatedPip pipEvent,
                Pip pip,
                Pip relatedPip,
                FileArtifact relatedArtifact,
                AbsolutePath directoryPath)
            {
                Contract.Requires(pipEvent != null);
                Contract.Requires(pip != null);
                Contract.Requires(relatedPip != null);
                Contract.Requires(relatedArtifact.IsValid);
                Contract.Requires(directoryPath.IsValid);

                PipProvenance provenance = pip.Provenance ?? GetDummyProvenance();
                PipProvenance provenanceForRelated = relatedPip.Provenance ?? GetDummyProvenance();
                pipEvent(
                    LoggingContext,
                    provenance.Token.Path.ToString(Context.PathTable),
                    provenance.Token.Line,
                    provenance.Token.Position,
                    provenance.SemiStableHash,
                    pip.GetDescription(Context),
                    provenance.OutputValueSymbol.ToString(Context.SymbolTable),
                    relatedArtifact.Path.ToString(Context.PathTable),
                    directoryPath.ToString(Context.PathTable),
                    provenanceForRelated.SemiStableHash,
                    relatedPip.GetDescription(Context),
                    provenanceForRelated.OutputValueSymbol.ToString(Context.SymbolTable));
            }

            #endregion Event Logging

            #region IPipScheduleTraversal Members

            public override IEnumerable<Pip> RetrievePipImmediateDependencies(Pip pip)
            {
                // TODO: This lock would no longer be required if pips must be added in dependency first order
                // since the set of dependencies would not change after adding a pip
                using (LockManager.AcquireLock(pip.PipId))
                {
                    return base.RetrievePipImmediateDependencies(pip);
                }
            }

            public override IEnumerable<Pip> RetrievePipImmediateDependents(Pip pip)
            {
                // TODO: This lock would no longer be required if pips must be added in dependency first order
                // since the set of dependencies would not change after adding a pip
                using (LockManager.AcquireLock(pip.PipId))
                {
                    return base.RetrievePipImmediateDependents(pip).ToArray();
                }
            }

            /// <inheritdoc />
            public GraphPatchingStatistics PartiallyReloadGraph(HashSet<AbsolutePath> affectedSpecs)
            {
                Contract.Requires(affectedSpecs != null);

                throw new InvalidOperationException("This graph builder does not support graph patching");
            }

            /// <inheritdoc />
            public void SetSpecsToIgnore(IEnumerable<AbsolutePath> specsToIgnore)
            {
                throw new InvalidOperationException("This graph builder does not support graph patching");
            }

            #endregion
        }
    }
}
