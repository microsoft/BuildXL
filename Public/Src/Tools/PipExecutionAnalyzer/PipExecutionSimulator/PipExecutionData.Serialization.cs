// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PipExecutionSimulator
{
    public partial class PipExecutionData
    {
        private ConcurrentBigMap<int, NodeId> m_pipProducers = new ConcurrentBigMap<int, NodeId>();
        private ConcurrentBigMap<int, NodeId> m_pipIdToNode = new ConcurrentBigMap<int, NodeId>();
        private ConcurrentDenseIndex<List<int>> m_pipConsumes = new ConcurrentDenseIndex<List<int>>(false);
        private Guid VersionGuid = new Guid("{4DC5CEA4-D59D-4BD3-B1BE-6A711C1D5C81}");

        public void ReadAndCacheJsonGraph(string path)
        {
            ReadJsonGraph(path, Path.ChangeExtension(path, ".processed.bin"));
        }

        public void ReadJsonGraph(string path, string targetPath = null, bool loadTargetIfApplicable = true)
        {
            if (loadTargetIfApplicable && targetPath != null && File.Exists(targetPath))
            {
                if (Deserialize(targetPath))
                {
                    return;
                }
            }

            MutableDataflowGraph = new MutableDirectedGraph();
            DataflowGraph = MutableDataflowGraph;
            using (var stream = new ProgressStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 << 10), "JsonData"))
            using (var textReader = new StreamReader(stream))
            using (var textWriter = new StreamWriter(Path.ChangeExtension(targetPath, ".filter.json")))
            using (var reader = new JsonFilter(path, new JsonTextReader(textReader), new JsonTextWriter(textWriter)))
            {
                AssertPostRead(reader, JsonToken.StartObject);
                while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
                {
                    string propertyName = (string)reader.Value;
                    switch (propertyName)
                    {
                        case "graph":
                            ReadGraph(reader);
                            break;
                        case "execution":
                            ReadExecutions(reader);
                            break;
                        case "pips":
                            ReadPips(reader);
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
            }

            AddEdges();

            Compute();

            if (targetPath != null)
            {
                Serialize(targetPath);
            }
        }

        private void AddEdges()
        {
            var nodes = MutableDataflowGraph.Nodes.ToArray();
            int count = 0;

            Parallel.ForEach(nodes, node =>
            {
                var consumes = m_pipConsumes[node.Value];
                if (consumes != null)
                {
                    foreach (var consumed in consumes)
                    {
                        var producer = m_pipProducers.GetOrAdd(consumed, MutableDataflowGraph, (consumed0, graph) =>
                            {
                                var sourceNode = graph.CreateNode();
                                PipTypes[sourceNode] = PipType.HashSourceFile;
                                return sourceNode;
                            }).Item.Value;

                        MutableDataflowGraph.AddEdge(producer, node);
                    }

                    int incrementedCount = Interlocked.Increment(ref count);
                    if ((incrementedCount % 10000) == 0)
                    {
                        Console.WriteLine("Added edges: {0} / {1} ({2} %)", incrementedCount, nodes.Length, ((double)incrementedCount * 100) / nodes.Length);
                    }
                }
            });
        }

        public void Serialize(string path)
        {
            Console.WriteLine("Serializing data...");
            using (var fs = File.Open(path, FileMode.OpenOrCreate))
            using (var writer = new BuildXLWriter(false, fs, false, false))
            {
                writer.Write(VersionGuid);
                var maxNode = (uint)DataflowGraph.NodeCount;
                DataflowGraph.Serialize(writer);
                //writer.Write(maxNode, AggregateCosts, (w, i) => w.Write(i));
                writer.Write(maxNode, PipIds, (w, i) => w.Write(i));
                writer.Write(maxNode, SemiStableHashes, (w, i) => w.Write(i));
                writer.Write(maxNode, StartTimes, (w, i) => w.Write(i));
                writer.Write(maxNode, Durations, (w, i) => w.Write(i));
                writer.Write(maxNode, PipTypes, (w, i) => w.Write((byte)i));
                //writer.Write(maxNode, CriticalChain, WriteNode);
                writer.Write(MinStartTime);
                writer.Write(MaxEndTime);
                writer.Write(TotalDuration);
                //writer.Write(MaxAggregateCost);
                //WriteNode(writer, CriticalPathHeadNode);
                //writer.Write(CriticalPath, WriteNode);
                SymbolTable.StringTable.Serialize(writer);
                SymbolTable.Serialize(writer);
                writer.Write(maxNode, OutputValues, (w, i) => w.Write(i));
            }

            Console.WriteLine("End serializing data");
        }

        public bool Deserialize(string path)
        {
            Console.WriteLine("Deserializing data...");
            using (var fs = new ProgressStream(File.Open(path, FileMode.Open), "DeserializedData"))
            using (var reader = new BuildXLReader(false, fs, false))
            {
                Guid readVersion = reader.ReadGuid();
                if (readVersion != VersionGuid)
                {
                    return false;
                }

                DataflowGraph = DeserializedDirectedGraph.DeserializeAsync(reader).Result;
                var maxNode = (uint)DataflowGraph.NodeCount;
                //reader.Read(maxNode, AggregateCosts, r => r.ReadUInt64());
                reader.Read(maxNode, PipIds, r => r.ReadInt32());
                reader.Read(maxNode, SemiStableHashes, r => r.ReadUInt64());
                reader.Read(maxNode, StartTimes, r => r.ReadUInt64());
                reader.Read(maxNode, Durations, r => r.ReadUInt64());
                reader.Read(maxNode, PipTypes, r => (PipType)r.ReadByte());
                //reader.Read(maxNode, CriticalChain, ReadNode);
                MinStartTime = reader.ReadUInt64();
                MaxEndTime = reader.ReadUInt64();
                TotalDuration = reader.ReadUInt64();
                //MaxAggregateCost = reader.ReadUInt64();
                //CriticalPathHeadNode = ReadNode(reader);
                //reader.Read(CriticalPath, ReadNode);
                var stringTable = StringTable.DeserializeAsync(reader).Result;
                SymbolTable = SymbolTable.DeserializeAsync(reader, Task.FromResult(stringTable)).Result;
                reader.Read(maxNode, OutputValues, r => r.ReadFullSymbol());
            }

            Console.WriteLine("End deserializing data");
            Compute();
            return true;
        }

        public void Compute()
        {
            Console.WriteLine("Computing actual concurrency...");
            ComputeActualConcurrency();
            Console.WriteLine("End computing actual concurrency");
            Console.WriteLine("Computing aggregate costs...");
            ComputeAggregateCosts();
            Console.WriteLine("End computing aggregate costs");
            //foreach (var node in DataflowGraph.Nodes)
            //{
            //    StartTimes[node] = Math.Max(MinStartTime, StartTimes[node]);
            //}
        }

        public static NodeId ReadNode(BinaryReader reader)
        {
            return new NodeId(reader.ReadUInt32());
        }

        public static void WriteNode(BinaryWriter writer, NodeId node)
        {
            writer.Write(node.Value);
        }

        public void ReadPips(JsonFilter reader)
        {
            Dictionary<string, PipType> pipTypeLookup = new Dictionary<string, PipType>();
            foreach (PipType pt in Enum.GetValues(typeof(PipType)))
            {
                pipTypeLookup[pt.ToString()] = pt;
            }

            AssertPostRead(reader, JsonToken.StartArray);
            while (reader.Read() && reader.TokenType == JsonToken.StartObject)
            {
                AssertPostRead(reader, JsonToken.PropertyName);
                var id = (uint)reader.ReadAsInt32();

                while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
                {
                    string propertyName = (string)reader.Value;
                    switch (propertyName)
                    {
                        case "type":
                            PipTypes[id] = pipTypeLookup[reader.ReadAsString()];
                            break;
                        case "stableId":
                            SemiStableHashes[id] = ulong.Parse(reader.ReadAsString(), NumberStyles.HexNumber);
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }

                AssertToken(reader, JsonToken.EndObject);
            }

            AssertToken(reader, JsonToken.EndArray);
        }

        public void ReadExecutions(JsonFilter reader)
        {
            AssertPostRead(reader, JsonToken.StartArray);
            while (reader.Read() && reader.TokenType == JsonToken.StartObject)
            {
                AssertPostRead(reader, JsonToken.PropertyName);
                var id = reader.ReadAsInt32().Value;
                var node = GetNodeForId(id);

                ulong? startTime = null, endTime = null;

                while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
                {
                    string propertyName = (string)reader.Value;
                    switch (propertyName)
                    {
                        case "startTime":
                            startTime = reader.ReadAsUInt64();
                            break;
                        case "endTime":
                            endTime = reader.ReadAsUInt64();
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }

                Contract.Assert(startTime.HasValue);
                Contract.Assert(endTime.HasValue);
                Min(ref MinStartTime, startTime.Value);
                Max(ref MaxEndTime, endTime.Value);
                StartTimes[node] = startTime.Value;
                Durations[node] = endTime.Value - startTime.Value;

                AssertToken(reader, JsonToken.EndObject);
            }

            TotalDuration = MaxEndTime - MinStartTime;

            AssertToken(reader, JsonToken.EndArray);
        }

        private NodeId GetNodeForId(int id)
        {
            NodeId node;
            if (!m_pipIdToNode.TryGetValue(id, out node))
            {
                node = MutableDataflowGraph.CreateNode();
                m_pipIdToNode[id] = node;
                PipIds[node] = id;
            }

            return node;
        }

        public void ReadGraph(JsonFilter reader)
        {
            Dictionary<string, PipType> pipTypeLookup = new Dictionary<string, PipType>();
            foreach (PipType pt in Enum.GetValues(typeof(PipType)))
            {
                pipTypeLookup[pt.ToString()] = pt;
            }

            AssertPostRead(reader, JsonToken.StartArray);
            while (reader.Read() && reader.TokenType == JsonToken.StartObject)
            {
                AssertPostRead(reader, JsonToken.PropertyName);
                var id = (int)reader.ReadAsInt32();
                NodeId node = GetNodeForId(id);


                while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
                {
                    int value = 0;
                    string propertyName = (string)reader.Value;
                    switch (propertyName)
                    {
                        case "type":
                            PipTypes[node] = pipTypeLookup[reader.ReadAsString()];
                            break;
                        case "stableId":
                            SemiStableHashes[node] = ulong.Parse(reader.ReadAsString(), NumberStyles.HexNumber);
                            break;
                        case "dependsOn":
                            AssertPostRead(reader, JsonToken.StartArray);
                            while (reader.TryReadInt32(out value))
                            {
                                MutableDataflowGraph.AddEdge(GetNodeForId(value), node);
                            }

                            AssertToken(reader, JsonToken.EndArray);
                            break;
                        case "consumes":
                            AssertPostRead(reader, JsonToken.StartArray);
                            while (reader.TryReadInt32(out value))
                            {
                                var list = GetOrAddDependenciesList(node);
                                list.Add(value);
                            }

                            AssertToken(reader, JsonToken.EndArray);
                            break;
                        case "produces":
                            AssertPostRead(reader, JsonToken.StartArray);
                            while (reader.TryReadInt32(out value))
                            {
                                m_pipProducers[value] = node;
                            }

                            AssertToken(reader, JsonToken.EndArray);
                            break;
                        case "provenance":
                        AssertPostRead(reader, JsonToken.StartObject);
                        while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
                        {
                            if (reader.Value.Equals("value"))
                            {
                                var outputValue = FullSymbol.Create(SymbolTable, reader.ReadAsString());
                                OutputValues[node] = outputValue;
                                continue;
                            }

                            reader.Skip();
                        }

                        AssertToken(reader, JsonToken.EndObject);
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }

                AssertToken(reader, JsonToken.EndObject);
            }

            AssertToken(reader, JsonToken.EndArray);
        }

        private List<int> GetOrAddDependenciesList(NodeId node)
        {
            var list = m_pipConsumes[node.Value];
            if (list == null)
            {
                list = new List<int>();
                m_pipConsumes[node.Value] = list;
            }

            return list;
        }

        public void AssertToken(JsonFilter reader, JsonToken expectedTokenType)
        {
            Contract.Assert(reader.TokenType == expectedTokenType, "Unexpected token");
        }

        public void AssertPreRead(JsonFilter reader, JsonToken expectedTokenType)
        {
            Contract.Assert(reader.TokenType == expectedTokenType, "Unexpected token");
            reader.Read();
        }

        public void AssertPostRead(JsonFilter reader, JsonToken expectedTokenType)
        {
            reader.Read();
            Contract.Assert(reader.TokenType == expectedTokenType, "Unexpected token");
        }
    }

    public static class ExtensionMethods
    {
        public static uint ValueAsUint(this JsonFilter reader)
        {
            return (uint)(long)reader.Value;
        }

        public static bool TryReadInt32(this JsonFilter reader, out int value)
        {
            var possibleValue = reader.ReadAsInt32();
            if (possibleValue.HasValue)
            {
                value = possibleValue.Value;
                return true;
            }

            value = 0;
            return false;
        }

        public static int ValueAsInt(this JsonFilter reader)
        {
            return (int)(long)reader.Value;
        }

        public static ulong ReadAsUInt64(this JsonFilter reader)
        {
            Contract.Assert(reader.Read());
            Contract.Assert(reader.TokenType == JsonToken.Integer);
            return (ulong)(long)reader.Value;
        }

        public static void Read<T>(this BuildXLReader reader, uint maxNode, ConcurrentNodeDictionary<T> map, Func<BuildXLReader, T> readItem)
        {
            for (uint i = NodeId.MinValue; i <= maxNode; i++)
            {
                map[i] = readItem(reader);
            }
        }

        public static void Write<T>(this BuildXLWriter writer, uint maxNode, ConcurrentNodeDictionary<T> map, Action<BuildXLWriter, T> writeItem)
        {
            for (uint i = NodeId.MinValue; i <= maxNode; i++)
            {
                writeItem(writer, map[i]);
            }
        }

        public static void Read<T>(this BuildXLReader reader, ICollection<T> collection, Func<BuildXLReader, T> readItem)
        {
            var count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                collection.Add(readItem(reader));
            }
        }

        public static void Write<T>(this BuildXLWriter writer, ICollection<T> collection, Action<BuildXLWriter, T> writeItem)
        {
            writer.Write(collection.Count);
            foreach (var item in collection)
            {
                writeItem(writer, item);
            }
        }
    }
}
