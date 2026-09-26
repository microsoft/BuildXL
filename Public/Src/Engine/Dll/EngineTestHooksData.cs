// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Engine.Cache;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Processes;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Scheduler;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;

namespace BuildXL.Engine
{
    /// <summary>
    /// Class that contains hooks for test to inspect the internal state
    /// while not having to hold on to the state after it is needed in regular executions
    /// </summary>
    public sealed class EngineTestHooksData : IDisposable
    {
        private readonly DirectedGraphOwnership m_directedGraphOwnership = new DirectedGraphOwnership(graph: null, ownsGraph: false);

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="captureScheduler">indicates the scheduler should be captured and not disposed with engine.</param>
        /// <param name="captureFrontEndEngineAbstraction">indicates the created FrontEndEngineAbstraction should be captured and not disposed with engine.</param>
        public EngineTestHooksData(bool captureScheduler = false, bool captureFrontEndEngineAbstraction = false)
        {
            if (captureScheduler)
            {
                Scheduler = new BoxRef<Scheduler.Scheduler>();
            }

            if (captureFrontEndEngineAbstraction)
            {
                FrontEndEngineAbstraction = new BoxRef<FrontEndEngineAbstraction>();
            }
        }

        /// <summary>
        /// Specifies the factory for creating the cache to use.
        /// </summary>
        public Func<EngineCache> CacheFactory { get; set; }

        /// <summary>
        /// The scheduler used by the Engine
        /// </summary>
        public BoxRef<Scheduler.Scheduler> Scheduler { get; set; }

        /// <summary>
        /// The FrontEndEngineAbstraction created by the Engine
        /// </summary>
        public BoxRef<FrontEndEngineAbstraction> FrontEndEngineAbstraction { get; set; }

        /// <summary>
        /// Salt for graph fingerprint.
        /// </summary>
        public int? GraphFingerprintSalt { get; set; } = null;

        /// <summary>
        /// The AppDeployment used for an identity in graph caching
        /// </summary>
        public AppDeployment AppDeployment { get; set; } = null;

        /// <summary>
        /// Result of graph reuse check.
        /// </summary>
        public GraphReuseResultSnapshot GraphReuseResult { get; private set; } = null;

        /// <summary>
        /// Whether BuildXL should warn about directories that have virus scanned enabled
        /// </summary>
        public bool DoWarnForVirusScan { get; set; } = true;

        /// <summary>
        /// The temp directory the Engine used for initializing its <see cref="TempCleaner"/>
        /// The <see cref="TempCleaner"/>'s temp directory is passed into 
        /// <see cref="BuildXL.Native.IO.FileUtilities.DeleteFile(string, bool, BuildXL.Native.IO.ITempCleaner)"/>
        /// for move-deleting files
        /// </summary>
        public string TempCleanerTempDirectory { get; set; } = null;

        /// <summary>
        /// Listener to collect detours reported accesses
        /// </summary>
        public IDetoursEventListener DetoursListener { get; set; }

        /// <inheritdoc />
        public void Dispose()
        {
            m_directedGraphOwnership.Dispose();
            Scheduler?.Value?.PipGraph.PipTable.Dispose();
        }

        internal void TakeDirectedGraphOwnership(IReadonlyDirectedGraph graph)
        {
            // Release a graph retained by an earlier engine run before accepting the new graph.
            m_directedGraphOwnership.Dispose();
            m_directedGraphOwnership.TakeOwnership(graph);
        }

        internal void CaptureGraphReuseResult(GraphReuseResult result)
        {
            GraphReuseResult = new GraphReuseResultSnapshot(result);
        }
    }

    /// <summary>
    /// Graph reuse information retained by test hooks without retaining the graph or schedule.
    /// </summary>
    public sealed class GraphReuseResultSnapshot
    {
        internal GraphReuseResultSnapshot(GraphReuseResult result)
        {
            IsFullReuse = result.IsFullReuse;
            IsPartialReuse = result.IsPartialReuse;
            IsNoReuse = result.IsNoReuse;
            ChangedPaths = CopyPaths(result.InputChanges?.ChangedPaths);
            UnchangedPaths = CopyPaths(result.InputChanges?.UnchangedPaths.Keys);
        }

        /// <summary>
        /// Whether the complete engine schedule was reused.
        /// </summary>
        public bool IsFullReuse { get; }

        /// <summary>
        /// Whether the prior pip graph was reused for graph patching.
        /// </summary>
        public bool IsPartialReuse { get; }

        /// <summary>
        /// Whether no graph state was reused.
        /// </summary>
        public bool IsNoReuse { get; }

        /// <summary>
        /// Paths known to have changed since the previous build.
        /// </summary>
        public IReadOnlyList<string> ChangedPaths { get; }

        /// <summary>
        /// Paths known not to have changed since the previous build.
        /// </summary>
        public IReadOnlyList<string> UnchangedPaths { get; }

        private static IReadOnlyList<string> CopyPaths(IEnumerable<string> paths)
        {
            return paths == null
                ? Array.Empty<string>()
                : Array.AsReadOnly(paths.ToArray());
        }
    }
}
