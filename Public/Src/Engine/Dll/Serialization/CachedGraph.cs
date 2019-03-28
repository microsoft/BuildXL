// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Serialization;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Engine
{
    /// <summary>
    /// An object encapsulating loaded pip graph and auxiliary data structures
    /// </summary>
    public sealed class CachedGraph
    {
        /// <summary>
        /// Caches loaded graphs using weak references to avoid keeping graphs alive
        /// </summary>
        private static readonly ObjectCache<Guid, WeakReference<CachedGraph>> s_loadedCachedGraphs = new ObjectCache<Guid, WeakReference<CachedGraph>>(1000);

        /// <summary>
        /// The directed graph for the graph
        /// </summary>
        public readonly DirectedGraph DataflowGraph;

        /// <summary>
        /// The pip table for the graph
        /// </summary>
        public readonly PipTable PipTable;

        /// <summary>
        /// The mount path expander for the graph
        /// </summary>
        public readonly MountPathExpander MountPathExpander;

        /// <summary>
        /// The execution context for the graph
        /// </summary>
        public readonly PipExecutionContext Context;

        /// <summary>
        /// The loaded pip graph
        /// </summary>
        public readonly PipGraph PipGraph;

        /// <summary>
        /// Class constructor
        /// </summary>
        public CachedGraph(PipGraph pipGraph, DirectedGraph dataflowGraph, PipExecutionContext context, MountPathExpander mountPathExpander)
        {
            Contract.Requires(pipGraph != null);
            Contract.Requires(dataflowGraph != null);
            Contract.Requires(context != null);
            Contract.Requires(mountPathExpander != null);

            DataflowGraph = dataflowGraph;
            PipTable = pipGraph.PipTable;
            MountPathExpander = mountPathExpander;
            Context = context;
            PipGraph = pipGraph;
        }

        /// <summary>
        /// Loads the cache graph store in the given directory
        /// </summary>
        public static async Task<CachedGraph> LoadAsync(string cachedGraphDirectory, LoggingContext loggingContext, bool preferLoadingEngineCacheInMemory, FileSystemStreamProvider readStreamProvider = null)
        {
            // preferLoadingEngineCacheInMemory will be removed after ExecutionLog.cs is updated in OSGTools.
            var unused = preferLoadingEngineCacheInMemory;
            CachedGraphLoader loadingGraph = CachedGraphLoader.CreateFromDisk(CancellationToken.None, cachedGraphDirectory, loggingContext, readStreamProvider);
            var graphId = await loadingGraph.GetOrLoadPipGraphIdAsync();

            // Use a the cached graph based on graph id if already in memory
            if (s_loadedCachedGraphs.TryGetValue(graphId, out var weakCachedGraph) && 
                weakCachedGraph.TryGetTarget(out var cachedGraph))
            {
                return cachedGraph;
            }

            cachedGraph = await loadingGraph.GetOrLoadCachedGraphAsync();
            if (cachedGraph?.PipGraph != null)
            {
                s_loadedCachedGraphs.AddItem(cachedGraph.PipGraph.GraphId, new WeakReference<CachedGraph>(cachedGraph));
            }

            return cachedGraph;
        }
    }

    /// <summary>
    /// Loads data structures for cached graphs
    /// </summary>
    internal sealed class CachedGraphLoader
    {
        private readonly Lazy<Task<Guid>> m_pipGraphIdTask;
        private readonly Lazy<Task<CachedGraph>> m_cachedGraphTask;
        private readonly Lazy<Task<StringTable>> m_stringTableTask;
        private readonly Lazy<Task<PathTable>> m_pathTableTask;
        private readonly Lazy<Task<SymbolTable>> m_symbolTableTask;
        private readonly Lazy<Task<QualifierTable>> m_qualifierTableTask;
        private readonly Lazy<Task<PipTable>> m_pipTableTask;
        private readonly Lazy<Task<MountPathExpander>> m_mountPathExpanderTask;
        private readonly Lazy<Task<PipExecutionContext>> m_pipExecutionContextTask;
        private readonly Lazy<Task<HistoricTableSizes>> m_historicDataTask;
        private readonly Lazy<Task<PipGraph>> m_pipGraphTask;

        /// <summary>
        /// Loads a cache graph from a given cached graph directory
        /// </summary>
        public static CachedGraphLoader CreateFromDisk(CancellationToken cancellationToken, string cachedGraphDirectory, LoggingContext loggingContext, FileSystemStreamProvider readStreamProvider = null)
        {
            var serializer = new EngineSerializer(loggingContext, cachedGraphDirectory, readOnly: true, readStreamProvider: readStreamProvider);
            return new CachedGraphLoader(cancellationToken, serializer);
        }

        /// <summary>
        /// Loads a cache graph with a given serializer
        /// </summary>
        public static CachedGraphLoader CreateFromDisk(CancellationToken cancellationToken, EngineSerializer serializer)
        {
            return new CachedGraphLoader(cancellationToken, serializer);
        }

        /// <summary>
        /// Loads a cache graph from a given engine state
        /// </summary>
        public static CachedGraphLoader CreateFromEngineState(CancellationToken cancellationToken, EngineState engineState)
        {
            return new CachedGraphLoader(cancellationToken, engineState);
        }

        private CachedGraphLoader(CancellationToken cancellationToken, EngineSerializer serializer)
        {
            m_pipGraphIdTask = CreateLazyFileDeserialization(
                serializer,
                GraphCacheFile.PipGraph,
                async reader =>
                {
                    var graphId = await PipGraph.DeserializeGraphIdAsync(reader);
                    return graphId.Item1;
                });

            var directedGraphTask = CreateLazyFileDeserialization(
                serializer,
                GraphCacheFile.DirectedGraph,
                DeserializedDirectedGraph.DeserializeAsync);

            m_stringTableTask = CreateLazyFileDeserialization(
                serializer,
                GraphCacheFile.StringTable,
                StringTable.DeserializeAsync);

            m_pathTableTask = CreateLazyFileDeserialization(
                serializer,
                GraphCacheFile.PathTable,
                reader => reader.ReadPathTableAsync(m_stringTableTask.Value));

            m_symbolTableTask = CreateLazyFileDeserialization(
                serializer,
                GraphCacheFile.SymbolTable,
                reader => reader.ReadSymbolTableAsync(m_stringTableTask.Value));

            m_qualifierTableTask = CreateLazyFileDeserialization(
                serializer,
                GraphCacheFile.QualifierTable,
                reader => reader.ReadQualifierTableAsync(m_stringTableTask.Value));

            m_pipTableTask = CreateLazyFileDeserialization(
                serializer,
                GraphCacheFile.PipTable,
                (reader) => PipTable.DeserializeAsync(
                    reader,
                    m_pathTableTask.Value,
                    m_symbolTableTask.Value,
                    EngineSchedule.PipTableInitialBufferSize,
                    EngineSchedule.PipTableMaxDegreeOfParallelismDuringConstruction,
                    debug: false));

            m_mountPathExpanderTask = CreateLazyFileDeserialization(
                serializer,
                GraphCacheFile.MountPathExpander,
                reader => MountPathExpander.DeserializeAsync(reader, m_pathTableTask.Value));

            m_pipExecutionContextTask = CreateAsyncLazy(() => Task.Run(
                async () =>
                {
                    var stringTable = await m_stringTableTask.Value;
                    var pathTable = await m_pathTableTask.Value;
                    var symbolTable = await m_symbolTableTask.Value;
                    var qualifierTable = await m_qualifierTableTask.Value;

                    if (stringTable != null && pathTable != null && symbolTable != null && qualifierTable != null)
                    {
                        return (PipExecutionContext)new SchedulerContext(cancellationToken, stringTable, pathTable, symbolTable, qualifierTable);
                    }

                    return null;
                }));

            m_historicDataTask = CreateLazyFileDeserialization(
                serializer,
                GraphCacheFile.HistoricTableSizes,
                reader => Task.FromResult(HistoricTableSizes.Deserialize(reader)));

            m_pipGraphTask = CreateLazyFileDeserialization(
                serializer,
                GraphCacheFile.PipGraph,
                reader => PipGraph.DeserializeAsync(
                    reader,
                    serializer.LoggingContext,
                    m_pipTableTask.Value,
                    directedGraphTask.Value,
                    m_pipExecutionContextTask.Value,
                    ToSemanticPathExpander(m_mountPathExpanderTask.Value)));

            m_cachedGraphTask = CreateAsyncLazy(() => CreateCachedGraph(m_pipTableTask.Value, m_pipGraphTask.Value, directedGraphTask.Value, m_pipExecutionContextTask.Value, m_mountPathExpanderTask.Value));
        }

        private CachedGraphLoader(CancellationToken cancellationToken, EngineState engineState)
        {
            var pipGraph = engineState.PipGraph;
            m_stringTableTask = CreateAsyncLazyFromResult(engineState.StringTable);
            m_pathTableTask = CreateAsyncLazyFromResult(engineState.PathTable);
            m_symbolTableTask = CreateAsyncLazyFromResult(engineState.SymbolTable);
            m_qualifierTableTask = CreateAsyncLazyFromResult(engineState.QualifierTable);
            m_pipTableTask = CreateAsyncLazyFromResult(engineState.PipTable);
            m_mountPathExpanderTask = CreateAsyncLazyFromResult(engineState.MountPathExpander);
            m_pipExecutionContextTask = CreateAsyncLazyFromResult((PipExecutionContext)new SchedulerContext(cancellationToken, engineState.StringTable, engineState.PathTable, engineState.SymbolTable, engineState.QualifierTable));
            m_historicDataTask = CreateAsyncLazyFromResult(engineState.HistoricTableSizes);
            m_pipGraphTask = CreateAsyncLazyFromResult(pipGraph);
            m_cachedGraphTask = CreateAsyncLazyFromResult(new CachedGraph(pipGraph, pipGraph.DataflowGraph, m_pipExecutionContextTask.Value.Result, engineState.MountPathExpander));
        }

        private static async Task<CachedGraph> CreateCachedGraph(Task<PipTable> pipTableTask, Task<PipGraph> pipGraphTask, Task<DeserializedDirectedGraph> directedGraphTask, Task<PipExecutionContext> contextTask, Task<MountPathExpander> mountPathExpanderTask)
        {
            var pipGraph = await pipGraphTask;
            var directedGraph = await directedGraphTask;
            var context = await contextTask;
            var mountPathExpander = await mountPathExpanderTask;
            if (pipGraph == null ||
                directedGraph == null ||
                context == null ||
                mountPathExpander == null)
            {
                var pipTable = await pipTableTask;
                if (pipTable != null)
                {
                    pipTable.Dispose();
                }

                return null;
            }

            return new CachedGraph(pipGraph, directedGraph, context, mountPathExpander);
        }

        private static async Task<SemanticPathExpander> ToSemanticPathExpander(Task<MountPathExpander> mountPathExpander)
        {
            return await mountPathExpander;
        }

        private static Lazy<Task<T>> CreateLazyFileDeserialization<T>(EngineSerializer serializer, GraphCacheFile file, Func<BuildXLReader, Task<T>> deserializer)
        {
            return CreateAsyncLazy(() => serializer.DeserializeFromFileAsync<T>(file, deserializer));
        }

        private static Lazy<T> CreateAsyncLazy<T>(Func<T> taskCreator) where T : Task
        {
            return new Lazy<T>(taskCreator, isThreadSafe: true);
        }

        private static Lazy<Task<T>> CreateAsyncLazyFromResult<T>(T obj)
        {
            return Lazy.Create(() => Task.FromResult<T>(obj));
        }

        internal Task<PathTable> GetOrLoadPathTableAsync()
        {
            return m_pathTableTask.Value;
        }

        internal Task<SymbolTable> GetOrLoadSymbolTableAsync()
        {
            return m_symbolTableTask.Value;
        }

        internal Task<PipTable> GetOrLoadPipTableAsync()
        {
            return m_pipTableTask.Value;
        }

        internal Task<PipExecutionContext> GetOrLoadPipExecutionContextAsync()
        {
            return m_pipExecutionContextTask.Value;
        }

        internal Task<HistoricTableSizes> GetOrLoadHistoricDataAsync()
        {
            return m_historicDataTask.Value;
        }

        internal Task<PipGraph> GetOrLoadPipGraphAsync()
        {
            return m_pipGraphTask.Value;
        }

        internal Task<MountPathExpander> GetOrLoadMountPathExpanderAsync()
        {
            return m_mountPathExpanderTask.Value;
        }

        internal Task<Guid> GetOrLoadPipGraphIdAsync()
        {
            return m_pipGraphIdTask.Value;
        }

        internal Task<CachedGraph> GetOrLoadCachedGraphAsync()
        {
            return m_cachedGraphTask.Value;
        }
    }
}
