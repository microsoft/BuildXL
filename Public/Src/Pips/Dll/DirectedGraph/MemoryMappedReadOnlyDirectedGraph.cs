// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.DirectedGraph
{
    /// <summary>
    /// Read-only directed graph whose edge and node-height data is backed by memory-mapped files.
    /// </summary>
    /// <remarks>
    /// The cached outgoing artifact stores two per-node offset tables, node heights, and a contiguous sequence of
    /// packed outgoing edges. Each offset pair identifies the range of edges for one node, so traversing an adjacency
    /// list requires only two offset reads followed by sequential edge reads. Edge records use the smallest whole-byte
    /// width that can represent every node ID plus the light-edge flag.
    ///
    /// Only the outgoing artifact is serialized on the graph-construction critical path. On first load, a consumer
    /// reconstructs the incoming edges into a sidecar next to the outgoing artifact, then maps both files read-only.
    /// Later loads reuse a complete sidecar whose envelope correlation ID, layout, and offsets match the outgoing file.
    /// Reconstruction scatters reverse edges directly into a writable mapping. Traversal acquires each read-only view
    /// pointer once for the graph lifetime to avoid per-value safe-handle synchronization.
    ///
    /// This representation substantially reduces managed heap retention because the large adjacency tables remain
    /// file-backed. The tradeoffs are an additional first-load construction step, disk space for the incoming
    /// sidecar, and random memory-mapped reads during traversal. The files must remain directly accessible
    /// to the operating system and cannot be mapped while compressed or stored inside a zip archive. Callers that read
    /// external zip bundles must first materialize the outgoing artifact to a temporary file.
    ///
    /// Instances own their mappings and must be disposed when the graph is no longer needed.
    /// </remarks>
    public sealed class MemoryMappedReadOnlyDirectedGraph : DirectedGraph, IDisposable
    {
        // The serialized header consists of four Int32 values followed by six Int64 values.
        private static int HeaderSize => (4 * sizeof(int)) + (6 * sizeof(long));

        // On a representative 492M-edge graph, 4 and 8 writers were within 1% for outgoing serialization,
        // while incoming construction was 1.6% faster with 4. Cap at 4 to avoid extra contention for no measured benefit.
        private const int MaxConstructionParallelism = 4;
        private static readonly FileEnvelope s_fileEnvelope = new FileEnvelope(name: "MemoryMappedReadOnlyDirectedGraph", version: 3);

        private readonly MemoryMappedFile m_outgoingFile;
        private readonly MemoryMappedViewAccessor m_outgoingView;
        private readonly MemoryMappedFile m_incomingFile;
        private readonly MemoryMappedViewAccessor m_incomingView;
        private readonly long m_outgoingOffsetsPosition;
        private readonly long m_incomingOffsetsPosition;
        private readonly long m_heightsPosition;
        private readonly long m_outgoingEdgesPosition;
        private readonly long m_incomingEdgesPosition;
        private readonly long m_outgoingFileLength;
        private readonly long m_incomingFileLength;
        private readonly int m_edgeByteWidth;
        private readonly uint m_nodeIdMask;
        private readonly unsafe byte* m_outgoingPointer;
        private readonly unsafe byte* m_incomingPointer;
        private bool m_disposed;

        /// <summary>
        /// Initializes a graph over validated read-only outgoing and incoming mappings.
        /// </summary>
        private unsafe MemoryMappedReadOnlyDirectedGraph(
            MemoryMappedFile outgoingFile,
            MemoryMappedViewAccessor outgoingView,
            MemoryMappedFile incomingFile,
            MemoryMappedViewAccessor incomingView,
            FileLayout outgoingLayout,
            FileLayout incomingLayout)
        {
            m_outgoingFile = outgoingFile;
            m_outgoingView = outgoingView;
            m_incomingFile = incomingFile;
            m_incomingView = incomingView;
            m_lastNodeId = outgoingLayout.NodeCount;
            m_edgeCount = outgoingLayout.EdgeCount;
            m_outgoingOffsetsPosition = outgoingLayout.OffsetsPosition;
            m_incomingOffsetsPosition = incomingLayout.OffsetsPosition;
            m_heightsPosition = outgoingLayout.HeightsPosition;
            m_outgoingEdgesPosition = outgoingLayout.EdgesPosition;
            m_incomingEdgesPosition = incomingLayout.EdgesPosition;
            m_outgoingFileLength = outgoingLayout.FileLength;
            m_incomingFileLength = incomingLayout.FileLength;
            m_edgeByteWidth = outgoingLayout.EdgeByteWidth;
            m_nodeIdMask = GetNodeIdMask(m_edgeByteWidth);

            byte* outgoingPointer = null;
            m_outgoingView.SafeMemoryMappedViewHandle.AcquirePointer(ref outgoingPointer);
            try
            {
                byte* incomingPointer = null;
                m_incomingView.SafeMemoryMappedViewHandle.AcquirePointer(ref incomingPointer);
                m_outgoingPointer = outgoingPointer + m_outgoingView.PointerOffset;
                m_incomingPointer = incomingPointer + m_incomingView.PointerOffset;
            }
            catch
            {
                m_outgoingView.SafeMemoryMappedViewHandle.ReleasePointer();
                throw;
            }
        }

        #region Serialization

        /// <summary>
        /// Gets the persistent incoming sidecar path for an outgoing artifact.
        /// </summary>
        public static string GetIncomingPath(string outgoingPath)
        {
            Contract.Requires(!string.IsNullOrEmpty(outgoingPath));
            return outgoingPath + ".incoming";
        }

        /// <summary>
        /// Reads and validates the outgoing artifact envelope.
        /// </summary>
        public static FileEnvelopeId ReadEnvelopeId(string outgoingPath)
        {
            Contract.Requires(!string.IsNullOrEmpty(outgoingPath));

            using var stream = new FileStream(outgoingPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return s_fileEnvelope.ReadHeader(stream);
        }

        /// <summary>
        /// Writes the outgoing graph artifact with the supplied file-envelope correlation ID.
        /// </summary>
        internal static void WriteOutgoing(
            string path,
            MutableDirectedGraph graph,
            Task nodeHeightsReady,
            FileEnvelopeId envelopeId)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            Contract.Requires(graph != null);
            Contract.Requires(nodeHeightsReady != null);
            Contract.Requires(envelopeId.IsValid);

            int nodeCount = graph.NodeCount;
            int edgeCount = graph.EdgeCount;
            int edgeByteWidth = CalculateEdgeByteWidth(nodeCount);
            int[] offsets;
            FileLayout layout;

            // Write the fixed-size metadata first and reserve the complete file so independent writers can fill
            // non-overlapping edge and height regions in parallel.
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var writer = new BinaryWriter(stream))
            {
                s_fileEnvelope.WriteHeader(stream, envelopeId);
                layout = CreateLayout(FileKind.Outgoing, nodeCount, edgeCount, edgeByteWidth, stream.Position);
                WriteHeader(writer, layout);
                WriteOffsets(writer, graph, nodeCount, edgeCount, isIncoming: false, captureOffsets: true, out offsets);
                WriteOffsets(writer, graph, nodeCount, edgeCount, isIncoming: true, captureOffsets: false, out _);
                stream.SetLength(layout.FileLength);
            }

            // Outgoing edge ranges are independent by source node. MutableDirectedGraph starts computing heights
            // before this method so that work overlaps metadata and edge serialization. Wait once before bulk-reading
            // the resulting internal state rather than synchronizing every GetComputedNodeHeight call.
            var tasks = new List<Task>();
            AddOutgoingEdgeWriters(tasks, path, graph, nodeCount, layout, offsets);
            tasks.Add(
                Task.Run(
                    () =>
                    {
                        nodeHeightsReady.GetAwaiter().GetResult();
                        using var heightStream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 64 * 1024, FileOptions.RandomAccess);
                        using var heightWriter = new BinaryWriter(heightStream);
                        heightStream.Position = layout.HeightsPosition;
                        heightWriter.Write(0);
                        for (uint node = 1; node <= nodeCount; node++)
                        {
                            heightWriter.Write(graph.GetComputedNodeHeight(node));
                        }
                    }));
            Task.WhenAll(tasks).GetAwaiter().GetResult();

            // Mark the envelope complete only after every reserved region has been written successfully.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                stream.Position = layout.FileLength;
                s_fileEnvelope.FixUpHeader(stream, envelopeId);
            }
        }

        #endregion

        #region Deserialization

        /// <summary>
        /// Creates the incoming-edge artifact, validates the outgoing artifact correlation, and opens both artifacts.
        /// </summary>
        public static MemoryMappedReadOnlyDirectedGraph Deserialize(
            string outgoingPath,
            FileEnvelopeId expectedEnvelopeId,
            bool deleteIncomingOnClose = false)
        {
            Contract.Requires(!string.IsNullOrEmpty(outgoingPath));
            Contract.Requires(expectedEnvelopeId.IsValid);

            string incomingPath = GetIncomingPath(outgoingPath);
            var outgoingStream = new FileStream(outgoingPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            MemoryMappedFile outgoingFile = null;
            MemoryMappedViewAccessor outgoingView = null;
            MemoryMappedFile incomingFile = null;
            MemoryMappedViewAccessor incomingView = null;
            FileStream incomingStream = null;
            string temporaryIncomingPath = null;

            try
            {
                // Validate and map the cached outgoing artifact before creating any derived state.
                FileEnvelopeId outgoingEnvelopeId = ReadEnvelope(outgoingStream, expectedEnvelopeId, out long outgoingHeaderPosition);
                outgoingFile = MemoryMappedFile.CreateFromFile(
                    outgoingStream,
                    mapName: null,
                    capacity: 0,
                    MemoryMappedFileAccess.Read,
                    HandleInheritability.None,
                    leaveOpen: false);
                outgoingView = outgoingFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                FileLayout outgoingLayout = ReadAndValidateLayout(
                    outgoingView,
                    outgoingStream.Length,
                    FileKind.Outgoing,
                    outgoingHeaderPosition);

                // The outgoing artifact carries both degree tables. Keeping these temporary arrays avoids random
                // accessor reads while reverse edges are scattered in parallel.
                int offsetCount = GetOffsetCount(outgoingLayout.NodeCount);
                var outgoingOffsets = new int[offsetCount];
                outgoingView.ReadArray(outgoingLayout.OffsetsPosition, outgoingOffsets, 0, outgoingOffsets.Length);

                var incomingOffsets = new int[offsetCount];
                outgoingView.ReadArray(outgoingLayout.SecondaryOffsetsPosition, incomingOffsets, 0, incomingOffsets.Length);
                ValidateOffsets(outgoingOffsets, outgoingLayout.EdgeCount, "outgoing");
                ValidateOffsets(incomingOffsets, outgoingLayout.EdgeCount, "incoming");

                var incomingEnvelopeId = outgoingEnvelopeId;
                // The sidecar is derived on first access and retained beside the outgoing artifact. Subsequent loads
                // take this fast path when its correlation ID and layout prove that it belongs to this exact graph.
                if (TryOpenIncoming(
                    incomingPath,
                    incomingEnvelopeId,
                    outgoingLayout,
                    incomingOffsets,
                    deleteIncomingOnClose,
                    out incomingFile,
                    out incomingView,
                    out FileLayout incomingLayout))
                {
                    return new MemoryMappedReadOnlyDirectedGraph(
                        outgoingFile,
                        outgoingView,
                        incomingFile,
                        incomingView,
                        outgoingLayout,
                        incomingLayout);
                }

                int processId;
                using (Process currentProcess = Process.GetCurrentProcess())
                {
                    processId = currentProcess.Id;
                }

                DeleteAbandonedIncomingFiles(incomingPath, processId);

                // Use a unique path so concurrent builders cannot delete or overwrite one another's partial sidecars.
                temporaryIncomingPath = string.Concat(
                    incomingPath,
                    ".tmp.",
                    processId.ToString(CultureInfo.InvariantCulture),
                    ".",
                    Guid.NewGuid().ToString("N"));

                // Build into a unique file so readers can never observe a partially written incoming artifact.
                using (var stream = new FileStream(temporaryIncomingPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new BinaryWriter(stream))
                {
                    s_fileEnvelope.WriteHeader(stream, incomingEnvelopeId);
                    incomingLayout = CreateLayout(
                        FileKind.Incoming,
                        outgoingLayout.NodeCount,
                        outgoingLayout.EdgeCount,
                        outgoingLayout.EdgeByteWidth,
                        stream.Position);
                    WriteHeader(writer, incomingLayout);
                    for (int i = 0; i < incomingOffsets.Length; i++)
                    {
                        writer.Write(incomingOffsets[i]);
                    }

                    stream.SetLength(incomingLayout.FileLength);
                }

                incomingStream = new FileStream(
                    temporaryIncomingPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    options: FileOptions.RandomAccess);
                incomingFile = MemoryMappedFile.CreateFromFile(
                    incomingStream,
                    mapName: null,
                    capacity: 0,
                    MemoryMappedFileAccess.ReadWrite,
                    HandleInheritability.None,
                    leaveOpen: false);
                incomingView = incomingFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);

                BuildAndWriteIncomingEdges(
                    outgoingView,
                    incomingView,
                    outgoingLayout,
                    incomingLayout,
                    outgoingOffsets,
                    incomingOffsets);

                incomingStream.Position = incomingLayout.FileLength;
                s_fileEnvelope.FixUpHeader(incomingStream, incomingEnvelopeId);
                ReadAndValidateLayout(incomingView, incomingStream.Length, FileKind.Incoming, incomingLayout.HeaderPosition);

                incomingView.Flush();
                incomingView.Dispose();
                incomingView = null;
                incomingFile.Dispose();
                incomingFile = null;
                incomingStream.Dispose();
                incomingStream = null;

                // Publish only after the file is complete and correlated with the outgoing artifact. Existing readers
                // retain their old equivalent file section when the destination is replaced.
                try
                {
                    FileUtilities.MoveFileAsync(temporaryIncomingPath, incomingPath, replaceExisting: true).GetAwaiter().GetResult();
                    temporaryIncomingPath = null;
                }
                catch (BuildXLException)
                {
                    // Unix replacement is implemented as delete followed by move, so another builder can win between
                    // those operations. Reuse its fully validated result instead of failing this load.
                    if (TryOpenIncoming(
                        incomingPath,
                        incomingEnvelopeId,
                        outgoingLayout,
                        incomingOffsets,
                        deleteIncomingOnClose,
                        out incomingFile,
                        out incomingView,
                        out incomingLayout))
                    {
                        _ = FileUtilities.TryDeleteFile(temporaryIncomingPath, retryOnFailure: true);
                        temporaryIncomingPath = null;
                        return new MemoryMappedReadOnlyDirectedGraph(
                            outgoingFile,
                            outgoingView,
                            incomingFile,
                            incomingView,
                            outgoingLayout,
                            incomingLayout);
                    }

                    throw;
                }

                // Close the writable temporary mapping before publication, then reopen the published path read-only so
                // the graph never retains a writable mapping.
                if (!TryOpenIncoming(
                    incomingPath,
                    incomingEnvelopeId,
                    outgoingLayout,
                    incomingOffsets,
                    deleteIncomingOnClose,
                    out incomingFile,
                    out incomingView,
                    out incomingLayout))
                {
                    throw new InvalidDataException("The completed incoming directed graph artifact could not be reopened.");
                }

                return new MemoryMappedReadOnlyDirectedGraph(
                    outgoingFile,
                    outgoingView,
                    incomingFile,
                    incomingView,
                    outgoingLayout,
                    incomingLayout);
            }
            catch
            {
                incomingView?.Dispose();
                incomingFile?.Dispose();
                incomingStream?.Dispose();
                outgoingView?.Dispose();
                outgoingFile?.Dispose();
                outgoingStream.Dispose();
                if (temporaryIncomingPath != null)
                {
                    _ = FileUtilities.TryDeleteFile(temporaryIncomingPath, retryOnFailure: true);
                }

                throw;
            }
        }

        #endregion

        #region Graph traversal

        /// <inheritdoc />
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;
                m_incomingView.SafeMemoryMappedViewHandle.ReleasePointer();
                m_outgoingView.SafeMemoryMappedViewHandle.ReleasePointer();
                m_incomingView.Dispose();
                m_incomingFile.Dispose();
                m_outgoingView.Dispose();
                m_outgoingFile.Dispose();
            }
        }

        /// <inheritdoc />
        protected override unsafe void GetEdgeAndNextIndex(int currentIndex, out Edge edge, out int nextIndex, bool isIncoming)
        {
            Debug.Assert((uint)currentIndex < (uint)m_edgeCount);
            byte* pointer = isIncoming ? m_incomingPointer : m_outgoingPointer;
            long edgesPosition = isIncoming ? m_incomingEdgesPosition : m_outgoingEdgesPosition;
            long fileLength = isIncoming ? m_incomingFileLength : m_outgoingFileLength;
            long edgePosition = edgesPosition + ((long)currentIndex * m_edgeByteWidth);
            uint packed = ReadPackedEdgeFromPointer(GetPointerAtOffset(pointer, fileLength, edgePosition, m_edgeByteWidth), m_edgeByteWidth);
            uint nodeId = (packed & m_nodeIdMask) + 1;
            if (nodeId > m_lastNodeId)
            {
                throw new InvalidDataException($"Directed graph edge references node {nodeId}, which exceeds node count {m_lastNodeId}.");
            }

            uint lightBit = (packed & GetLightEdgeMask(m_edgeByteWidth)) != 0 ? Edge.LightEdgeBit : 0;
            edge = new Edge(nodeId | lightBit);
            nextIndex = currentIndex + 1;
        }

        /// <inheritdoc />
        protected override NodeEdgeListHeader GetInEdgeListHeader(uint index)
        {
            return GetEdgeListHeader(index, isIncoming: true);
        }

        /// <inheritdoc />
        protected override NodeEdgeListHeader GetOutEdgeListHeader(uint index)
        {
            return GetEdgeListHeader(index, isIncoming: false);
        }

        private NodeEdgeListHeader GetEdgeListHeader(uint index, bool isIncoming)
        {
            Debug.Assert(index <= m_lastNodeId);
            long offsetsPosition = isIncoming ? m_incomingOffsetsPosition : m_outgoingOffsetsPosition;
            long firstPosition = offsetsPosition + ((long)index * sizeof(int));
            long nextPosition = offsetsPosition + ((long)(index + 1) * sizeof(int));
            int first = ReadInt32AtOffset(isIncoming, firstPosition);
            int next = ReadInt32AtOffset(isIncoming, nextPosition);
            return new NodeEdgeListHeader(first, next - first);
        }

        /// <inheritdoc />
        protected override int GetNodeHeight(uint index)
        {
            Debug.Assert(index <= m_lastNodeId);
            long heightPosition = m_heightsPosition + ((long)index * sizeof(int));
            return ReadInt32AtOffset(isIncoming: false, position: heightPosition);
        }

        private unsafe int ReadInt32AtOffset(bool isIncoming, long position)
        {
            byte* pointer = isIncoming ? m_incomingPointer : m_outgoingPointer;
            long fileLength = isIncoming ? m_incomingFileLength : m_outgoingFileLength;
            Debug.Assert(position >= 0 && position <= fileLength - sizeof(int));
            return *(int*)(pointer + position);
        }

        private static unsafe byte* GetPointerAtOffset(byte* pointer, long fileLength, long position, int byteCount)
        {
            Debug.Assert(position >= 0 && byteCount >= 0 && position <= fileLength - byteCount);
            return pointer + position;
        }

        #endregion

        #region File format

        private static int CalculateEdgeByteWidth(int nodeCount)
        {
            int nodeBitWidth = CalculateNodeIdBitWidth(nodeCount);
            // Add one bit for IsLight, round up to the next whole byte, and retain at least one byte for empty graphs.
            return Math.Max(1, (nodeBitWidth + 1 + 7) / 8);
        }

        private static int CalculateNodeIdBitWidth(int nodeCount)
        {
            // NodeId 0 is reserved to represent an invalid node; packed edges subtract one to encode valid IDs as a dense zero-based range.
            uint maximumZeroBasedNodeId = nodeCount == 0 ? 0 : (uint)(nodeCount - 1);
            int bitWidth = 0;
            // Repeatedly dropping the least-significant bit computes floor(log2(maximum)) + 1 without floating point.
            while (maximumZeroBasedNodeId != 0)
            {
                bitWidth++;
                maximumZeroBasedNodeId >>= 1;
            }

            return bitWidth;
        }

        private static int GetConstructionParallelism()
        {
            return Math.Min(MaxConstructionParallelism, Math.Max(1, Environment.ProcessorCount / 2));
        }

        private static uint GetLightEdgeMask(int edgeByteWidth)
        {
            return 1U << ((edgeByteWidth * 8) - 1);
        }

        private static uint GetNodeIdMask(int edgeByteWidth)
        {
            return GetLightEdgeMask(edgeByteWidth) - 1;
        }

        private static long AlignToUInt64(long position) => (position + sizeof(ulong) - 1) & ~(sizeof(ulong) - 1);

        private static int GetOffsetCount(int nodeCount)
        {
            // Node IDs are one-based, so index 0 is unused. The extra terminal entry at nodeCount + 1 closes the
            // half-open edge range [offsets[node], offsets[node + 1]) for the final node.
            return checked(nodeCount + 2);
        }

        private static FileLayout CreateLayout(FileKind kind, int nodeCount, int edgeCount, int edgeByteWidth, long headerPosition)
        {
            int offsetCount = GetOffsetCount(nodeCount);
            // Heights are stored only in the outgoing artifact because they are node properties, not edge-direction
            // properties. Index 0 is retained so a node ID can directly index its height.
            int heightCount = kind == FileKind.Outgoing ? nodeCount + 1 : 0;
            long offsetsPosition = headerPosition + HeaderSize;
            long secondaryOffsetsPosition = kind == FileKind.Outgoing ? offsetsPosition + ((long)offsetCount * sizeof(int)) : 0;
            long heightsPosition = kind == FileKind.Outgoing ? secondaryOffsetsPosition + ((long)offsetCount * sizeof(int)) : 0;
            long edgesPosition = AlignToUInt64(
                offsetsPosition
                + ((long)offsetCount * sizeof(int))
                + (kind == FileKind.Outgoing ? (long)offsetCount * sizeof(int) : 0)
                + ((long)heightCount * sizeof(int)));
            long edgeDataLength = (long)edgeCount * edgeByteWidth;
            long fileLength = edgesPosition + edgeDataLength;
            return new FileLayout(
                kind,
                nodeCount,
                edgeCount,
                edgeByteWidth,
                headerPosition,
                offsetsPosition,
                secondaryOffsetsPosition,
                heightsPosition,
                edgesPosition,
                fileLength,
                edgeDataLength);
        }

        private static void WriteHeader(BinaryWriter writer, FileLayout layout)
        {
            // Persist every derived region boundary and size. Readers recompute the expected layout and compare each
            // value, turning truncation, format drift, and corrupt metadata into a managed validation failure before
            // any raw pointer is acquired.
            writer.Write((int)layout.Kind);
            writer.Write(layout.NodeCount);
            writer.Write(layout.EdgeCount);
            writer.Write(layout.EdgeByteWidth);
            writer.Write(layout.OffsetsPosition);
            writer.Write(layout.SecondaryOffsetsPosition);
            writer.Write(layout.HeightsPosition);
            writer.Write(layout.EdgesPosition);
            writer.Write(layout.FileLength);
            writer.Write(layout.EdgeDataLength);
            Contract.Assert(writer.BaseStream.Position - layout.HeaderPosition == HeaderSize);
        }

        private static FileLayout ReadAndValidateLayout(
            MemoryMappedViewAccessor view,
            long actualLength,
            FileKind expectedKind,
            long headerPosition)
        {
            if (actualLength < headerPosition + HeaderSize)
            {
                throw new InvalidDataException("Unsupported memory-mapped directed graph format.");
            }

            var headerBytes = new byte[HeaderSize];
            view.ReadArray(headerPosition, headerBytes, 0, headerBytes.Length);
            using var headerStream = new MemoryStream(headerBytes, writable: false);
            using var reader = new BinaryReader(headerStream);
            FileKind kind = (FileKind)reader.ReadInt32();
            int nodeCount = reader.ReadInt32();
            int edgeCount = reader.ReadInt32();
            int edgeByteWidth = reader.ReadInt32();
            long offsetsPosition = reader.ReadInt64();
            long secondaryOffsetsPosition = reader.ReadInt64();
            long heightsPosition = reader.ReadInt64();
            long edgesPosition = reader.ReadInt64();
            long fileLength = reader.ReadInt64();
            long edgeDataLength = reader.ReadInt64();

            ValidateHeaderField(kind == expectedKind, $"File kind {kind} does not match expected kind {expectedKind}.");
            ValidateHeaderField(nodeCount >= 0, $"Node count {nodeCount} is negative.");
            ValidateHeaderField(nodeCount <= NodeId.MaxValue, $"Node count {nodeCount} exceeds the maximum supported node ID.");
            ValidateHeaderField(edgeCount >= 0, $"Edge count {edgeCount} is negative.");
            ValidateHeaderField(edgeByteWidth == CalculateEdgeByteWidth(nodeCount), $"Edge byte width {edgeByteWidth} is invalid for {nodeCount} nodes.");

            FileLayout expected = CreateLayout(kind, nodeCount, edgeCount, edgeByteWidth, headerPosition);
            ValidateHeaderField(offsetsPosition == expected.OffsetsPosition, $"Offsets position {offsetsPosition} does not match expected position {expected.OffsetsPosition}.");
            ValidateHeaderField(secondaryOffsetsPosition == expected.SecondaryOffsetsPosition, $"Secondary offsets position {secondaryOffsetsPosition} does not match expected position {expected.SecondaryOffsetsPosition}.");
            ValidateHeaderField(heightsPosition == expected.HeightsPosition, $"Heights position {heightsPosition} does not match expected position {expected.HeightsPosition}.");
            ValidateHeaderField(edgesPosition == expected.EdgesPosition, $"Edges position {edgesPosition} does not match expected position {expected.EdgesPosition}.");
            ValidateHeaderField(fileLength == expected.FileLength, $"Declared file length {fileLength} does not match expected length {expected.FileLength}.");
            ValidateHeaderField(edgeDataLength == expected.EdgeDataLength, $"Edge data length {edgeDataLength} does not match expected length {expected.EdgeDataLength}.");
            ValidateHeaderField(fileLength == actualLength, $"Declared file length {fileLength} does not match actual length {actualLength}.");

            return expected;
        }

        private static void ValidateHeaderField(bool condition, string message)
        {
            // These are validations of persisted bytes, not caller preconditions. They must fail deterministically
            // with a data error in release builds before any pointer is acquired.
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }

        private static FileEnvelopeId ReadEnvelope(
            FileStream stream,
            FileEnvelopeId expectedEnvelopeId,
            out long headerPosition)
        {
            try
            {
                FileEnvelopeId envelopeId = s_fileEnvelope.ReadHeader(stream);
                // FileEnvelopeId contains the unique correlation GUID shared by the graph-cache descriptor and the
                // outgoing artifact. The incoming sidecar copies this ID, preventing files from different graphs from
                // being paired even when their dimensions happen to match.
                FileEnvelope.CheckCorrelationIds(envelopeId, expectedEnvelopeId);

                headerPosition = stream.Position;
                return envelopeId;
            }
            catch (BuildXLException exception)
            {
                throw new InvalidDataException("Invalid memory-mapped directed graph file envelope.", exception);
            }
        }

        #endregion

        #region Incoming sidecar validation and publication

        private static void DeleteAbandonedIncomingFiles(string incomingPath, int currentProcessId)
        {
            string directory = Path.GetDirectoryName(incomingPath);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            string incomingFileName = Path.GetFileName(incomingPath);
            string currentProcessPrefix = string.Concat(
                incomingFileName,
                ".tmp.",
                currentProcessId.ToString(CultureInfo.InvariantCulture),
                ".");

            foreach (string temporaryPath in Directory.EnumerateFiles(
                directory,
                incomingFileName + ".tmp.*",
                SearchOption.TopDirectoryOnly))
            {
                // Preserve another concurrent construction in this process. Other process IDs identify files left by
                // earlier graph loads and are safe to remove before creating this process's new temporary sidecar.
                if (!Path.GetFileName(temporaryPath).StartsWith(
                    currentProcessPrefix,
                    OperatingSystemHelper.PathComparison))
                {
                    _ = FileUtilities.TryDeleteFile(temporaryPath, retryOnFailure: false);
                }
            }
        }

        /// <summary>
        /// Opens a complete incoming sidecar only when it matches the outgoing artifact.
        /// </summary>
        private static bool TryOpenIncoming(
            string incomingPath,
            FileEnvelopeId expectedEnvelopeId,
            FileLayout outgoingLayout,
            int[] expectedOffsets,
            bool deleteOnClose,
            out MemoryMappedFile incomingFile,
            out MemoryMappedViewAccessor incomingView,
            out FileLayout incomingLayout)
        {
            incomingFile = null;
            incomingView = null;
            incomingLayout = default;
            FileStream incomingStream = null;
            MemoryMappedFile candidateFile = null;
            MemoryMappedViewAccessor candidateView = null;

            try
            {
                FileOptions options = FileOptions.RandomAccess;
                if (deleteOnClose)
                {
                    options |= FileOptions.DeleteOnClose;
                }

                incomingStream = new FileStream(
                    incomingPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    64 * 1024,
                    options);
                ReadEnvelope(incomingStream, expectedEnvelopeId, out long headerPosition);
                long fileLength = incomingStream.Length;
                candidateFile = MemoryMappedFile.CreateFromFile(
                    incomingStream,
                    mapName: null,
                    capacity: 0,
                    MemoryMappedFileAccess.Read,
                    HandleInheritability.None,
                    leaveOpen: false);
                incomingStream = null;
                candidateView = candidateFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                FileLayout candidateLayout = ReadAndValidateLayout(
                    candidateView,
                    fileLength,
                    FileKind.Incoming,
                    headerPosition);

                if (candidateLayout.NodeCount != outgoingLayout.NodeCount
                    || candidateLayout.EdgeCount != outgoingLayout.EdgeCount
                    || candidateLayout.EdgeByteWidth != outgoingLayout.EdgeByteWidth)
                {
                    throw new InvalidDataException("The incoming directed graph artifact does not match the outgoing artifact.");
                }

                var actualOffsets = new int[expectedOffsets.Length];
                candidateView.ReadArray(candidateLayout.OffsetsPosition, actualOffsets, 0, actualOffsets.Length);
                ValidateOffsets(actualOffsets, candidateLayout.EdgeCount, "incoming");
                for (int i = 0; i < actualOffsets.Length; i++)
                {
                    if (actualOffsets[i] != expectedOffsets[i])
                    {
                        throw new InvalidDataException($"The incoming directed graph offset at index {i} does not match the outgoing artifact.");
                    }
                }

                incomingFile = candidateFile;
                incomingView = candidateView;
                incomingLayout = candidateLayout;
                candidateFile = null;
                candidateView = null;
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (InvalidDataException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            finally
            {
                candidateView?.Dispose();
                candidateFile?.Dispose();
                incomingStream?.Dispose();
            }
        }

        #endregion

        #region Serialization helpers

        private static void WriteOffsets(
            BinaryWriter writer,
            MutableDirectedGraph graph,
            int nodeCount,
            int edgeCount,
            bool isIncoming,
            bool captureOffsets,
            out int[] offsets)
        {
            offsets = captureOffsets ? new int[GetOffsetCount(nodeCount)] : null;

            int offset = 0;
            writer.Write(offset);
            for (uint node = 1; node <= nodeCount; node++)
            {
                if (offsets != null)
                {
                    offsets[node] = offset;
                }

                writer.Write(offset);
                offset = checked(offset + graph.GetSealedEdgeCount(node, isIncoming));
            }

            if (offset != edgeCount)
            {
                string direction = isIncoming ? "incoming" : "outgoing";
                throw new InvalidDataException($"Computed {offset} {direction} edges instead of {edgeCount}.");
            }

            if (offsets != null)
            {
                offsets[nodeCount + 1] = offset;
            }

            writer.Write(offset);
        }

        private static void AddOutgoingEdgeWriters(
            List<Task> tasks,
            string path,
            MutableDirectedGraph graph,
            int nodeCount,
            FileLayout layout,
            int[] offsets)
        {
            int partitionCount = GetConstructionParallelism();
            for (int partition = 0; partition < partitionCount; partition++)
            {
                int startNode = 1 + checked((int)(((long)nodeCount * partition) / partitionCount));
                int endNode = checked((int)(((long)nodeCount * (partition + 1)) / partitionCount));
                long position = layout.EdgesPosition + ((long)offsets[startNode] * layout.EdgeByteWidth);
                long length = (long)(offsets[endNode + 1] - offsets[startNode]) * layout.EdgeByteWidth;
                tasks.Add(Task.Run(() => WriteOutgoingEdges(path, graph, startNode, endNode, layout.EdgeByteWidth, position, length)));
            }
        }

        private static void WriteOutgoingEdges(
            string path,
            MutableDirectedGraph graph,
            int startNode,
            int endNode,
            int edgeByteWidth,
            long position,
            long length)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 64 * 1024, FileOptions.SequentialScan);
            stream.Position = position;
            var buffer = new byte[64 * 1024];
            int bufferIndex = 0;
            int valueBitWidth = edgeByteWidth * 8;
            for (uint node = (uint)startNode; node <= endNode; node++)
            {
                foreach (var edge in graph.GetSealedOutgoingEdges(node))
                {
                    uint packed = edge.OtherNode.Value - 1;
                    if (edge.IsLight)
                    {
                        packed |= 1U << (valueBitWidth - 1);
                    }

                    if (buffer.Length - bufferIndex < edgeByteWidth)
                    {
                        stream.Write(buffer, 0, bufferIndex);
                        bufferIndex = 0;
                    }

                    WritePackedEdge(buffer, bufferIndex, packed, edgeByteWidth);
                    bufferIndex += edgeByteWidth;
                }
            }

            if (bufferIndex != 0)
            {
                stream.Write(buffer, 0, bufferIndex);
            }

            if (stream.Position != position + length)
            {
                throw new InvalidDataException($"Wrote {stream.Position - position} outgoing edge bytes instead of {length}.");
            }
        }

        private static unsafe void BuildAndWriteIncomingEdges(
            MemoryMappedViewAccessor outgoingView,
            MemoryMappedViewAccessor incomingView,
            FileLayout outgoingLayout,
            FileLayout incomingLayout,
            int[] outgoingOffsets,
            int[] incomingOffsets)
        {
            var incomingCursors = new int[incomingOffsets.Length];
            Array.Copy(incomingOffsets, incomingCursors, incomingOffsets.Length);
            byte* outgoingPointer = null;
            byte* incomingPointer = null;

            // Acquire each view pointer once. The parallel loop then scatters reverse edges directly into the
            // incoming mapping without an edge-sized temporary managed buffer.
            outgoingView.SafeMemoryMappedViewHandle.AcquirePointer(ref outgoingPointer);
            try
            {
                incomingView.SafeMemoryMappedViewHandle.AcquirePointer(ref incomingPointer);
                try
                {
                    outgoingPointer += outgoingView.PointerOffset;
                    incomingPointer += incomingView.PointerOffset;
                    int partitionCount = GetConstructionParallelism();
                    uint nodeMask = GetNodeIdMask(outgoingLayout.EdgeByteWidth);
                    uint lightMask = GetLightEdgeMask(outgoingLayout.EdgeByteWidth);

                    Parallel.For(
                        0,
                        partitionCount,
                        partition =>
                        {
                            int startNode = 1 + checked((int)(((long)outgoingLayout.NodeCount * partition) / partitionCount));
                            int endNode = checked((int)(((long)outgoingLayout.NodeCount * (partition + 1)) / partitionCount));
                            for (int source = startNode; source <= endNode; source++)
                            {
                                for (int edgeIndex = outgoingOffsets[source]; edgeIndex < outgoingOffsets[source + 1]; edgeIndex++)
                                {
                                    long outgoingEdgePosition = outgoingLayout.EdgesPosition + ((long)edgeIndex * outgoingLayout.EdgeByteWidth);
                                    byte* outgoingEdge = GetPointerAtOffset(
                                        outgoingPointer,
                                        outgoingLayout.FileLength,
                                        outgoingEdgePosition,
                                        outgoingLayout.EdgeByteWidth);
                                    uint packed = ReadPackedEdgeFromPointer(outgoingEdge, outgoingLayout.EdgeByteWidth);
                                    int target = checked((int)((packed & nodeMask) + 1));
                                    if (target > outgoingLayout.NodeCount)
                                    {
                                        throw new InvalidDataException($"Edge target {target} exceeds node count {outgoingLayout.NodeCount}.");
                                    }

                                    int incomingIndex = Interlocked.Increment(ref incomingCursors[target]) - 1;
                                    if (incomingIndex >= incomingOffsets[target + 1])
                                    {
                                        throw new InvalidDataException($"Incoming edge count for node {target} exceeds its declared contiguous edge range.");
                                    }

                                    uint incomingPacked = (uint)(source - 1);
                                    if ((packed & lightMask) != 0)
                                    {
                                        incomingPacked |= lightMask;
                                    }

                                    long incomingEdgePosition = incomingLayout.EdgesPosition + ((long)incomingIndex * incomingLayout.EdgeByteWidth);
                                    byte* incomingEdge = GetPointerAtOffset(
                                        incomingPointer,
                                        incomingLayout.FileLength,
                                        incomingEdgePosition,
                                        incomingLayout.EdgeByteWidth);
                                    WritePackedEdgeToPointer(incomingEdge, incomingPacked, incomingLayout.EdgeByteWidth);
                                }
                            }
                        });
                }
                finally
                {
                    incomingView.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }
            finally
            {
                outgoingView.SafeMemoryMappedViewHandle.ReleasePointer();
            }

        }

        private static void ValidateOffsets(int[] offsets, int edgeCount, string description)
        {
            // Offset tables are serialized input. Use explicit data errors rather than contracts so corrupt cache
            // artifacts are rejected consistently in release builds.
            if (offsets.Length < 2)
            {
                throw new InvalidDataException($"The {description} offset table does not contain the required sentinel entries.");
            }

            if (offsets[0] != 0)
            {
                throw new InvalidDataException($"The unused index zero in the {description} offset table must be zero.");
            }

            if (offsets[1] != 0)
            {
                throw new InvalidDataException($"The first {description} edge range must start at edge index zero.");
            }

            if (offsets[offsets.Length - 1] != edgeCount)
            {
                throw new InvalidDataException($"The terminal {description} offset must equal edge count {edgeCount}.");
            }

            int previous = 0;
            for (int i = 1; i < offsets.Length; i++)
            {
                int current = offsets[i];
                if (current < previous || current > edgeCount)
                {
                    throw new InvalidDataException($"Invalid {description} edge offset at index {i}.");
                }

                previous = current;
            }
        }

        private static void WritePackedEdge(byte[] buffer, int position, uint value, int edgeByteWidth)
        {
            for (int i = 0; i < edgeByteWidth; i++)
            {
                buffer[position + i] = unchecked((byte)(value >> (i * 8)));
            }
        }

        private static unsafe uint ReadPackedEdgeFromPointer(byte* position, int edgeByteWidth)
        {
            uint value = 0;
            for (int i = 0; i < edgeByteWidth; i++)
            {
                value |= (uint)position[i] << (i * 8);
            }

            return value;
        }

        private static unsafe void WritePackedEdgeToPointer(byte* position, uint value, int edgeByteWidth)
        {
            for (int i = 0; i < edgeByteWidth; i++)
            {
                position[i] = unchecked((byte)(value >> (i * 8)));
            }
        }

        #endregion

        #region File format types

        private enum FileKind
        {
            /// <summary>
            /// The cached artifact containing outgoing edges, both offset tables, and node heights.
            /// </summary>
            Outgoing = 1,

            /// <summary>
            /// The derived artifact containing incoming edges and their offset table.
            /// </summary>
            Incoming = 2,
        }

        private readonly struct FileLayout
        {
            /// <summary>
            /// Describes the validated positions and sizes of one graph artifact.
            /// </summary>
            internal FileLayout(
                FileKind kind,
                int nodeCount,
                int edgeCount,
                int edgeByteWidth,
                long headerPosition,
                long offsetsPosition,
                long secondaryOffsetsPosition,
                long heightsPosition,
                long edgesPosition,
                long fileLength,
                long edgeDataLength)
            {
                Kind = kind;
                NodeCount = nodeCount;
                EdgeCount = edgeCount;
                EdgeByteWidth = edgeByteWidth;
                HeaderPosition = headerPosition;
                OffsetsPosition = offsetsPosition;
                SecondaryOffsetsPosition = secondaryOffsetsPosition;
                HeightsPosition = heightsPosition;
                EdgesPosition = edgesPosition;
                FileLength = fileLength;
                EdgeDataLength = edgeDataLength;
            }

            #endregion

            internal FileKind Kind { get; }
            internal int NodeCount { get; }
            internal int EdgeCount { get; }
            internal int EdgeByteWidth { get; }
            internal long HeaderPosition { get; }
            internal long OffsetsPosition { get; }
            internal long SecondaryOffsetsPosition { get; }
            internal long HeightsPosition { get; }
            internal long EdgesPosition { get; }
            internal long FileLength { get; }
            internal long EdgeDataLength { get; }
        }

    }
}
