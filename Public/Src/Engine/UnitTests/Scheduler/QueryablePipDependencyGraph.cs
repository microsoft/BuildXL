// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.TestUtilities.Xunit;

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
        private readonly BuildXLContext m_context;
        private readonly Dictionary<AbsolutePath, Pip> m_pathProducers = new Dictionary<AbsolutePath, Pip>();
        private readonly Dictionary<PipId, Pip> m_pips = new Dictionary<PipId, Pip>();
        private int m_nextPipIdValue = 1;

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

        public Pip TryFindProducer(AbsolutePath producedPath, VersionDisposition versionDisposition, DependencyOrderingFilter? maybeOrderingFilter = null)
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
        public Process AddProcess(AbsolutePath producedPath, DoubleWritePolicy doubleWritePolicy = DoubleWritePolicy.DoubleWritesAreErrors)
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
                doubleWritePolicy: doubleWritePolicy);

            process.PipId = AllocateNextPipId();
            m_pips.Add(process.PipId, process);
            m_pathProducers.Add(producedPath, process);

            return process;
        }

        private PipId AllocateNextPipId()
        {
            return new PipId((uint)(m_nextPipIdValue++));
        }

        #region IQueryablePipDependencyGraph Members

        /// <inheritdoc />
        public bool IsReachableFrom(PipId from, PipId to)
        {
            throw new NotImplementedException();
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
        
        #endregion
    }
}
