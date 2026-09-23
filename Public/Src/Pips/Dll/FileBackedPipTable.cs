// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Diagnostics;

namespace BuildXL.Pips
{
    /// <summary>
    /// A pip table whose serialized pip bodies are written through to a file during construction.
    /// </summary>
    /// <remarks>
    /// Pip bodies are serialized in the background as they are added and appended to a temporary file.
    /// The table retains compact mutable metadata and weak references in memory so common queries do not
    /// require deserializing the full pip.
    ///
    /// After serialization completes, the backing file is mapped read-only. Each pip is located by its
    /// recorded offset and length, copied into a pooled buffer, and deserialized when first requested. The
    /// resulting pip may then be retained by its weak reference and reused by subsequent requests.
    ///
    /// The persisted format contains an index of record lengths followed by the contiguous pip bodies and
    /// their mutable metadata. Loading that format from a local file maps the existing payload rather than
    /// copying all serialized pip bytes into managed memory.
    /// </remarks>
    public sealed class FileBackedPipTable : IPipTable
    {
        internal const uint FormatMarker = 0x46505442;

        /// <summary>
        /// Returns whether the stream at its current position contains the file-backed PipTable format.
        /// </summary>
        public static bool IsFileBackedFormat(Stream stream)
        {
            Contract.RequiresNotNull(stream);
            Contract.Requires(stream.CanSeek);

            long position = stream.Position;
            try
            {
                int first = stream.ReadByte();
                int second = stream.ReadByte();
                int third = stream.ReadByte();
                int fourth = stream.ReadByte();
                if (fourth < 0)
                {
                    return false;
                }

                uint marker =
                    (uint)first |
                    ((uint)second << 8) |
                    ((uint)third << 16) |
                    ((uint)fourth << 24);
                return marker == FormatMarker;
            }
            finally
            {
                stream.Position = position;
            }
        }

        /// <summary>
        /// Stores serialized pip bodies in a file so they do not remain in managed memory.
        /// </summary>
        /// <remarks>
        /// During construction, serialized pips are appended to a temporary file and their offsets and lengths are
        /// recorded in memory. Reads can use the file stream until <see cref="Complete"/> flushes the file and creates
        /// a read-only memory map. Subsequent reads copy only the requested record into a pooled buffer for
        /// deserialization. A deserialized store maps the pip payload directly from the serialized graph file instead
        /// of creating a temporary file.
        /// </remarks>
        private sealed class FileBackedPipStore : IDisposable
        {
            // This is the default Stream.CopyTo buffer size: 20 4-KiB pages while remaining below the large object heap threshold.
            private const int CopyBufferSize = 81920;

            private readonly object m_fileLock = new object();
            private readonly PathTable m_pathTable;
            private readonly bool m_debug;
            private readonly int m_initialBufferSize;
            private readonly ConcurrentDenseIndex<long> m_endOffsets;
            private readonly byte[] m_copyBuffer = new byte[CopyBufferSize];
            private readonly long m_payloadOffset;
            private FileStream m_fileStream;
            private MemoryMappedFile m_memoryMappedFile;
            private MemoryMappedViewAccessor m_viewAccessor;
            private int m_lastId;
            private int m_completed;
            private int m_disposed;

            /// <summary>
            /// Creates a writable store backed by a temporary file in <paramref name="storageDirectory"/>, or the
            /// system temporary directory when no directory is provided.
            /// </summary>
            public FileBackedPipStore(PathTable pathTable, int initialBufferSize, bool debug, string storageDirectory)
            {
                Contract.Requires(pathTable != null);
                Contract.Requires(initialBufferSize > 0);

                m_pathTable = pathTable;
                m_debug = debug;
                m_initialBufferSize = initialBufferSize;
                m_endOffsets = new ConcurrentDenseIndex<long>(debug);

                storageDirectory = storageDirectory ?? Path.GetTempPath();
                Directory.CreateDirectory(storageDirectory);
                string path = Path.Combine(storageDirectory, "BuildXL.PipTable." + Guid.NewGuid().ToString("N") + ".tmp");
                m_fileStream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read | FileShare.Delete,
                    bufferSize: CopyBufferSize,
                    options: FileOptions.DeleteOnClose);
            }

            private FileBackedPipStore(
                PathTable pathTable,
                bool debug,
                ConcurrentDenseIndex<long> endOffsets,
                int lastId,
                long payloadOffset,
                MemoryMappedFile memoryMappedFile,
                MemoryMappedViewAccessor viewAccessor)
            {
                m_pathTable = pathTable;
                m_debug = debug;
                m_initialBufferSize = 0;
                m_endOffsets = endOffsets;
                m_lastId = lastId;
                m_payloadOffset = payloadOffset;
                m_memoryMappedFile = memoryMappedFile;
                m_viewAccessor = viewAccessor;
                m_completed = 1;
            }

            /// <summary>
            /// Gets the number of file streams used to store pip bodies.
            /// </summary>
            public int PageStreamsCount => 1;

            /// <summary>
            /// Gets the number of serialized pip records in the store.
            /// </summary>
            public int Count => m_lastId;

            /// <summary>
            /// Gets the managed-memory capacity used for serialized pip pages, which is zero for this file-backed store.
            /// </summary>
            public long MemorySize => 0;

            /// <summary>
            /// Gets the managed-memory space used for serialized pip pages, which is zero for this file-backed store.
            /// </summary>
            public long MemoryUsed => 0;

            /// <summary>
            /// Serializes and appends a pip record, returning an identifier for its recorded file location.
            /// </summary>
            public PageableStoreId Write(Action<BuildXLWriter> serializer)
            {
                Contract.RequiresNotNull(serializer);
                Contract.Requires(Volatile.Read(ref m_completed) == 0);

                using (var streamWrapper = Pools.MemoryStreamPool.GetInstance())
                {
                    var stream = streamWrapper.Instance;
                    if (stream.Capacity < m_initialBufferSize)
                    {
                        stream.Capacity = m_initialBufferSize;
                    }

                    using (var writer = new PipWriter(m_debug, stream, leaveOpen: true, logStats: true))
                    {
                        serializer(writer);
                    }

                    int length = checked((int)stream.Length);
                    long offset;
                    int id;
                    lock (m_fileLock)
                    {
                        offset = m_fileStream.Position;
                        stream.Position = 0;
                        stream.CopyTo(m_fileStream, CopyBufferSize);
                        id = ++m_lastId;
                        m_endOffsets[(uint)id] = checked(offset + length);
                    }

                    return new PageableStoreId((uint)id);
                }
            }

            /// <summary>
            /// Reads and deserializes a pip record from its recorded file location.
            /// </summary>
            public T Read<T>(PageableStoreId id, Func<BuildXLReader, T> deserializer)
            {
                Contract.Requires(id.IsValid);
                Contract.RequiresNotNull(deserializer);

                GetRecordLocation(id.Value, out long offset, out int length);
                byte[] bytes = RentBuffer(length);
                try
                {
                    if (Volatile.Read(ref m_completed) != 0)
                    {
                        // ReadArray is safe to call concurrently because it uses an explicit offset and
                        // SafeBuffer keeps the memory-mapped handle alive for the duration of each read.
                        int bytesRead = m_viewAccessor.ReadArray(offset, bytes, 0, length);
                        if (bytesRead != length)
                        {
                            throw new EndOfStreamException();
                        }
                    }
                    else
                    {
                        lock (m_fileLock)
                        {
                            m_fileStream.Flush();
                            long previousPosition = m_fileStream.Position;
                            m_fileStream.Position = offset;
                            int totalRead = 0;
                            while (totalRead < length)
                            {
                                int read = m_fileStream.Read(bytes, totalRead, length - totalRead);
                                if (read == 0)
                                {
                                    throw new EndOfStreamException();
                                }

                                totalRead += read;
                            }

                            m_fileStream.Position = previousPosition;
                        }
                    }

                    using (var stream = new MemoryStream(bytes, 0, length, writable: false))
                    using (var reader = new PipReader(m_debug, m_pathTable.StringTable, stream, leaveOpen: false))
                    {
                        return deserializer(reader);
                    }
                }
                finally
                {
                    ReturnBuffer(bytes);
                }
            }

            private static byte[] RentBuffer(int length)
            {
#if NETCOREAPP
                return System.Buffers.ArrayPool<byte>.Shared.Rent(length);
#else
                return new byte[length];
#endif
            }

            private static void ReturnBuffer(byte[] buffer)
            {
#if NETCOREAPP
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
#endif
            }

            /// <summary>
            /// Flushes the writable file and creates the read-only memory map used by subsequent reads.
            /// </summary>
            public void Complete()
            {
                lock (m_fileLock)
                {
                    if (Volatile.Read(ref m_completed) != 0)
                    {
                        return;
                    }

                    m_fileStream.Flush(flushToDisk: false);
                    if (m_fileStream.Length > 0)
                    {
                        CreateMemoryMap(m_fileStream, out m_memoryMappedFile, out m_viewAccessor);
                    }

                    Volatile.Write(ref m_completed, 1);
                }
            }

            /// <summary>
            /// Writes the store format metadata and serialized pip payload to the final PipTable file.
            /// </summary>
            /// <remarks>
            /// This copies the pip bodies from the construction-time temporary file into <paramref name="writer"/>.
            /// The current store continues using the temporary file until disposal, but a subsequently deserialized
            /// store maps the resulting PipTable file directly as its backing storage.
            /// </remarks>
            public void Serialize(BuildXLWriter writer)
            {
                Contract.RequiresNotNull(writer);

                Complete();
                writer.Write(FormatMarker);
                writer.Write(m_debug);
                writer.Write(m_lastId);
                for (uint i = 1; i <= m_lastId; i++)
                {
                    GetRecordLocation(i, out _, out int length);
                    writer.Write(length);
                }

                lock (m_copyBuffer)
                {
                    for (uint i = 1; i <= m_lastId; i++)
                    {
                        GetRecordLocation(i, out long offset, out int length);
                        int totalRead = 0;
                        while (totalRead < length)
                        {
                            int bytesToRead = Math.Min(m_copyBuffer.Length, length - totalRead);
                            int bytesRead = m_viewAccessor.ReadArray(offset + totalRead, m_copyBuffer, 0, bytesToRead);
                            if (bytesRead != bytesToRead)
                            {
                                throw new EndOfStreamException();
                            }

                            writer.Write(m_copyBuffer, 0, bytesRead);
                            totalRead += bytesRead;
                        }
                    }
                }
            }

            /// <summary>
            /// Reads store metadata and maps the serialized pip payload directly from the reader's file stream.
            /// </summary>
            public static FileBackedPipStore Deserialize(BuildXLReader reader, PathTable pathTable)
            {
                bool debug = reader.ReadBoolean();
                int count = reader.ReadInt32();
                if (count < 0)
                {
                    throw new InvalidDataException("A file-backed PipTable has a negative record count.");
                }

                int[] lengths = new int[count];
                long payloadLength = 0;
                for (int i = 0; i < count; i++)
                {
                    int length = reader.ReadInt32();
                    if (length < 0)
                    {
                        throw new InvalidDataException("A serialized pip has a negative length.");
                    }

                    lengths[i] = length;
                    payloadLength = checked(payloadLength + length);
                }

                if (!(reader.BaseStream is FileStream fileStream))
                {
                    throw new InvalidDataException("A file-backed PipTable must be deserialized from an unbuffered file stream.");
                }

                long payloadOffset = fileStream.Position;
                if (payloadOffset > fileStream.Length - payloadLength)
                {
                    throw new EndOfStreamException("The serialized pip payload exceeds the PipTable file length.");
                }

                var endOffsets = new ConcurrentDenseIndex<long>(debug);
                long offset = payloadOffset;
                for (uint i = 1; i <= count; i++)
                {
                    offset += lengths[i - 1];
                    endOffsets[i] = offset;
                }

                CreateMemoryMap(fileStream, out var memoryMappedFile, out var viewAccessor);
                fileStream.Position = offset;
                return new FileBackedPipStore(pathTable, debug, endOffsets, count, payloadOffset, memoryMappedFile, viewAccessor);
            }

            private void GetRecordLocation(uint id, out long offset, out int length)
            {
                long endOffset = m_endOffsets[id];
                offset = id == 1 ? m_payloadOffset : m_endOffsets[id - 1];
                length = checked((int)(endOffset - offset));
            }

            private static void CreateMemoryMap(
                FileStream fileStream,
                out MemoryMappedFile memoryMappedFile,
                out MemoryMappedViewAccessor viewAccessor)
            {
                memoryMappedFile = MemoryMappedFile.CreateFromFile(
                    fileStream,
                    mapName: null,
                    capacity: 0,
                    access: MemoryMappedFileAccess.Read,
                    inheritability: HandleInheritability.None,
                    leaveOpen: true);
                viewAccessor = memoryMappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            }

            /// <summary>
            /// Releases the file and mapping resources and deletes a temporary backing file when one was created.
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }

                m_viewAccessor?.Dispose();
                m_memoryMappedFile?.Dispose();
                m_fileStream?.Dispose();
            }
        }

        private readonly FileBackedPipStore m_store;
        private readonly ConcurrentDenseIndex<MutablePipState> m_mutables;
        private readonly int[] m_deserializationContexts = new int[(int)PipQueryContext.End];
        private readonly PipTableSerializationScheduler m_serializationScheduler;
        private readonly Lazy<Task> m_serializationCompletion;
        private readonly Pip m_dummyHashSourceFilePip;
        private int m_lastId;
        private int m_count;
        private int m_writes;
        private long m_readTicks;
        private long m_writeTicks;

        /// <summary>
        /// Creates a file-backed table for graph construction.
        /// </summary>
        public FileBackedPipTable(
            PathTable pathTable,
            SymbolTable symbolTable,
            int initialBufferSize,
            int maxDegreeOfParallelism,
            bool debug,
            string storageDirectory = null)
            : this(
                  pathTable,
                  new FileBackedPipStore(pathTable, initialBufferSize, debug, storageDirectory),
                  new ConcurrentDenseIndex<MutablePipState>(debug),
                  maxDegreeOfParallelism,
                  debug)
        {
            Contract.RequiresNotNull(symbolTable);
        }

        /// <summary>
        /// Converts an existing table to the file-backed representation for analyzer benchmarks.
        /// </summary>
        /// <remarks>
        /// Product code constructs or deserializes file-backed tables directly and does not use this conversion path.
        /// </remarks>
        internal static async Task<FileBackedPipTable> ConvertAsync(
            IPipTable source,
            PathTable pathTable,
            SymbolTable symbolTable,
            int initialBufferSize,
            int maxDegreeOfParallelism,
            string storageDirectory)
        {
            Contract.RequiresNotNull(source);
            Contract.RequiresNotNull(pathTable);
            Contract.RequiresNotNull(symbolTable);

            var result = new FileBackedPipTable(
                pathTable,
                symbolTable,
                initialBufferSize,
                maxDegreeOfParallelism,
                debug: false,
                storageDirectory);

            try
            {
                foreach (var pipId in source.StableKeys)
                {
                    var pip = source.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
                    pip.ResetPipId();
                    result.Add(pipId.Value, pip);
                }

                result.StopBackgroundSerialization();
                await result.WhenDone();
                return result;
            }
            catch (Exception conversionException)
            {
                result.StopBackgroundSerialization();
                await result.WhenDone().ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted)
                        {
                            conversionException.Data[nameof(PipTableSerializationScheduler)] = task.Exception;
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                result.Dispose();
                throw;
            }
        }

        private FileBackedPipTable(
            PathTable pathTable,
            FileBackedPipStore store,
            ConcurrentDenseIndex<MutablePipState> mutables,
            int maxDegreeOfParallelism,
            bool debug = false)
        {
            Contract.RequiresNotNull(pathTable);
            Contract.RequiresNotNull(store);
            Contract.RequiresNotNull(mutables);

            m_store = store;
            m_mutables = mutables;
            m_serializationScheduler = new PipTableSerializationScheduler(maxDegreeOfParallelism, debug, ProcessQueueItem);
            m_serializationCompletion = new Lazy<Task>(
                () => m_serializationScheduler.WhenDone(),
                LazyThreadSafetyMode.ExecutionAndPublication);
            m_dummyHashSourceFilePip = new HashSourceFile(
                FileArtifact.CreateSourceFile(
                    AbsolutePath.Create(pathTable, PathGeneratorUtilities.GetAbsolutePath("B", "DUMMY_HASH_SOURCE_FILE"))));
        }

        /// <inheritdoc />
        public bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public bool RequiresUncompressedSerialization => true;

        /// <inheritdoc />
        public long Reads
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_deserializationContexts.Select(i => (long)i).Sum();
            }
        }

        /// <inheritdoc />
        public int Writes
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return Volatile.Read(ref m_writes);
            }
        }

        /// <inheritdoc />
        public IEnumerable<KeyValuePair<PipQueryContext, int>> DeserializationContexts
        {
            get
            {
                Contract.Requires(!IsDisposed);
                var result = new List<KeyValuePair<PipQueryContext, int>>();
                for (PipQueryContext context = 0; context < PipQueryContext.End; context++)
                {
                    int count = m_deserializationContexts[(int)context];
                    if (count > 0)
                    {
                        result.Add(new KeyValuePair<PipQueryContext, int>(context, count));
                    }
                }

                return result;
            }
        }

        /// <inheritdoc />
        public int Count
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return Volatile.Read(ref m_count);
            }
        }

        /// <inheritdoc />
        public IList<PipId> StableKeys
        {
            get
            {
                Contract.Requires(!IsDisposed);
                int count = Volatile.Read(ref m_count);
                int max = Volatile.Read(ref m_lastId);
                Contract.Assume(count == max);
                return new PipIdRangeList(max);
            }
        }

        /// <inheritdoc />
        public IEnumerable<PipId> Keys
        {
            get
            {
                Contract.Requires(!IsDisposed);
                int count = Volatile.Read(ref m_count);
                int max = Volatile.Read(ref m_lastId);
                return count == max ? (IEnumerable<PipId>)new PipIdRangeList(max) : GetValidKeys(max);
            }
        }

        /// <inheritdoc />
        public int PageStreamsCount
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_store.PageStreamsCount;
            }
        }

        /// <inheritdoc />
        public long Size
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_store.MemorySize;
            }
        }

        /// <inheritdoc />
        public long WritesMilliseconds => Volatile.Read(ref m_writeTicks) * 1000 / Stopwatch.Frequency;

        /// <inheritdoc />
        public long ReadsMilliseconds => Volatile.Read(ref m_readTicks) * 1000 / Stopwatch.Frequency;

        /// <inheritdoc />
        public long Used
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_store.MemoryUsed;
            }
        }

        /// <inheritdoc />
        public int Alive
        {
            get
            {
                Contract.Requires(!IsDisposed);
                int alive = 0;
                uint max = (uint)m_lastId;
                for (uint i = 1; i <= max; i++)
                {
                    MutablePipState mutable = m_mutables[i];
                    if (mutable != null && mutable.IsAlive)
                    {
                        alive++;
                    }
                }

                return alive;
            }
        }

        /// <summary>
        /// Adds a pip and schedules its body for serialization to the backing file.
        /// </summary>
        public PipId Add(uint nodeIdValue, Pip pip)
        {
            Contract.Requires(!IsDisposed);
            Contract.RequiresNotNull(pip);
            Contract.Requires(nodeIdValue > 0);
            Contract.Requires(!pip.PipId.IsValid);

            var pipId = new PipId(nodeIdValue);
            pip.PipId = pipId;
            var mutable = MutablePipState.Create(pip);
            Contract.Assume(m_mutables[nodeIdValue] == null);
            m_mutables[nodeIdValue] = mutable;
            Interlocked.Increment(ref m_count);

            while (true)
            {
                int oldMaxPipValue = Volatile.Read(ref m_lastId);
                int newMaxPipValue = Math.Max(oldMaxPipValue, (int)nodeIdValue);
                if (oldMaxPipValue == newMaxPipValue ||
                    Interlocked.CompareExchange(ref m_lastId, newMaxPipValue, oldMaxPipValue) == oldMaxPipValue)
                {
                    break;
                }
            }

            m_serializationScheduler.ScheduleSerialization(pip, mutable);
            return pipId;
        }

        /// <summary>
        /// Returns whether the identifier refers to a pip in this table.
        /// </summary>
        public bool IsValid(PipId pipId)
        {
            return (pipId.Value > 0 && pipId.Value <= m_lastId && m_mutables[pipId.Value] != null) ||
                pipId == PipId.DummyHashSourceFilePipId;
        }

        #region Pip metadata

        /// <summary>Gets service information without hydrating the pip.</summary>
        public ServiceInfo GetServiceInfo(PipId pipId)
        {
            Contract.Requires(IsValid(pipId));
            var mutable = m_mutables[pipId.Value] as ProcessMutablePipState;
            return mutable?.ServiceInfo ?? ServiceInfo.None;
        }

        /// <summary>Gets a pip type without hydrating the pip.</summary>
        public PipType GetPipType(PipId pipId)
        {
            Contract.Requires(IsValid(pipId));
            return pipId == PipId.DummyHashSourceFilePipId ? PipType.HashSourceFile : m_mutables[pipId.Value].PipType;
        }

        /// <summary>Gets process options without hydrating the pip.</summary>
        public Operations.Process.Options GetProcessOptions(PipId pipId)
        {
            var mutable = GetMutable(pipId) as ProcessMutablePipState;
            Contract.Assert(mutable != null);
            return mutable.ProcessOptions;
        }

        /// <summary>Gets the rewrite policy without hydrating the pip.</summary>
        public RewritePolicy GetRewritePolicy(PipId pipId)
        {
            var mutable = GetMutable(pipId) as ProcessMutablePipState;
            Contract.Assert(mutable != null);
            return mutable.RewritePolicy;
        }

        /// <summary>Gets the process executable path without hydrating the pip.</summary>
        public AbsolutePath GetProcessExecutablePath(PipId pipId)
        {
            var mutable = GetMutable(pipId) as ProcessMutablePipState;
            Contract.Assert(mutable != null);
            return mutable.ExecutablePath;
        }

        /// <summary>Gets the seal directory kind without hydrating the pip.</summary>
        public SealDirectoryKind GetSealDirectoryKind(PipId pipId)
        {
            var mutable = GetMutable(pipId);
            return mutable.PipType == PipType.SealDirectory
                ? ((SealDirectoryMutablePipState)mutable).SealDirectoryKind
                : default;
        }

        /// <summary>Gets the seal directory root without hydrating the pip.</summary>
        public AbsolutePath GetSealDirectoryRoot(PipId pipId)
        {
            var mutable = GetMutable(pipId) as SealDirectoryMutablePipState;
            Contract.Assert(mutable != null);
            return mutable.DirectoryRoot;
        }

        /// <summary>Gets whether the seal directory should be scrubbed.</summary>
        public bool ShouldScrubFullSealDirectory(PipId pipId)
        {
            var mutable = GetMutable(pipId);
            return mutable.PipType == PipType.SealDirectory && ((SealDirectoryMutablePipState)mutable).Scrub;
        }

        /// <summary>Gets whether the seal directory is composite.</summary>
        public bool IsSealDirectoryComposite(PipId pipId)
        {
            var mutable = GetMutable(pipId);
            return mutable.PipType == PipType.SealDirectory && ((SealDirectoryMutablePipState)mutable).IsComposite;
        }

        /// <summary>Gets source seal directory patterns without hydrating the pip.</summary>
        public ReadOnlyArray<StringId> GetSourceSealDirectoryPatterns(PipId pipId)
        {
            var mutable = GetMutable(pipId);
            if (mutable.PipType == PipType.SealDirectory)
            {
                var sealMutable = (SealDirectoryMutablePipState)mutable;
                Contract.Assert(sealMutable.SealDirectoryKind.IsSourceSeal(), "Pattern is only available for source seal directories.");
                return sealMutable.Patterns;
            }

            return ReadOnlyArray<StringId>.Empty;
        }

        /// <summary>Gets a pip semistable hash without hydrating the pip.</summary>
        public long GetPipSemiStableHash(PipId pipId) => GetMutable(pipId).SemiStableHash;

        /// <summary>Gets the pip scheduling priority.</summary>
        public int GetPipPriority(PipId pipId)
        {
            var mutable = GetMutable(pipId);
            return mutable.PipType == PipType.Process ? ((ProcessMutablePipState)mutable).Priority : 0;
        }

        /// <summary>Gets a formatted pip semistable hash without hydrating the pip.</summary>
        public string GetFormattedSemiStableHash(PipId pipId) => Pip.FormatSemiStableHash(GetPipSemiStableHash(pipId));

        /// <summary>Gets the owning process module without hydrating the pip.</summary>
        public ModuleId GetProcessModuleId(PipId pipId)
        {
            var mutable = GetMutable(pipId) as ProcessMutablePipState;
            Contract.Assert(mutable != null);
            return mutable.ModuleId;
        }

        /// <inheritdoc />
        public StringId GetProcessToolDescription(PipId pipId) => GetProcessMutableState(pipId).ToolDescription;

        /// <inheritdoc />
        public QualifierId GetProcessQualifierId(PipId pipId) => GetProcessMutableState(pipId).QualifierId;

        /// <inheritdoc />
        public int GetProcessWeight(PipId pipId) => GetProcessMutableState(pipId).Weight;

        /// <inheritdoc />
        public int GetProcessFileDependencyCount(PipId pipId) => GetProcessMutableState(pipId).NumFileDependencies;

        /// <inheritdoc />
        public int GetProcessDirectoryDependencyCount(PipId pipId) => GetProcessMutableState(pipId).NumDirectoryDependencies;

        /// <inheritdoc />
        public int GetProcessFileOutputCount(PipId pipId) => GetProcessMutableState(pipId).NumFileOutputs;

        /// <inheritdoc />
        public int GetProcessDirectoryOutputCount(PipId pipId) => GetProcessMutableState(pipId).NumDirectoryOutputs;

        private ProcessMutablePipState GetProcessMutableState(PipId pipId)
        {
            var mutable = GetMutable(pipId) as ProcessMutablePipState;
            Contract.Assert(mutable != null);
            return mutable;
        }

        /// <summary>Gets whether the process should fail the build immediately.</summary>
        public bool IsSucceedFast(PipId pipId) => (GetMutable(pipId) as ProcessMutablePipState)?.IsSucceedFast == true;

        /// <summary>Gets whether outputs must remain writable.</summary>
        public bool MustOutputsRemainWritable(PipId pipId) => GetMutable(pipId).MustOutputsRemainWritable();

        /// <summary>Gets whether the pip has preserve-outputs semantics.</summary>
        public bool IsPreservedOutputsPip(PipId pipId) => GetMutable(pipId).IsPreservedOutputsPip();

        /// <summary>Gets whether the pip is an incremental tool.</summary>
        public bool IsIncrementalTool(PipId pipId) => GetMutable(pipId).IsIncrementalTool();

        /// <summary>Gets whether the process has a preserve-output allowlist.</summary>
        public bool HasPreserveOutputAllowlist(PipId pipId)
        {
            var mutable = GetMutable(pipId) as ProcessMutablePipState;
            Contract.Assert(mutable != null);
            return mutable.HasPreserveOutputAllowlist();
        }

        /// <summary>Gets the process preserve-output trust level.</summary>
        public int GetProcessPreserveOutputsTrustLevel(PipId pipId)
        {
            var mutable = GetMutable(pipId) as ProcessMutablePipState;
            Contract.Assert(mutable != null);
            return mutable.PreserveOutputTrustLevel;
        }

        #endregion Pip metadata

        /// <summary>
        /// Returns the requested pip, hydrating its serialized body from the backing file when necessary.
        /// </summary>
        /// <remarks>
        /// This is potentially expensive. Hydration copies the serialized record into a pooled buffer and
        /// deserializes it. A weak reference may allow subsequent calls to reuse the hydrated pip.
        /// </remarks>
        public Pip HydratePip(PipId pipId, PipQueryContext context)
        {
            Contract.Requires(IsValid(pipId));
            if (pipId == PipId.DummyHashSourceFilePipId)
            {
                return m_dummyHashSourceFilePip;
            }

            return GetMutable(pipId).InternalGetOrSetPip(
                this,
                pipId,
                context,
                (table, pipId2, storeId, context2) =>
                {
                    Interlocked.Increment(ref table.m_deserializationContexts[(int)context2]);
                    return ExceptionUtilities.HandleRecoverableIOException(
                        (table, storeId, pipId2),
                        tuple =>
                        {
                            var start = Stopwatch.GetTimestamp();
                            var pip = tuple.Item1.m_store.Read<Pip>(tuple.storeId, reader => ((PipReader)reader).ReadPip());
                            pip.PipId = tuple.pipId2;
                            Interlocked.Add(ref tuple.Item1.m_readTicks, Stopwatch.GetTimestamp() - start);
                            return pip;
                        },
                        (tuple, ex) => ExceptionHandling.OnFatalException(ex));
                });
        }

        /// <summary>
        /// Signals that no additional pips will be added and stops accepting serialization work.
        /// </summary>
        public void StopBackgroundSerialization()
        {
            _ = m_serializationCompletion.Value;
        }

        /// <summary>
        /// Returns a task that completes after pending serialization finishes and the backing file is mapped.
        /// </summary>
        public Task WhenDone()
        {
            Contract.Requires(!IsDisposed);
            return CompleteStoreAsync();
        }

        /// <summary>
        /// Serializes the file-backed table, including pip bodies and their independently stored metadata.
        /// </summary>
        public void Serialize(BuildXLWriter writer, int maxDegreeOfParallelism)
        {
            Contract.Requires(!IsDisposed);
            Contract.RequiresNotNull(writer);
            Contract.Requires(maxDegreeOfParallelism == -1 || maxDegreeOfParallelism > 0);

            if (!m_serializationCompletion.IsValueCreated)
            {
                m_serializationScheduler.IncreaseConcurrencyTo(maxDegreeOfParallelism);
            }

            StopBackgroundSerialization();
            WhenDone().GetAwaiter().GetResult();

            int count = Volatile.Read(ref m_count);
            int maxId = Volatile.Read(ref m_lastId);
            if (count != maxId)
            {
                throw new InvalidOperationException(
                    $"FileBackedPipTable serialization requires contiguous pip IDs. Count={count}, MaxId={maxId}.");
            }

            if (m_store.Count != count)
            {
                throw new InvalidOperationException(
                    $"FileBackedPipTable serialization requires one stored pip body per pip. Count={count}, StoredPips={m_store.Count}.");
            }

            for (uint i = 1; i <= (uint)maxId; i++)
            {
                if (m_mutables[i] == null)
                {
                    throw new InvalidOperationException($"Missing metadata for PipId {i}.");
                }
            }

            m_store.Serialize(writer);

            writer.Write(maxId);
            for (uint i = 1; i <= (uint)maxId; i++)
            {
                m_mutables[i].Serialize(writer);
            }
        }

        /// <summary>
        /// Stops pending serialization and releases the backing file and memory map.
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            StopBackgroundSerialization();
            try
            {
                WhenDone().GetAwaiter().GetResult();
            }
            finally
            {
                m_store.Dispose();
                IsDisposed = true;
            }
        }

        /// <summary>
        /// Deserializes a file-backed table.
        /// </summary>
        internal static async Task<FileBackedPipTable> DeserializeAsync(
            BuildXLReader reader,
            Task<PathTable> pathTableTask,
            Task<SymbolTable> symbolTableTask,
            int maxDegreeOfParallelism)
        {
            Contract.RequiresNotNull(reader);

            uint formatMarker = reader.ReadUInt32();
            if (formatMarker != FormatMarker)
            {
                throw new InvalidDataException("The PipTable has an unrecognized serialization format.");
            }

            var pathTable = await pathTableTask;
            var symbolTable = await symbolTableTask;
            if (pathTable == null || symbolTable == null)
            {
                return null;
            }

            var store = FileBackedPipStore.Deserialize(reader, pathTable);
            try
            {
                var mutables = new ConcurrentDenseIndex<MutablePipState>(debug: false);
                int pipCount = reader.ReadInt32();
                if (pipCount != store.Count)
                {
                    throw new InvalidDataException("The file-backed PipTable record and metadata counts do not match.");
                }

                for (uint i = 0; i < pipCount; i++)
                {
                    mutables[i + 1] = MutablePipState.Deserialize(reader);
                }

                var table = new FileBackedPipTable(pathTable, store, mutables, maxDegreeOfParallelism);
                table.Complete(pipCount);
                return table;
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        private IEnumerable<PipId> GetValidKeys(int max)
        {
            for (uint i = 1; i <= (uint)max; i++)
            {
                if (m_mutables[i] != null)
                {
                    yield return new PipId(i);
                }
            }
        }

        private MutablePipState GetMutable(PipId pipId)
        {
            Contract.Requires(IsValid(pipId));
            MutablePipState mutable = m_mutables[pipId.Value];
            Contract.Assume(mutable != null);
            return mutable;
        }

        /// <summary>
        /// Completes initialization of a deserialized table.
        /// </summary>
        /// <remarks>
        /// Deserialization restores the table metadata in bulk, so this method initializes the counters before
        /// completing the otherwise-empty serialization scheduler and creating the store's read-only memory map.
        /// </remarks>
        private void Complete(int pipCount)
        {
            m_lastId = pipCount;
            m_count = pipCount;
            m_serializationCompletion.Value.GetAwaiter().GetResult();
            m_store.Complete();
        }

        /// <summary>
        /// Completes a table populated through background serialization during graph construction.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Complete(int)"/>, the counters were maintained as pips were added. This path only
        /// waits for pending serialization to drain before creating the store's read-only memory map.
        /// </remarks>
        private async Task CompleteStoreAsync()
        {
            await m_serializationCompletion.Value;
            m_store.Complete();
        }

        [SuppressMessage("Microsoft.Reliability", "CA2004:GCKeepAlive")]
        private void ProcessQueueItem(Pip pip, MutablePipState pipState)
        {
            ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
                    var start = Stopwatch.GetTimestamp();
                    PageableStoreId value = m_store.Write(writer => ((PipWriter)writer).Write(pip));
#if DEBUG
                    m_store.Read<Pip>(value, reader => ((PipReader)reader).ReadPip());
#endif
                    pipState.StoreId = value;
                    GC.KeepAlive(pip);
                    Interlocked.Add(ref m_writeTicks, Stopwatch.GetTimestamp() - start);
                    Interlocked.Increment(ref m_writes);
                },
                ex => ExceptionHandling.OnFatalException(ex));
        }
    }
}
