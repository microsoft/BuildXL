// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using BuildXL.Engine.Cache.Serialization;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;
using Newtonsoft.Json.Linq;
using static BuildXL.Scheduler.Tracing.FingerprintStore;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Helper class for reading fingerprint store entries and performing post-retrieval formatting and logging.
    /// </summary>
    public sealed class FingerprintStoreReader : IDisposable
    {
        /// <summary>
        /// The underlying <see cref="FingerprintStore"/> for finer-grained access to data.
        /// </summary>
        public FingerprintStore Store { get; private set; }

        /// <summary>
        /// Directory for outputting individual pip information.
        /// </summary>
        private readonly string m_outputDirectory;

        /// <summary>
        /// Version of the store opened.
        /// </summary>
        public int StoreVersion => Store.StoreVersion;

        /// <summary>
        /// Constructor helper method
        /// </summary>
        public static Possible<FingerprintStoreReader> Create(string storeDirectory, string outputDirectory)
        {
            var possibleStore = FingerprintStore.Open(storeDirectory, readOnly: true);
            if (possibleStore.Succeeded)
            {
                return new FingerprintStoreReader(possibleStore.Result, outputDirectory);
            }

            return possibleStore.Failure;
        }

        private FingerprintStoreReader(FingerprintStore store, string outputDirectory)
        {
            Contract.Requires(store != null);
            Contract.Requires(!string.IsNullOrEmpty(outputDirectory));

            Store = store;
            m_outputDirectory = outputDirectory;
            Directory.CreateDirectory(m_outputDirectory);
        }

        /// <summary>
        /// Calls through to <see cref="FingerprintStore.TryGetCacheMissList(out IReadOnlyList{PipCacheMissInfo})"/>.
        /// </summary>
        public bool TryGetCacheMissList(out IReadOnlyList<PipCacheMissInfo> cacheMissList)
        {
            return Store.TryGetCacheMissList(out cacheMissList);
        }

        /// <summary>
        /// While the returned <see cref="PipRecordingSession"/> is in scope,
        /// records all the information retrieved from the <see cref="FingerprintStore"/>
        /// to per-pip files in <see cref="m_outputDirectory"/>.
        /// </summary>
        public PipRecordingSession StartPipRecordingSession(Process pip, string pipUniqueOutputHash)
        {
            TextWriter writer = new StreamWriter(Path.Combine(m_outputDirectory, pip.SemiStableHash.ToString("x16", CultureInfo.InvariantCulture) + ".txt"));
            Store.TryGetFingerprintStoreEntry(pipUniqueOutputHash, pip.FormattedSemiStableHash, out var entry);

            return new PipRecordingSession(Store, entry, writer);
        }

        /// <summary>
        /// While the returned <see cref="PipRecordingSession"/> is in scope,
        /// records all the information retrieved from the <see cref="FingerprintStore"/>
        /// to per-pip files in <see cref="m_outputDirectory"/>.
        /// </summary>
        public PipRecordingSession StartPipRecordingSession(string pipFormattedSemistableHash)
        {
            TextWriter writer = new StreamWriter(Path.Combine(m_outputDirectory, pipFormattedSemistableHash + ".txt"));
            Store.TryGetFingerprintStoreEntryBySemiStableHash(pipFormattedSemistableHash, out var entry);

            return new PipRecordingSession(Store, entry, writer);
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            Store.Dispose();
        }

        /// <summary>
        /// Encapsulates reading entries for one specific pip from the fingerprint store and writing the 
        /// retrieved entries to a records file.
        /// </summary>
        public class PipRecordingSession : IDisposable
        {
            private readonly FingerprintStoreEntry m_entry;

            private readonly FingerprintStore m_store;

            /// <summary>
            /// The formatted semi stable hash of the pip during the build that logged <see cref="m_store"/>.
            /// Formatted semi stable hashes may not be stable for the same pip across different builds.
            /// </summary>
            public string FormattedSemiStableHash => EntryExists ? m_entry.PipToFingerprintKeys.Key : null;

            /// <summary>
            /// The optional writer for the pip entry
            /// </summary>
            public TextWriter PipWriter { get; private set; }

            /// <summary>
            /// Whether the entry exists
            /// </summary>
            public bool EntryExists => m_entry != null;

            /// <summary>
            /// Weak fingerprint of the entry
            /// </summary>
            public string WeakFingerprint
            {
                get
                {
                    Contract.Assert(EntryExists);
                    return m_entry.WeakFingerprintToInputs.Key;
                }
            }

            /// <summary>
            /// Strong fingerprint of the entry
            /// </summary>
            public string StrongFingerprint
            {
                get
                {
                    Contract.Assert(EntryExists);
                    return m_entry.StrongFingerprintEntry.StrongFingerprintToInputs.Key;
                }
            }

            /// <summary>
            /// Path set hash of the entry.
            /// </summary>
            public string PathSetHash
            {
                get
                {
                    Contract.Assert(EntryExists);
                    return m_entry.StrongFingerprintEntry.PathSetHashToInputs.Key;
                }
            }

            /// <summary>
            /// Get path set value of the entry.
            /// </summary>
            public string PathSetValue
            {
                get
                {
                    Contract.Assert(EntryExists);
                    return m_entry.StrongFingerprintEntry.PathSetHashToInputs.Value;
                }
            }

            /// <summary>
            /// Constructor
            /// </summary>
            public PipRecordingSession(FingerprintStore store, FingerprintStoreEntry entry, TextWriter textWriter = null)
            {
                m_store = store;
                m_entry = entry;

                PipWriter = textWriter;

                if (EntryExists && textWriter != null)
                {
                    // Write all pip fingerprint information to a file, except for directory memberships.
                    // Directory memberships are skipped unless there is a strong fingerprint miss
                    // to avoid parsing the strong fingerprint entry.
                    m_entry.Print(PipWriter);
                }
            }

            /// <summary>
            /// Get weak fingerprint tree for the entry
            /// </summary>
            public JsonNode GetWeakFingerprintTree() => JsonTree.Deserialize(m_entry.WeakFingerprintToInputs.Value);

            /// <summary>
            /// Get strong fingerprint tree for the entry
            /// </summary>
            public JsonNode GetStrongFingerprintTree() => MergeStrongFingerprintAndPathSetTrees(GetStrongFingerpintInputTree(), GetPathSetTree());

            /// <summary>
            /// Get pathset tree.
            /// </summary>
            public JsonNode GetPathSetTree() => JsonTree.Deserialize(m_entry.StrongFingerprintEntry.PathSetHashToInputs.Value);

            private JsonNode GetStrongFingerpintInputTree() => JsonTree.Deserialize(m_entry.StrongFingerprintEntry.StrongFingerprintToInputs.Value);

            /// <summary>
            /// Diff pathsets.
            /// </summary>
            public JObject DiffPathSet(PipRecordingSession otherSession) =>
                JsonFingerprintDiff.DiffPathSets(
                    PathSetHash,
                    GetPathSetTree(),
                    GetStrongFingerpintInputTree(),
                    otherSession.PathSetHash,
                    otherSession.GetPathSetTree(),
                    otherSession.GetStrongFingerpintInputTree(),
                    directoryMembershipHash => GetDirectoryMembership(m_store, directoryMembershipHash),
                    otherDirectoryMembershipHash => GetDirectoryMembership(otherSession.m_store, otherDirectoryMembershipHash));

            /// <summary>
            /// Diff strong fingerprints.
            /// </summary>
            public JObject DiffStrongFingerprint(PipRecordingSession otherSession) =>
                JsonFingerprintDiff.DiffStrongFingerprints(
                    StrongFingerprint,
                    GetPathSetTree(),
                    GetStrongFingerpintInputTree(),
                    otherSession.StrongFingerprint,
                    otherSession.GetPathSetTree(),
                    otherSession.GetStrongFingerpintInputTree(),
                    directoryMembershipHash => GetDirectoryMembership(m_store, directoryMembershipHash),
                    otherDirectoryMembershipHash => GetDirectoryMembership(otherSession.m_store, otherDirectoryMembershipHash));

            /// <summary>
            /// Diff weak fingerprints.
            /// </summary>
            public JObject DiffWeakFingerprint(PipRecordingSession otherSession) =>
                JsonFingerprintDiff.DiffWeakFingerprints(
                    WeakFingerprint,
                    GetWeakFingerprintTree(),
                    otherSession.WeakFingerprint,
                    otherSession.GetWeakFingerprintTree());

            /// <summary>
            /// Path set hash inputs are stored separately from the strong fingerprint inputs.
            /// This merges the path set hash inputs tree into the strong fingerprint inputs tree
            /// while maintaining the 1:1 relationship between the path set and observed inputs.
            /// 
            /// Node notation:
            /// [id] "{name}":"{value}"
            /// Tree notation:
            /// {parentNode}
            ///     {childNode}
            /// 
            /// Start with the following subtrees:
            /// 
            /// From strong fingerprint
            /// 
            /// [1] "PathSet":"VSO0:7E2E49845EC0AE7413519E3EE605272078AF0B1C2911C021681D1D9197CC134A00"
            /// [2] "ObservedInputs":""
            ///     [3] "E":"VSO0:E0C5007DC8CF2D331236F156F136C50CACE2A5D549CD132D9B44ABD1F13D50CC00",
            /// 
            /// From path set hash 
            /// 
            /// [4] "Paths":""
            ///     [5] "Path":"B:/out/objects/n/x/qbkexxlc8je93wycw7yrlw0a305n7k/xunit-out/CacheMissAnaAD836B23/3/obj/readonly/src_0"
            ///     [6] "Flags":"IsDirectoryPath, DirectoryEnumeration, DirectoryEnumerationWithAllPattern"
            ///     [7] "EnumeratePatternRegex":"^.*$"
            ///
            /// And end with:
            /// 
            /// [1] "PathSet":"VSO0:7E2E49845EC0AE7413519E3EE605272078AF0B1C2911C021681D1D9197CC134A00"
            ///     [5'] "B:/out/objects/n/x/qbkexxlc8je93wycw7yrlw0a305n7k/xunit-out/CacheMissAnaAD836B23/3/obj/readonly/src_0":""
            ///         [6'] "Flags":"IsDirectoryPath, DirectoryEnumeration, DirectoryEnumerationWithAllPattern"
            ///         [7'] "EnumeratePatternRegex":"^.*$"
            ///         [3'] "ObservedInput":"E:VSO0:E0C5007DC8CF2D331236F156F136C50CACE2A5D549CD132D9B44ABD1F13D50CC00"
            ///         [8] "Members":"[src_1, src_2]"
            /// </summary>
            /// <returns>
            /// The root node of the merged tree (which will be the strong fingerprint tree's root).
            /// </returns>
            private JsonNode MergeStrongFingerprintAndPathSetTrees(JsonNode strongFingerprintTree, JsonNode pathSetTree)
            {
                // [1] "PathSet":"VSO0:7E2E49845EC0AE7413519E3EE605272078AF0B1C2911C021681D1D9197CC134A00")
                var parentPathNode = JsonTree.FindNodeByName(strongFingerprintTree, ObservedPathEntryConstants.PathSet);

                // [2] "ObservedInputs":""
                var observedInputsNode = JsonTree.FindNodeByName(strongFingerprintTree, ObservedInputConstants.ObservedInputs);
                JsonTree.EmancipateBranch(observedInputsNode);

                // In preparation for merging with observed inputs nodes,
                // remove the path set node's branch from the path set tree
                // [4] "Paths":""
                var pathSetNode = JsonTree.FindNodeByName(pathSetTree, ObservedPathSet.Labels.Paths);
                JsonTree.EmancipateBranch(pathSetNode);
                JsonNode currPathNode = null;
                JsonNode currFlagNode = null;
                JsonNode currRegexNode = null;
                var observedInputIt = observedInputsNode.Children.First;
                for (var it = pathSetNode.Children.First; it != null; it = pathSetNode.Children.First)
                {
                    var child = it.Value;
                    switch (child.Name)
                    {
                        case ObservedPathEntryConstants.Path:
                            if (currPathNode != null)
                            {
                                mergePathSetNode(parentPathNode, currPathNode, currFlagNode, currRegexNode, observedInputIt.Value);
                                observedInputIt = observedInputsNode.Children.First;
                                currPathNode = null;
                                currFlagNode = null;
                                currRegexNode = null;
                            }

                            currPathNode = child;
                            JsonTree.EmancipateBranch(currPathNode);
                            break;
                        case ObservedPathEntryConstants.Flags:
                            // [6] "Flags":"IsDirectoryPath, DirectoryEnumeration, DirectoryEnumerationWithAllPattern"
                            currFlagNode = child;
                            JsonTree.EmancipateBranch(currFlagNode);
                            break;
                        case ObservedPathEntryConstants.EnumeratePatternRegex:
                            // [7] "EnumeratePatternRegex":"^.*$"
                            currRegexNode = child;
                            JsonTree.EmancipateBranch(currRegexNode);
                            break;
                        default:
                            break;
                    }
                }

                if (currPathNode != null)
                {
                    mergePathSetNode(parentPathNode, currPathNode, currFlagNode, currRegexNode, observedInputIt.Value);
                }

                // Re-parent any other branches of the path set tree to the strong fingerprint tree
                // so they are still in a full strong fingerprint tree comparison.
                // We re-parent under parentPathNode because branches of pathSetTree are elements of PathSet
                var node = pathSetTree.Children.First;
                while (node != null)
                {
                    JsonTree.ReparentBranch(node.Value, parentPathNode);
                    node = pathSetTree.Children.First;
                }

                return strongFingerprintTree;

                void mergePathSetNode(JsonNode parentNode, JsonNode pathNode, JsonNode flagNode, JsonNode regexNode, JsonNode observedInputNode)
                {
                    // Switch from literal string "path" to actual file system path
                    // [5'] "B:/out/objects/n/x/qbkexxlc8je93wycw7yrlw0a305n7k/xunit-out/CacheMissAnaAD836B23/3/obj/readonly/src_0":""
                    pathNode.Name = pathNode.Values[0];

                    // The name captures the node's value, so clear the values to avoid extraneous value comparison when diffing
                    pathNode.Values.Clear();
                    JsonTree.ReparentBranch(pathNode, parentNode);

                    // [6'] "Flags":"IsDirectoryPath, DirectoryEnumeration, DirectoryEnumerationWithAllPattern"
                    JsonTree.ReparentBranch(flagNode, pathNode);
                    // [7'] "EnumeratePatternRegex":"^.*$"
                    JsonTree.ReparentBranch(regexNode, pathNode);

                    // [3'] "ObservedInput":"E:VSO0:E0C5007DC8CF2D331236F156F136C50CACE2A5D549CD132D9B44ABD1F13D50CC00"
                    // [8] "Members":"[src_1, src_2]"
                    ReparentObservedInput(observedInputNode, pathNode);
                }
            }

            /// <summary>
            /// Makes a tree that represents an observed input on a path into a subtree of
            /// a tree that represents the corresponding path in the pathset.
            /// 
            /// <see cref="MergeStrongFingerprintAndPathSetTrees(JsonNode, JsonNode)"/>
            /// for numbering explanation.
            /// 
            /// Converts
            /// [3] "E":"VSO0:E0C5007DC8CF2D331236F156F136C50CACE2A5D549CD132D9B44ABD1F13D50CC00"
            /// =>
            /// [3'] "ObservedInput":"E:VSO0:E0C5007DC8CF2D331236F156F136C50CACE2A5D549CD132D9B44ABD1F13D50CC00"
            /// 
            /// Reparent [3'] from
            /// [2] "ObservedInputs":""
            /// to
            /// [5'] "B:/out/objects/n/x/qbkexxlc8je93wycw7yrlw0a305n7k/xunit-out/CacheMissAnaAD836B23/3/obj/readonly/src_0":""
            /// 
            /// Add
            /// [8] "Members":"[src_1, src_2]"
            /// to
            /// [5'] "B:/out/objects/n/x/qbkexxlc8je93wycw7yrlw0a305n7k/xunit-out/CacheMissAnaAD836B23/3/obj/readonly/src_0":""
            /// </summary>
            /// <param name="observedInputNode"></param>
            /// <param name="pathSetNode"></param>
            private void ReparentObservedInput(JsonNode observedInputNode, JsonNode pathSetNode)
            {
                // Store values from
                // [3] "E":"VSO0:E0C5007DC8CF2D331236F156F136C50CACE2A5D549CD132D9B44ABD1F13D50CC00"
                // before manipulating the node
                var observedInputType = observedInputNode.Name;
                var observedInputHash = observedInputNode.Values[0];

                var values = observedInputNode.Values;
                values.Clear();

                string expandedType = ObservedInputConstants.ToExpandedString(observedInputType);
                switch (observedInputType)
                {
                    case ObservedInputConstants.AbsentPathProbe:
                    case ObservedInputConstants.ExistingFileProbe:
                    case ObservedInputConstants.ExistingDirectoryProbe:
                        values.Add(expandedType);
                        break;
                    case ObservedInputConstants.FileContentRead:
                        values.Add($"{expandedType}:{observedInputHash}");
                        break;
                    case ObservedInputConstants.DirectoryEnumeration:
                        values.Add($"{expandedType}:{observedInputHash}");
                        // [8] "Members":"[src_1, src_2]"
                        AddDirectoryMembershipBranch(observedInputHash, pathSetNode);
                        break;
                }

                // [3'] "ObservedInput":"E:VSO0:E0C5007DC8CF2D331236F156F136C50CACE2A5D549CD132D9B44ABD1F13D50CC00"
                observedInputNode.Name = ObservedInputConstants.ObservedInputs;
                JsonTree.ReparentBranch(observedInputNode, pathSetNode);
            }

            /// <summary>
            /// Adds a directory membership tree as a sub-tree to a given path set tree.
            /// </summary>
            /// <param name="directoryFingerprint">
            /// The directory fingerprint to look up membership.
            /// </param>
            /// <param name="pathSetNode">
            /// The path set node that represents the directory and the parent node.
            /// </param>
            private void AddDirectoryMembershipBranch(string directoryFingerprint, JsonNode pathSetNode)
            {
                if (m_store.TryGetContentHashValue(directoryFingerprint, out string inputs))
                {
                    WriteToPipFile(PrettyFormatJsonField(new KeyValuePair<string, string>(directoryFingerprint, inputs)).ToString());

                    var directoryMembershipTree = JsonTree.Deserialize(inputs);
                    for (var it = directoryMembershipTree.Children.First; it != null; it = it.Next)
                    {
                        JsonTree.ReparentBranch(it.Value, pathSetNode);
                    }
                }
                else
                {
                    // Include a node for the directory membership, but use an error message as the value
                    var placeholder = new JsonNode
                    {
                        Name = directoryFingerprint
                    };
                    placeholder.Values.Add(CacheMissAnalysisUtilities.RepeatedStrings.MissingDirectoryMembershipFingerprint);

                    JsonTree.ReparentBranch(placeholder, pathSetNode);
                }
            }

            private static IReadOnlyList<string> GetDirectoryMembership(FingerprintStore store, string directoryFingerprint)
            {
                if(!store.TryGetContentHashValue(directoryFingerprint, out string storedValue))
                {
                    return null;
                }

                var directoryMembershipTree = JsonTree.Deserialize(storedValue);
                return directoryMembershipTree.Children.First.Value.Values;
            }
            
            /// <summary>
            /// Writes a message to a specific pip's file.
            /// </summary>
            public void WriteToPipFile(string message)
            {
                PipWriter?.WriteLine(message);
            }

            /// <summary>
            /// Dispose
            /// </summary>
            public void Dispose()
            {
                PipWriter?.Dispose();
            }
        }
    }
}
