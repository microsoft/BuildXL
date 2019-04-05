// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.Graph
{
    public sealed partial class PipGraph
    {
        /// <summary>
        /// Captures all state which is serialized for a pip graph
        /// </summary>
        private sealed class SerializedState
        {
            public readonly Guid GraphId;
            public readonly ConcurrentBigMap<(FullSymbol, QualifierId, AbsolutePath), NodeId> Values;
            public readonly ConcurrentBigMap<FileArtifact, NodeId> SpecFiles;
            public readonly ConcurrentBigMap<ModuleId, NodeId> Modules;
            public readonly ConcurrentBigMap<FileArtifact, NodeId> PipProducers;
            public readonly ConcurrentBigMap<DirectoryArtifact, NodeId> OpaqueDirectoryProducers;
            public readonly ConcurrentBigMap<AbsolutePath, bool> OutputDirectoryRoots;
            public readonly ConcurrentBigMap<DirectoryArtifact, NodeId> CompositeOutputDirectoryProducers;
            public readonly ConcurrentBigMap<AbsolutePath, DirectoryArtifact> SourceSealedDirectoryRoots;
            public readonly ConcurrentBigMap<AbsolutePath, PipId> TemporaryPaths;
            public readonly ConcurrentBigMap<DirectoryArtifact, NodeId> SealDirectoryNodes;
            public readonly ConcurrentBigSet<PipId> RewritingPips;
            public readonly ConcurrentBigSet<PipId> RewrittenPips;
            public readonly ConcurrentBigMap<AbsolutePath, int> LatestWriteCountsByPath;
            public readonly ConcurrentBigMap<PipId, ConcurrentBigSet<PipId>> ServicePipClients;
            public readonly StringId ApiServerMoniker;
            public readonly int MaxAbsolutePath;
            public readonly ContentFingerprint SemistableProcessFingerprint;
            public readonly PipGraphStaticFingerprints PipStaticFingerprints;

            /// <summary>
            /// Initialize state from pip graph builder
            /// </summary>
            public SerializedState(
                ConcurrentBigMap<(FullSymbol, QualifierId, AbsolutePath), NodeId> values,
                ConcurrentBigMap<FileArtifact, NodeId> specFiles,
                ConcurrentBigMap<ModuleId, NodeId> modules,
                ConcurrentBigMap<FileArtifact, NodeId> pipProducers,
                ConcurrentBigMap<DirectoryArtifact, NodeId> opaqueDirectoryProducers,
                ConcurrentBigMap<AbsolutePath, bool> outputDirectoryRoots,
                ConcurrentBigMap<DirectoryArtifact, NodeId> compositeSharedOpaqueProducers,
                ConcurrentBigMap<AbsolutePath, DirectoryArtifact> sourceSealedDirectoryRoots,
                ConcurrentBigMap<AbsolutePath, PipId> temporaryPaths,
                ConcurrentBigMap<DirectoryArtifact, NodeId> sealDirectoryNodes,
                ConcurrentBigSet<PipId> rewritingPips,
                ConcurrentBigSet<PipId> rewrittenPips,
                ConcurrentBigMap<AbsolutePath, int> latestWriteCountsByPath,
                ConcurrentBigMap<PipId, ConcurrentBigSet<PipId>> servicePipClients,
                StringId apiServerMoniker,
                int maxAbsolutePath,
                ContentFingerprint semistableProcessFingerprint,
                PipGraphStaticFingerprints pipStaticFingerprints)
                : this(
                    graphId: Guid.NewGuid(),
                    values: values,
                    specFiles: specFiles,
                    modules: modules,
                    pipProducers: pipProducers,
                    opaqueDirectoryProducers: opaqueDirectoryProducers,
                    outputDirectoryRoots: outputDirectoryRoots,
                    compositeOutputDirectoryProducers: compositeSharedOpaqueProducers,
                    sourceSealedDirectoryRoots: sourceSealedDirectoryRoots,
                    temporaryPaths: temporaryPaths,
                    sealDirectoryNodes: sealDirectoryNodes,
                    rewritingPips: rewritingPips,
                    rewrittenPips: rewrittenPips,
                    latestWriteCountsByPath: latestWriteCountsByPath,
                    servicePipClients: servicePipClients,
                    apiServerMoniker: apiServerMoniker,
                    maxAbsolutePath: maxAbsolutePath,
                    semistableProcessFingerprint: semistableProcessFingerprint,
                    pipStaticFingerprints: pipStaticFingerprints)
            {
            }

            /// <summary>
            /// Initialize state from deserializing
            /// </summary>
            private SerializedState(
                Guid graphId,
                ConcurrentBigMap<(FullSymbol, QualifierId, AbsolutePath), NodeId> values,
                ConcurrentBigMap<FileArtifact, NodeId> specFiles,
                ConcurrentBigMap<ModuleId, NodeId> modules,
                ConcurrentBigMap<FileArtifact, NodeId> pipProducers,
                ConcurrentBigMap<DirectoryArtifact, NodeId> opaqueDirectoryProducers,
                ConcurrentBigMap<AbsolutePath, bool> outputDirectoryRoots,
                ConcurrentBigMap<DirectoryArtifact, NodeId> compositeOutputDirectoryProducers,
                ConcurrentBigMap<AbsolutePath, DirectoryArtifact> sourceSealedDirectoryRoots,
                ConcurrentBigMap<AbsolutePath, PipId> temporaryPaths,
                ConcurrentBigMap<DirectoryArtifact, NodeId> sealDirectoryNodes,
                ConcurrentBigSet<PipId> rewritingPips,
                ConcurrentBigSet<PipId> rewrittenPips,
                ConcurrentBigMap<AbsolutePath, int> latestWriteCountsByPath,
                ConcurrentBigMap<PipId, ConcurrentBigSet<PipId>> servicePipClients,
                StringId apiServerMoniker,
                int maxAbsolutePath,
                ContentFingerprint semistableProcessFingerprint,
                PipGraphStaticFingerprints pipStaticFingerprints)
            {
                GraphId = graphId;
                Values = values;
                SpecFiles = specFiles;
                Modules = modules;
                PipProducers = pipProducers;
                OpaqueDirectoryProducers = opaqueDirectoryProducers;
                OutputDirectoryRoots = outputDirectoryRoots;
                CompositeOutputDirectoryProducers = compositeOutputDirectoryProducers;
                SourceSealedDirectoryRoots = sourceSealedDirectoryRoots;
                TemporaryPaths = temporaryPaths;
                SealDirectoryNodes = sealDirectoryNodes;
                RewritingPips = rewritingPips;
                RewrittenPips = rewrittenPips;
                LatestWriteCountsByPath = latestWriteCountsByPath;
                ServicePipClients = servicePipClients;
                ApiServerMoniker = apiServerMoniker;
                MaxAbsolutePath = maxAbsolutePath;
                SemistableProcessFingerprint = semistableProcessFingerprint;
                PipStaticFingerprints = pipStaticFingerprints;
            }

            /// <summary>
            /// Initialize state from pip graph in preparation for serialization
            /// </summary>
            public SerializedState(PipGraph graph)
                : this(
                    graphId: graph.GraphId,
                    values: graph.Values,
                    specFiles: graph.SpecFiles,
                    modules: graph.Modules,
                    pipProducers: graph.PipProducers,
                    opaqueDirectoryProducers: graph.OutputDirectoryProducers,
                    outputDirectoryRoots: graph.OutputDirectoryRoots,
                    compositeOutputDirectoryProducers: graph.CompositeOutputDirectoryProducers,
                    sourceSealedDirectoryRoots: graph.SourceSealedDirectoryRoots,
                    temporaryPaths: graph.TemporaryPaths,
                    sealDirectoryNodes: graph.m_sealedDirectoryNodes,
                    rewritingPips: graph.RewritingPips,
                    rewrittenPips: graph.RewrittenPips,
                    latestWriteCountsByPath: graph.LatestWriteCountsByPath,
                    servicePipClients: graph.m_servicePipClients,
                    apiServerMoniker: graph.ApiServerMoniker,
                    maxAbsolutePath: graph.MaxAbsolutePathIndex,
                    semistableProcessFingerprint: graph.SemistableFingerprint,
                    pipStaticFingerprints: graph.PipStaticFingerprints)
            {
            }

            /// <summary>
            /// Initialize state by deserializing
            /// </summary>
            public static async Task<SerializedState> ReadAsync(BuildXLReader reader)
            {
                var graphId = reader.ReadGuid();
                var semistableProcessFingerprint = new ContentFingerprint(reader);

                var pipProducers = ConcurrentBigMap<FileArtifact, NodeId>.Deserialize(
                    reader,
                    () => new KeyValuePair<FileArtifact, NodeId>(
                        reader.ReadFileArtifact(),
                        new NodeId(reader.ReadUInt32())));

                var opaqueDirectoryProducers = ConcurrentBigMap<DirectoryArtifact, NodeId>.Deserialize(
                    reader,
                    () => new KeyValuePair<DirectoryArtifact, NodeId>(
                        reader.ReadDirectoryArtifact(),
                        new NodeId(reader.ReadUInt32())));

                var outputDirectoryRoots = ConcurrentBigMap<AbsolutePath, bool>.Deserialize(
                    reader,
                    () => new KeyValuePair<AbsolutePath, bool>(
                        reader.ReadAbsolutePath(),
                        reader.ReadBoolean()));

                var compositeOutputDirectoryProducers = ConcurrentBigMap<DirectoryArtifact, NodeId>.Deserialize(
                    reader,
                    () => new KeyValuePair<DirectoryArtifact, NodeId>(
                        reader.ReadDirectoryArtifact(),
                        new NodeId(reader.ReadUInt32())));

                var latestWriteCountsByPathTask = Task.Factory.StartNew(
                    () =>
                    {
                        var d = new Dictionary<AbsolutePath, int>(capacity: pipProducers.Count);

                        // Calculated state
                        foreach (var kvp in pipProducers)
                        {
                            FileArtifact fileArtifact = kvp.Key;

                            // Compute latest write count based on pip producers
                            int latestWriteCount;
                            if (d.TryGetValue(fileArtifact.Path, out latestWriteCount))
                            {
                                latestWriteCount = Math.Max(latestWriteCount, fileArtifact.RewriteCount);
                            }
                            else
                            {
                                latestWriteCount = fileArtifact.RewriteCount;
                            }

                            d[fileArtifact.Path] = latestWriteCount;
                        }

                        return ConcurrentBigMap<AbsolutePath, int>.Create(capacity: d.Count, items: d);
                    });

                var values = ConcurrentBigMap<(FullSymbol, QualifierId, AbsolutePath), NodeId>.Deserialize(
                    reader,
                    () =>
                        new KeyValuePair<(FullSymbol, QualifierId, AbsolutePath), NodeId>(
                               (
                                reader.ReadFullSymbol(),
                                reader.ReadQualifierId(),
                                reader.ReadAbsolutePath()),
                            new NodeId(reader.ReadUInt32())));

                var specFiles = ConcurrentBigMap<FileArtifact, NodeId>.Deserialize(
                    reader,
                    () =>
                        new KeyValuePair<FileArtifact, NodeId>(
                            reader.ReadFileArtifact(),
                            new NodeId(reader.ReadUInt32())));

                var modules = ConcurrentBigMap<ModuleId, NodeId>.Deserialize(
                    reader,
                    () =>
                        new KeyValuePair<ModuleId, NodeId>(
                            reader.ReadModuleId(),
                            new NodeId(reader.ReadUInt32())));

                var sourceSealedDirectoryRoots = ConcurrentBigMap<AbsolutePath, DirectoryArtifact>.Deserialize(
                    reader,
                    () =>
                    {
                        var path = reader.ReadAbsolutePath();
                        var directoryArtifact = reader.ReadDirectoryArtifact();
                        return new KeyValuePair<AbsolutePath, DirectoryArtifact>(path, directoryArtifact);
                    });

                var temporaryPaths = ConcurrentBigMap<AbsolutePath, PipId>.Deserialize(
                    reader,
                    () =>
                    {
                        var path = reader.ReadAbsolutePath();
                        var pipId = PipId.Deserialize(reader);
                        return new KeyValuePair<AbsolutePath, PipId>(path, pipId);
                    });

                var sealDirectoryNodes = ConcurrentBigMap<DirectoryArtifact, NodeId>.Deserialize(
                    reader,
                    () =>
                        {
                            var directory = reader.ReadDirectoryArtifact();
                            var nodeId = reader.ReadUInt32();
                            return new KeyValuePair<DirectoryArtifact, NodeId>(directory, directory.IsValid ? new NodeId(nodeId) : NodeId.Invalid);
                        });

                var rewritingPips = ConcurrentBigSet<PipId>.Deserialize(reader, () => PipId.Deserialize(reader));
                var rewrittenPips = ConcurrentBigSet<PipId>.Deserialize(reader, () => PipId.Deserialize(reader));

                var servicePipClients = ConcurrentBigMap<PipId, ConcurrentBigSet<PipId>>.Deserialize(
                    reader,
                    () =>
                        {
                            var servicePipId = PipId.Deserialize(reader);
                            var clientPipIds = ConcurrentBigSet<PipId>.Deserialize(reader, () => PipId.Deserialize(reader));
                            return new KeyValuePair<PipId, ConcurrentBigSet<PipId>>(servicePipId, clientPipIds);
                        });

                var apiServerMoniker = reader.ReadStringId();

                var pipStaticFingerprints = PipGraphStaticFingerprints.Deserialize(reader);

                var maxAbsolutePath = reader.ReadInt32Compact();

                var latestWriteCountsByPath = await latestWriteCountsByPathTask;

                return new SerializedState(
                    graphId: graphId,
                    values: values,
                    specFiles: specFiles,
                    modules: modules,
                    pipProducers: pipProducers,
                    opaqueDirectoryProducers: opaqueDirectoryProducers,
                    outputDirectoryRoots: outputDirectoryRoots,
                    compositeOutputDirectoryProducers: compositeOutputDirectoryProducers,
                    sourceSealedDirectoryRoots: sourceSealedDirectoryRoots,
                    temporaryPaths: temporaryPaths,
                    sealDirectoryNodes: sealDirectoryNodes,
                    rewritingPips: rewritingPips,
                    rewrittenPips: rewrittenPips,
                    latestWriteCountsByPath: latestWriteCountsByPath,
                    servicePipClients: servicePipClients,
                    apiServerMoniker: apiServerMoniker,
                    maxAbsolutePath: maxAbsolutePath,
                    semistableProcessFingerprint: semistableProcessFingerprint,
                    pipStaticFingerprints: pipStaticFingerprints);
            }

            public void Serialize(BuildXLWriter writer)
            {
                writer.Write(GraphId);
                SemistableProcessFingerprint.WriteTo(writer);

                PipProducers.Serialize(
                    writer,
                    kvp =>
                    {
                        writer.Write(kvp.Key);
                        writer.Write(kvp.Value.Value);
                    });

                OpaqueDirectoryProducers.Serialize(
                    writer,
                    kvp =>
                    {
                        writer.Write(kvp.Key);
                        writer.Write(kvp.Value.Value);
                    });

                OutputDirectoryRoots.Serialize(
                    writer,
                    kvp =>
                    {
                        writer.Write(kvp.Key);
                        writer.Write(kvp.Value);
                    });

                CompositeOutputDirectoryProducers.Serialize(
                    writer,
                    kvp =>
                    {
                        writer.Write(kvp.Key);
                        writer.Write(kvp.Value.Value);
                    });

                Values.Serialize(
                    writer,
                    kvp =>
                    {
                        writer.Write(kvp.Key.Item1);
                        writer.Write(kvp.Key.Item2);
                        writer.Write(kvp.Key.Item3);
                        writer.Write(kvp.Value.Value);
                    });

                SpecFiles.Serialize(
                    writer,
                    kvp =>
                    {
                        writer.Write(kvp.Key);
                        writer.Write(kvp.Value.Value);
                    });

                Modules.Serialize(
                    writer,
                    kvp =>
                    {
                        writer.Write(kvp.Key);
                        writer.Write(kvp.Value.Value);
                    });

                SourceSealedDirectoryRoots.Serialize(
                    writer,
                    kvp =>
                    {
                        writer.Write(kvp.Key);
                        writer.Write(kvp.Value);
                    });

                TemporaryPaths.Serialize(
                    writer,
                    kvp =>
                    {
                        writer.Write(kvp.Key);
                        kvp.Value.Serialize(writer);
                    });

                SealDirectoryNodes.Serialize(
                    writer,
                    kvp =>
                    {
                        writer.Write(kvp.Key);
                        writer.Write(kvp.Value.Value);
                    });

                RewritingPips.Serialize(writer, pipId => pipId.Serialize(writer));
                RewrittenPips.Serialize(writer, pipId => pipId.Serialize(writer));
                ServicePipClients.Serialize(writer, kvp =>
                {
                    kvp.Key.Serialize(writer);
                    kvp.Value.Serialize(writer, pipId => pipId.Serialize(writer));
                });

                writer.Write(ApiServerMoniker);

                PipStaticFingerprints.Serialize(writer);

                writer.WriteCompact(MaxAbsolutePath);
            }
        }

        /// <summary>
        /// Serializes
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            var serializedState = new SerializedState(this);
            serializedState.Serialize(writer);
        }

        /// <summary>
        /// Serializes the only GraphId guid and semistable process id
        /// </summary>
        public void SerializeGraphId(BuildXLWriter writer)
        {
            writer.Write(GraphId);
            SemistableFingerprint.WriteTo(writer);
        }

        /// <summary>
        /// Deserializes the only GraphId guid and semistable process id
        /// </summary>
        public static Task<Tuple<Guid, ContentFingerprint>> DeserializeGraphIdAsync(BuildXLReader reader)
        {
            Contract.Requires(reader != null);
            var graphId = reader.ReadGuid();
            var semistableProcessFingerprint = new ContentFingerprint(reader);

            return Task.FromResult(Tuple.Create(graphId, semistableProcessFingerprint));
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        public static async Task<PipGraph> DeserializeAsync(
            BuildXLReader reader,
            LoggingContext loggingContext,
            Task<PipTable> pipTableTask,
            Task<DeserializedDirectedGraph> directedGraphTask,
            Task<PipExecutionContext> contextTask,
            Task<SemanticPathExpander> semanticPathExpanderTask)
        {
            Contract.Requires(reader != null);
            Contract.Requires(loggingContext != null);
            Contract.Requires(pipTableTask != null);
            Contract.Requires(directedGraphTask != null);
            Contract.Requires(contextTask != null);
            Contract.Requires(semanticPathExpanderTask != null);

            var deserializedStateTask = SerializedState.ReadAsync(reader);

            var pipTable = await pipTableTask;
            var context = await contextTask;
            var semanticPathExpander = await semanticPathExpanderTask;
            var directedGraph = await directedGraphTask;
            var deserializedState = await deserializedStateTask;
            if (pipTable != null &&
                context != null &&
                semanticPathExpander != null &&
                directedGraph != null &&
                deserializedState != null)
            {
                var graph = new PipGraph(
                    deserializedState,
                    directedGraph,
                    pipTable,
                    context,
                    semanticPathExpander);

                return graph;
            }

            return null;
        }
    }
}
