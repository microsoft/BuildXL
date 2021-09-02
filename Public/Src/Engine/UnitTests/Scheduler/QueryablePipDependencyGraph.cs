// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Trivial implementation of <see cref="IQueryablePipDependencyGraph" />.
    /// The represented graph is one in which, when added, a new pip transitively depends upon every existing pip
    /// (any graph reachability check can be substituted for an ID comparison since the pips are serialized).
    /// Each path is produced once, so <see cref="VersionDisposition" /> can be ignored.
    /// </summary>
    public sealed class QueryablePipDependencyGraph : IQueryablePipDependencyGraph
    {
        public IReadonlyDirectedGraph DirectedGraph => throw new NotImplementedException();

        private readonly BuildXLContext m_context;
        private readonly Dictionary<AbsolutePath, Pip> m_pathProducers = new Dictionary<AbsolutePath, Pip>();
        private readonly Dictionary<PipId, Pip> m_pips = new Dictionary<PipId, Pip>();
        private int m_nextPipIdValue = 1;

        /// <summary>
        /// An edge (pipId1, pipId2) in this graph signifies that there is a dataflow dependency from 
        /// pipId1 to pipId2 (i.e., in a build, pipId1 must be executed before pipId2).
        /// </summary>
        private readonly MultiValueDictionary<PipId, PipId> m_dataflowGraph = new MultiValueDictionary<PipId, PipId>();

        private int m_concurrentStart = -1;
        private int m_concurrentStop = -1;

        public QueryablePipDependencyGraph(BuildXLContext context)
        {
            m_context = context;
        }

        /// <summary>
        /// Processes added to this graph are serialized in order of addition. This method sets a single range of already-added processes
        /// as 'concurrent', i.e., serializing edges among them removed.
        /// </summary>
        public void SetConcurrentRange(Process start, Process stop)
        {
            int startValue = (int)start.PipId.Value;
            int stopValue = (int)stop.PipId.Value;

            Contract.Assume(startValue < m_nextPipIdValue && stopValue < m_nextPipIdValue);
            Contract.Assume(startValue <= stopValue);

            m_concurrentStart = startValue;
            m_concurrentStop = stopValue;
        }

        public Pip TryFindProducer(AbsolutePath producedPath, VersionDisposition versionDisposition, DependencyOrderingFilter? maybeOrderingFilter = null, bool includeExclusiveOpaques = true)
        {
            Pip producer;
            if (!m_pathProducers.TryGetValue(producedPath, out producer))
            {
                return null;
            }

            if (maybeOrderingFilter.HasValue)
            {
                DependencyOrderingFilter filter = maybeOrderingFilter.Value;

                if (filter.Filter == DependencyOrderingFilterType.PossiblyPrecedingInWallTime) 
                {
                    if (!ArePipsConcurrent(producer, filter.Reference))
                    {
                        // Concurrent pips always possibly-precede one another. But now we need a strict ordering.

                        if (filter.Reference.PipId.Value <= producer.PipId.Value)
                        {
                            // The found pip does not precede the reference.
                            return null;
                        }
                    }
                }
                else if (filter.Filter == DependencyOrderingFilterType.Concurrent)
                {
                    if (!ArePipsConcurrent(producer, filter.Reference))
                    {
                        // Not in the range [concurrent start, concurrent stop] as established by SetConcurrentRange
                        return null;
                    }
                }
                else if (filter.Filter == DependencyOrderingFilterType.OrderedBefore)
                {
                    if (ArePipsConcurrent(producer, filter.Reference))
                    {
                        // If in [concurrent start, concurrent stop], the producer is not ordered before the reference (they are instead concurrent).
                        return null;
                    }

                    if (filter.Reference.PipId.Value <= producer.PipId.Value)
                    {
                        // Reference is ordered before producer. We wanted producer ordered before reference.
                        return null;
                    }
                }
                else
                {
                    throw Contract.AssertFailure("New filter type needs to be implemented");
                }
            }

            return producer;
        }

        private bool ArePipsConcurrent(Pip a, Pip b)
        {
            return (a.PipId.Value >= m_concurrentStart && a.PipId.Value <= m_concurrentStop) &&
                   (b.PipId.Value >= m_concurrentStart && b.PipId.Value <= m_concurrentStop);
        }

        /// <summary>
        /// Adds a fake process pip that produces only the given path.
        /// </summary>
        public Process AddProcess(AbsolutePath producedPath, RewritePolicy doubleWritePolicy = RewritePolicy.DoubleWritesAreErrors)
        {
            Contract.Assume(!m_pathProducers.ContainsKey(producedPath), "Each path may have only one producer (no rewrites)");

            AbsolutePath workingDirectory = AbsolutePath.Create(m_context.PathTable, PathGeneratorUtilities.GetAbsolutePath("X", ""));
            AbsolutePath exe = AbsolutePath.Create(m_context.PathTable, PathGeneratorUtilities.GetAbsolutePath("X", "fake.exe"));

            var process = new Process(
                executable: FileArtifact.CreateSourceFile(exe),
                workingDirectory: workingDirectory,
                arguments: PipDataBuilder.CreatePipData(m_context.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                standardInput: FileArtifact.Invalid,
                standardOutput: FileArtifact.Invalid,
                standardError: FileArtifact.Invalid,
                standardDirectory: workingDirectory,
                warningTimeout: null,
                timeout: null,
                dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(FileArtifact.CreateSourceFile(exe)),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(FileArtifact.CreateSourceFile(producedPath).CreateNextWrittenVersion().WithAttributes()),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(m_context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                rewritePolicy: doubleWritePolicy);

            process.PipId = AllocateNextPipId();
            m_pips.Add(process.PipId, process);
            m_pathProducers.Add(producedPath, process);

            return process;
        }

        /// <summary>
        /// Adds a fake write file pip that produces to the given destination path.
        /// </summary>
        public WriteFile AddWriteFilePip(AbsolutePath destinationPath)
        {
            Contract.Requires(destinationPath != null);

            FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationPath).CreateNextWrittenVersion();
            PipData contents = PipDataBuilder.CreatePipData(m_context.StringTable, " ", PipDataFragmentEscaping.CRuntimeArgumentRules, "content");

            var writeFile = new WriteFile(destinationArtifact, contents, WriteFileEncoding.Utf8, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(m_context));

            writeFile.PipId = AllocateNextPipId();
            m_pips.Add(writeFile.PipId, writeFile);
            m_pathProducers.Add(destinationArtifact, writeFile);

            return writeFile;
        }

        /// <summary>
        /// Adds a fake copy file pip that produces to the given destination path.
        /// </summary>
        public CopyFile AddCopyFilePip(FileArtifact source, FileArtifact destination)
        {
            Contract.Requires(source != null);
            Contract.Requires(destination != null);

            var copyFile = new CopyFile(source, destination, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(m_context));

            copyFile.PipId = AllocateNextPipId();
            m_pips.Add(copyFile.PipId, copyFile);
            m_pathProducers.Add(destination.Path, copyFile);

            return copyFile;
        }

        private PipId AllocateNextPipId()
        {
            return new PipId((uint)(m_nextPipIdValue++));
        }

        /// <summary>
        /// Adds a dataflow dependency from <paramref name="from"/> to <paramref name="to"/>
        /// (signifying 
        /// </summary>
        public void AddDataflowDependency(PipId from, PipId to)
        {
            m_dataflowGraph.Add(from, to);
        }

        #region IQueryablePipDependencyGraph Members

        private static readonly PipId[] EmptyPipIdList = new PipId[0];

        /// <inheritdoc />
        public bool IsReachableFrom(PipId from, PipId to)
        {
            if (from == to)
            {
                return true;
            }

            var visited = new HashSet<PipId>();
            var workList = new Queue<PipId>();
            workList.Enqueue(from);
            visited.Add(from);
            while (workList.Count > 0)
            {
                var node = workList.Dequeue();
                foreach (var nextNode in GetOutgoingNodes(node).Except(visited))
                {
                    if (nextNode == to)
                    {
                        return true;
                    }

                    workList.Enqueue(nextNode);
                    visited.Add(nextNode);
                }
            }

            return false;
        }

        private IReadOnlyList<PipId> GetOutgoingNodes(PipId node)
        {
            return m_dataflowGraph.TryGetValue(node, out var outNodes) && outNodes != null
                ? outNodes
                : EmptyPipIdList;
        }

        public Pip HydratePip(PipId pipId, PipQueryContext queryContext)
        {
            return m_pips[pipId];
        }

        public DirectoryArtifact TryGetSealSourceAncestor(AbsolutePath path)
        {
            return DirectoryArtifact.Invalid;
        }

        public Pip GetSealedDirectoryPip(DirectoryArtifact directoryArtifact, PipQueryContext queryContext)
        {
            throw new NotImplementedException();
        }

        public bool TryGetTempDirectoryAncestor(AbsolutePath path, out Pip pip, out AbsolutePath temPath)
        {
            temPath = AbsolutePath.Invalid;
            pip = null;
            return false;
        }

        public RewritePolicy GetRewritePolicy(PipId pipId)
        {
            return ((Process)m_pips[pipId]).RewritePolicy;
        }

        public string GetFormattedSemiStableHash(PipId pipId)
        {
            return ((Process)m_pips[pipId]).FormattedSemiStableHash;
        }

        public AbsolutePath GetProcessExecutablePath(PipId pipId)
        {
            return ((Process)m_pips[pipId]).Executable.Path;
        }

        #endregion
    }
}
