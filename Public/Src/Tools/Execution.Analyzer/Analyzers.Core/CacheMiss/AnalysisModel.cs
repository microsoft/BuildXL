// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using System;
using System.Collections.Generic;
using ContentHashLookup = BuildXL.Pips.Operations.PipFragmentRenderer.ContentHashLookup;

namespace BuildXL.Execution.Analyzer.Analyzers.CacheMiss
{
    /// <summary>
    /// Model for data from a cached graph and execution log trace needed for cache miss analysis
    /// </summary>
    internal sealed class AnalysisModel
    {
        public CachedGraph CachedGraph;
        public VisitationTracker ChangedPips;

        public ExtraFingerprintSalts Salts;

        public string[] Workers { get; set; } = new string[] { "Local" };

        #region Accessors

        /// <summary>
        /// The loaded directed graph
        /// </summary>
        public DirectedGraph DataflowGraph
        {
            get
            {
                return CachedGraph.DataflowGraph;
            }
        }

        /// <summary>
        /// The loaded pip table
        /// </summary>
        public PipTable PipTable
        {
            get
            {
                return CachedGraph.PipTable;
            }
        }

        /// <summary>
        /// The loaded path table
        /// </summary>
        public PathTable PathTable
        {
            get
            {
                return CachedGraph.Context.PathTable;
            }
        }

        /// <summary>
        /// The loaded symbol table
        /// </summary>
        public SymbolTable SymbolTable
        {
            get
            {
                return CachedGraph.Context.SymbolTable;
            }
        }

        #endregion

        private NodeVisitor visitor;
        private PipContentFingerprinter[] m_contentFingerprinters = new PipContentFingerprinter[1];

        public Func<uint, FileArtifact, FileContentInfo> LookupHashFunction { get; set; }

        public AnalysisModel(CachedGraph graph)
        {
            CachedGraph = graph;
            ChangedPips = new VisitationTracker(CachedGraph.DataflowGraph);
            visitor = new NodeVisitor(graph.DataflowGraph);
            LookupHashFunction = LookupHash;
        }

        public PipContentFingerprinter GetFingerprinter(uint workerId)
        {
            if (m_contentFingerprinters.Length <= workerId)
            {
                Array.Resize(ref m_contentFingerprinters, (int)workerId + 1);
            }

            if (m_contentFingerprinters[workerId] == null)
            {
                m_contentFingerprinters[workerId] = new PipContentFingerprinter(
                            CachedGraph.Context.PathTable,
                            artifact => LookupHashFunction(workerId, artifact),
                            Salts,
                            pathExpander: CachedGraph.MountPathExpander,
                            pipDataLookup: CachedGraph.PipGraph.QueryFileArtifactPipData)
                {
                    FingerprintTextEnabled = true,
                };
            }

            return m_contentFingerprinters[workerId];
        }

        /// <summary>
        /// Map of semistable hash to pip id
        /// </summary>
        private readonly ConcurrentBigMap<long, PipId> m_pipSemistableHashToPipId = new ConcurrentBigMap<long, PipId>();

        /// <summary>
        /// Map of file artifact to content information
        /// </summary>
        /// workerId, FileArtifact file, FileContentInfo fileContentInfo
        public readonly ConcurrentBigMap<(uint workerId, FileArtifact file), FileContentInfo> FileContentMap = new ConcurrentBigMap<(uint workerId, FileArtifact file), FileContentInfo>();
        public readonly ConcurrentBigMap<(uint workerId, AbsolutePath path, PipId, string enumeratePatternRegex), DirectoryMembershipHashedEventData> DirectoryData
            = new ConcurrentBigMap<(uint workerId, AbsolutePath path, PipId, string enumeratePatternRegex), DirectoryMembershipHashedEventData>();

        public readonly ConcurrentBigMap<PipId, PipCachingInfo> PipInfoMap = new ConcurrentBigMap<PipId, PipCachingInfo>();

        public PipCachingInfo GetPipInfo(PipId pipId)
        {
            if (!pipId.IsValid)
            {
                return null;
            }

            var result = PipInfoMap.GetOrAdd(pipId, this, (p, me) =>
            {
                var process = (Process)me.CachedGraph.PipGraph.GetPipFromPipId(pipId);
                return new PipCachingInfo(p, me)
                {
                    CacheablePipInfo = CacheableProcess.GetProcessCacheInfo(process, me.CachedGraph.Context),
                };
            });

            if (!result.IsFound)
            {
                m_pipSemistableHashToPipId.Add(CachedGraph.PipTable.GetPipSemiStableHash(pipId), pipId);
            }

            return result.Item.Value;
        }

        public void AddFileContentInfo(uint workerId, FileArtifact file, FileContentInfo fileContentInfo)
        {
            if (workerId != DistributionConstants.LocalWorkerId)
            {
                if (FileContentMap.TryGetValue((DistributionConstants.LocalWorkerId, file), out var localWorkerInfo)
                    && localWorkerInfo.Hash == fileContentInfo.Hash)
                {
                    // Don't add data which is redundant with local worker
                    return;
                }
            }

            FileContentMap.GetOrAdd((workerId, file), fileContentInfo);
        }

        public void AddDirectoryData(uint workerId, DirectoryMembershipHashedEventData data)
        {
            if (workerId != DistributionConstants.LocalWorkerId)
            {
                if (DirectoryData.TryGetValue((DistributionConstants.LocalWorkerId, data.Directory, data.PipId, data.EnumeratePatternRegex), out var localWorkerInfo)
                    && localWorkerInfo.EnumeratePatternRegex == data.EnumeratePatternRegex
                    && localWorkerInfo.Directory == data.Directory
                    && localWorkerInfo.IsSearchPath == data.IsSearchPath
                    && localWorkerInfo.IsStatic == data.IsStatic
                    && ListEquals(localWorkerInfo.Members, data.Members))
                {
                    // Don't add data which is redundant with local worker
                    return;
                }
            }

            DirectoryData.GetOrAdd((workerId, data.Directory, data.PipId, data.EnumeratePatternRegex), data);
        }

        private bool ListEquals(List<AbsolutePath> members1, List<AbsolutePath> members2)
        {
            if (members1.Count != members2.Count)
            {
                return false;
            }

            for (int i = 0; i < members1.Count; i++)
            {
                if (members1[i] != members2[i])
                {
                    return false;
                }
            }

            return true;
        }

        public PipId GetPipId(long semistableHash)
        {
            return m_pipSemistableHashToPipId.GetOrAdd(semistableHash, PipId.Invalid).Item.Value;
        }

        public void MarkChanged(PipId pipId)
        {
            visitor.VisitTransitiveDependents(pipId.ToNodeId(), ChangedPips, n => true);
        }

        public bool HasChangedDependencies(PipId pipId)
        {
            foreach (var incoming in DataflowGraph.GetIncomingEdges(pipId.ToNodeId()))
            {
                if (ChangedPips.WasVisited(incoming.OtherNode))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetDirectoryData(uint workerId, AbsolutePath path, PipId pipId, string enumeratePatternRegex, out DirectoryMembershipHashedEventData data)
        {
            if (DirectoryData.TryGetValue((workerId, path, pipId, enumeratePatternRegex), out data))
            {
                return true;
            }

            // Hedge for future optimization where only master's data is logged when worker's data is the same as the masters.
            if (workerId != DistributionConstants.LocalWorkerId)
            {
                if (DirectoryData.TryGetValue((DistributionConstants.LocalWorkerId, path, pipId, enumeratePatternRegex), out data))
                {
                    return true;
                }
            }

            return false;
        }

        public FileContentInfo LookupHash(uint workerId, FileArtifact artifact)
        {
            FileContentInfo fileContentInfo;
            if (FileContentMap.TryGetValue((workerId, artifact), out fileContentInfo))
            {
                return fileContentInfo;
            }

            return FileContentMap[(DistributionConstants.LocalWorkerId, artifact)];
        }

        public DirectoryMembershipHashedEventData? GetDirectoryMembershipData(uint workerId, PipId pipId, AbsolutePath path, string enumeratePatternRegex)
        {
            DirectoryMembershipHashedEventData data;
            if (TryGetDirectoryData(workerId, path, pipId, enumeratePatternRegex, out data))
            {
                return data;
            }

            if (TryGetDirectoryData(workerId, path, PipId.Invalid, enumeratePatternRegex, out data))
            {
                return data;
            }

            return null;
        }
    }
}
