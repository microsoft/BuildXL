// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using static BuildXL.Scheduler.IncrementalScheduling.IncrementalSchedulingStateWriteTextHelpers;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Class recording pips or nodes that get dirtied due to journal scan or changed pip graph.
    /// </summary>
    internal class DirtiedPips
    {
        /// <summary>
        /// Pips of the current pip graph that get dirtied after scan.
        /// </summary>
        public readonly List<NodeId> PipsOfCurrentGraphGetDirtiedAfterScan = new List<NodeId>();

        /// <summary>
        /// Pips of the current pip graph that get dirtied because graph has changed.
        /// </summary>
        public readonly List<NodeId> PipsOfCurrentGraphGetDirtiedDueToGraphChange = new List<NodeId>();

        /// <summary>
        /// Pips (both of the current graph and the other pip graphs) that get dirtied because dynamic observations have changed after scan.
        /// </summary>
        public readonly List<(PipStableId, DynamicObservationType)> PipsGetDirtiedDueToDynamicObservationAfterScan = new List<(PipStableId, DynamicObservationType)>();

        /// <summary>
        /// List of pips of other pip graphs (not the current pip graph) that get dirtied after scan.
        /// </summary>
        public readonly List<PipStableId> PipsOfOtherPipGraphsGetDirtiedAfterScan = new List<PipStableId>();

        /// <summary>
        /// List of source files of other pip graphs (not the current pip graph) that get dirtied after scan.
        /// </summary>
        public readonly List<AbsolutePath> SourceFilesOfOtherPipGraphsGetDirtiedAfterScan = new List<AbsolutePath>();

        /// <summary>
        /// List of pips of other pip graphs (not the current pip graph) that get dirtied because graph has changed.
        /// </summary>
        public readonly List<PipStableId> PipsOfOtherPipGraphsGetDirtiedDueToGraphChange = new List<PipStableId>();

        /// <summary>
        /// List of source files of other pip graphs (not the current pip graph) that get dirtied because graph has changed.
        /// </summary>
        public readonly List<AbsolutePath> SourceFilesOfOtherPipGraphsGetDirtiedDueToGraphChange = new List<AbsolutePath>();

        /// <summary>
        /// Creates an instance of <see cref="DirtiedPips"/>.
        /// </summary>
        public DirtiedPips()
        {
        }

        private DirtiedPips(
            List<NodeId> pipsOfCurrentGraphGetDirtiedAfterScan,
            List<NodeId> pipsOfCurrentGraphGetDirtiedDueToGraphChange,
            List<(PipStableId, DynamicObservationType)> pipsGetDirtiedDueToDynamicObservationAfterScan,
            List<PipStableId> pipsOfOtherPipGraphsGetDirtiedAfterScan,
            List<AbsolutePath> sourceFilesOfOtherPipGraphsGetDirtiedAfterScan,
            List<PipStableId> pipsOfOtherPipGraphsGetDirtiedDueToGraphChange,
            List<AbsolutePath> sourceFilesOfOtherPipGraphsGetDirtiedDueToGraphChange)
        {
            Contract.Requires(pipsOfCurrentGraphGetDirtiedAfterScan != null);
            Contract.Requires(pipsOfCurrentGraphGetDirtiedDueToGraphChange != null);
            Contract.Requires(pipsGetDirtiedDueToDynamicObservationAfterScan != null);
            Contract.Requires(pipsOfOtherPipGraphsGetDirtiedAfterScan != null);
            Contract.Requires(sourceFilesOfOtherPipGraphsGetDirtiedAfterScan != null);
            Contract.Requires(pipsOfOtherPipGraphsGetDirtiedDueToGraphChange != null);
            Contract.Requires(sourceFilesOfOtherPipGraphsGetDirtiedDueToGraphChange != null);

            PipsOfCurrentGraphGetDirtiedAfterScan = pipsOfCurrentGraphGetDirtiedAfterScan;
            PipsOfCurrentGraphGetDirtiedDueToGraphChange = pipsOfCurrentGraphGetDirtiedDueToGraphChange;
            PipsGetDirtiedDueToDynamicObservationAfterScan = pipsGetDirtiedDueToDynamicObservationAfterScan;
            PipsOfOtherPipGraphsGetDirtiedAfterScan = pipsOfOtherPipGraphsGetDirtiedAfterScan;
            SourceFilesOfOtherPipGraphsGetDirtiedAfterScan = sourceFilesOfOtherPipGraphsGetDirtiedAfterScan;
            PipsOfOtherPipGraphsGetDirtiedDueToGraphChange = pipsOfOtherPipGraphsGetDirtiedDueToGraphChange;
            SourceFilesOfOtherPipGraphsGetDirtiedDueToGraphChange = sourceFilesOfOtherPipGraphsGetDirtiedDueToGraphChange;
        }

        /// <summary>
        /// Serializes to a <see cref="BuildXLWriter"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteReadOnlyList(PipsOfCurrentGraphGetDirtiedAfterScan, (w, n) => w.Write(n.Value));
            writer.WriteReadOnlyList(PipsOfCurrentGraphGetDirtiedDueToGraphChange, (w, n) => w.Write(n.Value));
            writer.WriteReadOnlyList(PipsGetDirtiedDueToDynamicObservationAfterScan, (w, c) => { w.Write(c.Item1); w.Write((byte)c.Item2); });
            writer.WriteReadOnlyList(PipsOfOtherPipGraphsGetDirtiedAfterScan, (w, id) => w.Write(id));
            writer.WriteReadOnlyList(SourceFilesOfOtherPipGraphsGetDirtiedAfterScan, (w, p) => w.Write(p));
            writer.WriteReadOnlyList(PipsOfOtherPipGraphsGetDirtiedDueToGraphChange, (w, id) => w.Write(id));
            writer.WriteReadOnlyList(SourceFilesOfOtherPipGraphsGetDirtiedDueToGraphChange, (w, p) => w.Write(p));
        }

        /// <summary>
        /// Deserializes from a <see cref="BuildXLReader"/>.
        /// </summary>
        public static DirtiedPips Deserialize(BuildXLReader reader)
        {
            var pipsOfCurrentGraphGetDirtiedAfterScan = new List<NodeId>(reader.ReadReadOnlyList(r => new NodeId(r.ReadUInt32())));
            var pipsOfCurrentGraphGetDirtiedDueToGraphChange = new List<NodeId>(reader.ReadReadOnlyList(r => new NodeId(r.ReadUInt32())));
            var pipsGetDirtiedDueToDynamicObservationAfterScan = 
                new List<(PipStableId, DynamicObservationType)>(reader.ReadReadOnlyList(r => (r.ReadPipStableId(), (DynamicObservationType)r.ReadByte())));
            var pipsOfOtherPipGraphsGetDirtiedAfterScan = new List<PipStableId>(reader.ReadReadOnlyList(r => r.ReadPipStableId()));
            var sourceFilesOfOtherPipGraphsGetDirtiedAfterScan = new List<AbsolutePath>(reader.ReadReadOnlyList(r => r.ReadAbsolutePath()));
            var pipsOfOtherPipGraphsGetDirtiedDueToGraphChange = new List<PipStableId>(reader.ReadReadOnlyList(r => r.ReadPipStableId()));
            var sourceFilesOfOtherPipGraphsGetDirtiedDueToGraphChange = new List<AbsolutePath>(reader.ReadReadOnlyList(r => r.ReadAbsolutePath()));

            return new DirtiedPips(
                pipsOfCurrentGraphGetDirtiedAfterScan,
                pipsOfCurrentGraphGetDirtiedDueToGraphChange,
                pipsGetDirtiedDueToDynamicObservationAfterScan,
                pipsOfOtherPipGraphsGetDirtiedAfterScan,
                sourceFilesOfOtherPipGraphsGetDirtiedAfterScan,
                pipsOfOtherPipGraphsGetDirtiedDueToGraphChange,
                sourceFilesOfOtherPipGraphsGetDirtiedDueToGraphChange);
        }

        /// <summary>
        /// Writes textual format of this instance of <see cref="DirtiedPips"/>.
        /// </summary>
        public void WriteText(TextWriter writer, PipGraph pipGraph, PathTable pathTable, PipOrigins pipOrigins)
        {
            Contract.Requires(writer != null);
            Contract.Requires(pipGraph != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(pipOrigins != null);

            WriteTextEntryWithHeader(writer, "Pips of the current pip graph get dirtied after scan", w => WriteTextNodes(w, pipGraph, PipsOfCurrentGraphGetDirtiedAfterScan));
            WriteTextEntryWithHeader(writer, "Pips of the current pip graph get dirtied due to graph change", w => WriteTextNodes(w, pipGraph, PipsOfCurrentGraphGetDirtiedDueToGraphChange));
            WriteTextEntryWithHeader(
                writer,
                "Pips get dirtied due to observed files after scan",
                w => WriteTextList(w, PipsGetDirtiedDueToDynamicObservationAfterScan.Where(p => p.Item2 == DynamicObservationType.ObservedFile), p => GetPipIdText(pipOrigins, p.Item1)));
            WriteTextEntryWithHeader(
                writer,
                "Pips get dirtied due to enumeration after scan",
                w => WriteTextList(w, PipsGetDirtiedDueToDynamicObservationAfterScan.Where(p => p.Item2 == DynamicObservationType.Enumeration), p => GetPipIdText(pipOrigins, p.Item1)));
            WriteTextEntryWithHeader(
                writer,
                "Pips of other pip graphs get dirtied after scan",
                w => WriteTextList(w, PipsOfOtherPipGraphsGetDirtiedAfterScan, p => GetPipIdText(pipOrigins, p)));
            WriteTextEntryWithHeader(
                writer,
                "Source files of other pip graphs get dirtied after scan",
                w => WriteTextPaths(writer, pathTable, SourceFilesOfOtherPipGraphsGetDirtiedAfterScan));
            WriteTextEntryWithHeader(
                writer,
                "Pips of other pip graphs get dirtied due to graph change",
                w => WriteTextList(w, PipsOfOtherPipGraphsGetDirtiedDueToGraphChange, p => GetPipIdText(pipOrigins, p)));
            WriteTextEntryWithHeader(
                writer,
                "Source files of other pip graphs get dirtied due to graph change",
                w => WriteTextPaths(writer, pathTable, SourceFilesOfOtherPipGraphsGetDirtiedDueToGraphChange));
        }
    }
}
