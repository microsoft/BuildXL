// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Diagnostics;

namespace BuildXL.Pips
{
    /// <summary>
    /// A table storing a set of pips
    /// </summary>
    /// <remarks>
    /// All instance members of this class are thread-safe.
    /// All pips get serialized into a compact store format.
    /// Only weak references are held on pip values.
    /// If a pip value is requested that has been garbage collected, it is re-created by deserialization.
    /// </remarks>
    public sealed class PipTable : IDisposable
    {
        /// <summary>
        /// Envelope for serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelope = new FileEnvelope(name: "PipTable", version: 0);

        private sealed class PageablePipStore : PageableStore
        {
            public PageablePipStore(PathTable pathTable, SymbolTable symbolTable, int initialBufferSize, bool debug)
                : base(pathTable, symbolTable, initialBufferSize, debug)
            {
            }

            protected override BuildXLWriter CreateWriter(Stream stream, bool leaveOpen)
            {
                Contract.Requires(CanWrite);
                return new PipWriter(Debug, stream, leaveOpen, logStats: true);
            }

            protected override BuildXLReader CreateReader(Stream stream, bool leaveOpen)
            {
                return new PipReader(Debug, StringTable, stream, leaveOpen);
            }

            public static async Task<PageablePipStore> DeserializeAsync(BuildXLReader reader, Task<PathTable> pathTableTask, Task<SymbolTable> symbolTableTask, int initialBufferSize)
            {
                Contract.Requires(reader != null);
                Contract.Requires(pathTableTask != null);
                Contract.Requires(symbolTableTask != null);

                SerializedState state = ReadSerializedState(reader);

                var pathTable = await pathTableTask;
                var symbolTable = await symbolTableTask;
                if (pathTable != null && symbolTable != null)
                {
                    return new PageablePipStore(pathTable, symbolTable, state, initialBufferSize);
                }

                return null;
            }

            private PageablePipStore(PathTable pathTable, SymbolTable symbolTable, PageableStore.SerializedState state, int initialBufferSize)
                : base(pathTable, symbolTable, state, initialBufferSize)
            {
            }
        }

        private readonly PageablePipStore m_store;
        private readonly ConcurrentDenseIndex<MutablePipState> m_mutables;
        private int m_lastId;
        private int m_count;
        private int m_writes;
        private readonly int[] m_deserializationContexts = new int[(int)PipQueryContext.End];
        private long m_readTicks;
        private long m_writeTicks;

        private readonly PipTableSerializationScheduler m_serializationScheduler;

        /// <summary>
        /// Creates a new pip table
        /// </summary>
        public PipTable(PathTable pathTable, SymbolTable symbolTable, int initialBufferSize, int maxDegreeOfParallelism, bool debug)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(symbolTable != null);
            Contract.Requires(initialBufferSize >= 0);
            Contract.Requires(maxDegreeOfParallelism >= -1);
            Contract.Requires(maxDegreeOfParallelism > 0 || debug);

            m_store = new PageablePipStore(pathTable, symbolTable, initialBufferSize, debug);
            m_mutables = new ConcurrentDenseIndex<MutablePipState>(debug);
            m_serializationScheduler = new PipTableSerializationScheduler(maxDegreeOfParallelism, debug, ProcessQueueItem);
        }

        /// <summary>
        /// Whether this instance got disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Constructor used by deserialization
        /// </summary>
        private PipTable(PageablePipStore store, ConcurrentDenseIndex<MutablePipState> mutables, int pipCount, int maxDegreeOfParallelism)
        {
            Contract.Requires(store != null);
            Contract.Requires(mutables != null);

            m_lastId = pipCount;
            m_count = pipCount;
            m_store = store;
            m_mutables = mutables;

            m_serializationScheduler = new PipTableSerializationScheduler(maxDegreeOfParallelism, debug: false, serializer: ProcessQueueItem);
            m_serializationScheduler.Complete(); // Don't allow more changes
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
                    GC.KeepAlive(pip); // Pip must not get GCed until after StoreId has been set.
                    var end = Stopwatch.GetTimestamp();
                    Interlocked.Add(ref m_writeTicks, end - start);
                    Interlocked.Increment(ref m_writes);
                },
                ex => ExceptionHandling.OnFatalException(ex));
        }

        /// <summary>
        /// How many times persisted pips had to be reconstructed
        /// </summary>
        public long Reads
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_deserializationContexts.Select(i => (long)i).Sum();
            }
        }

        /// <summary>
        /// How many pips got persisted
        /// </summary>
        public int Writes
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return Volatile.Read(ref m_writes);
            }
        }

        /// <summary>
        /// List of all contexts that caused Pip deserialization, and their multiplicities
        /// </summary>
        public IEnumerable<KeyValuePair<PipQueryContext, int>> DeserializationContexts
        {
            get
            {
                Contract.Requires(!IsDisposed);
                List<KeyValuePair<PipQueryContext, int>> result = new List<KeyValuePair<PipQueryContext, int>>();
                for (PipQueryContext i = 0; i < PipQueryContext.End; i++)
                {
                    var count = m_deserializationContexts[(int)i];
                    if (count > 0)
                    {
                        result.Add(new KeyValuePair<PipQueryContext, int>(i, count));
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// How many pips got persisted
        /// </summary>
        public int Count
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return Volatile.Read(ref m_count);
            }
        }

        /// <summary>
        /// The purpose of this class is to facilitate efficient enumeration in <code>Parallel.ForEach</code>.
        /// </summary>
        private sealed class KeyList : IList<PipId>
        {
            private readonly int m_lastId;

            public KeyList(int lastId)
            {
                m_lastId = lastId;
            }

            public int IndexOf(PipId item) => (int)item.Value - 1;

            public void Insert(int index, PipId item) => throw new NotImplementedException();

            public void RemoveAt(int index) => throw new NotImplementedException();

            public PipId this[int index]
            {
                get => new PipId((uint)index + 1);
                set => throw new NotImplementedException();
            }

            public void Add(PipId item) => throw new NotImplementedException();

            public void Clear() => throw new NotImplementedException();

            public bool Contains(PipId item) => item.Value > 0 && item.Value <= (uint)m_lastId;

            public void CopyTo(PipId[] array, int arrayIndex)
            {
                for (int i = 1; i <= m_lastId; i++)
                {
                    array[i + arrayIndex - 1] = new PipId((uint)i);
                }
            }

            public int Count => m_lastId;

            public bool IsReadOnly => true;

            public bool Remove(PipId item) => throw new NotImplementedException();

            public IEnumerator<PipId> GetEnumerator()
            {
                for (uint i = 1; i <= m_lastId; i++)
                {
                    yield return new PipId(i);
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>
        /// All <code>PipId</code> values issued so far.
        /// </summary>
        /// <remarks>
        /// This property may only be invoked after all pips have been added. During pip table construction, the produced enumerable is slightly inefficient as it has to deal with temporary holes in the table.
        /// Use <see cref="Keys"/> during construction.
        /// </remarks>
        public IList<PipId> StableKeys
        {
            get
            {
                Contract.Requires(!IsDisposed);
                var count = Volatile.Read(ref m_count);
                var max = Volatile.Read(ref m_lastId);
                Contract.Assume(count == max);
                return new KeyList(max);
            }
        }

        /// <summary>
        /// All <code>PipId</code> values issued so far.
        /// </summary>
        /// <remarks>
        /// During pip table construction, the produced enumerable is slightly inefficient as it has to deal with temporary holes in the table.
        /// After all entries have been added with consecutive indices, the produced enumerable is in fact a list that can be used in efficient Parallel.ForEach enumerations.
        /// </remarks>
        public IEnumerable<PipId> Keys
        {
            get
            {
                Contract.Requires(!IsDisposed);
                var count = Volatile.Read(ref m_count);
                var max = Volatile.Read(ref m_lastId);
                if (count == max)
                {
                    // There are no holes (which could happen temporarily during concurrent pip additions),
                    // so we can return a nice list
                    return new KeyList(max);
                }

                return GetValidKeys(max);
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

        /// <summary>
        /// How many page streams were used to persist pips
        /// </summary>
        public int PageStreamsCount
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_store.PageStreamsCount;
            }
        }

        /// <summary>
        /// How many bytes do the buffers of the streams occupy in memory
        /// </summary>
        public long Size
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_store.MemorySize;
            }
        }

        /// <summary>
        /// How many milliseconds are spent writing (serializing) pips
        /// </summary>
        public long WritesMilliseconds => Volatile.Read(ref m_writeTicks) * 1000 / Stopwatch.Frequency;

        /// <summary>
        /// How many milliseconds are spent reading (deserializing) pips
        /// </summary>
        public long ReadsMilliseconds => Volatile.Read(ref m_readTicks) * 1000 / Stopwatch.Frequency;

        /// <summary>
        /// How many bytes of the stream buffers are actually used
        /// </summary>
        public long Used
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_store.MemoryUsed;
            }
        }

        /// <summary>
        /// How many pips are alive at this time
        /// </summary>
        public int Alive
        {
            get
            {
                Contract.Requires(!IsDisposed);
                int alive = 0;
                var max = (uint)m_lastId;
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
        /// Adds a pip to the table.
        /// </summary>
        public PipId Add(uint nodeIdValue, Pip pip)
        {
            Contract.Requires(!IsDisposed);
            Contract.Requires(pip != null);
            Contract.Requires(nodeIdValue > 0);
            Contract.Requires(!pip.PipId.IsValid);
            Contract.Ensures(Contract.Result<PipId>().IsValid);

            var pipId = new PipId(nodeIdValue);
            pip.PipId = pipId;
            var mutable = MutablePipState.Create(pip);
            Contract.Assume(m_mutables[nodeIdValue] == null);
            m_mutables[nodeIdValue] = mutable;
            Interlocked.Increment(ref m_count);

            // Fix the last id
            while (true)
            {
                int oldMaxPipValue = Volatile.Read(ref m_lastId);
                int newMaxPipValue = Math.Max(oldMaxPipValue, (int)nodeIdValue);
                if (oldMaxPipValue == newMaxPipValue || Interlocked.CompareExchange(ref m_lastId, newMaxPipValue, oldMaxPipValue) == oldMaxPipValue)
                {
                    break;
                }
            }

            m_serializationScheduler.ScheduleSerialization(pip, mutable);

            return pipId;
        }

        /// <summary>
        /// Whether a Pip id is valid in this table.
        /// </summary>
        [Pure]
        public bool IsValid(PipId pipId)
        {
            return (pipId.Value > 0 && pipId.Value <= m_lastId && m_mutables[pipId.Value] != null) || pipId == PipId.DummyHashSourceFilePipId;
        }

        /// <summary>
        /// Gets mutable state associated with a particular pip
        /// </summary>
        internal MutablePipState GetMutable(PipId pipId)
        {
            Contract.Requires(IsValid(pipId));
            Contract.Ensures(Contract.Result<MutablePipState>() != null);

            MutablePipState mutable = m_mutables[pipId.Value];
            Contract.Assume(mutable != null);
            return mutable;
        }

        /// <summary>
        /// Get a pip type without the need to hydrate the pip
        /// </summary>
        public PipType GetPipType(PipId pipId)
        {
            Contract.Requires(!IsDisposed);
            Contract.Requires(IsValid(pipId));

            if (pipId == PipId.DummyHashSourceFilePipId)
            {
                return PipType.HashSourceFile;
            }

            return m_mutables[pipId.Value].PipType;
        }

        /// <summary>
        /// Get pip process options without the need to hydrate the pip
        /// </summary>
        /// <remarks>
        /// <paramref name="pipId"/> must refer to <see cref="Operations.Process"/>. Use <see cref="GetPipType(PipId)"/> to check.
        /// </remarks>
        public Operations.Process.Options GetProcessOptions(PipId pipId)
        {
            Contract.Requires(!IsDisposed);
            Contract.Requires(IsValid(pipId));

            var processMutableState = m_mutables[pipId.Value] as ProcessMutablePipState;
            Contract.Assert(processMutableState != null);

            return processMutableState.ProcessOptions;
        }

        /// <summary>
        /// Get seal directory kind without the need to hydrate the pip
        /// </summary>
        public SealDirectoryKind GetSealDirectoryKind(PipId pipId)
        {
            Contract.Requires(!IsDisposed);
            Contract.Requires(IsValid(pipId));
            var mutable = m_mutables[pipId.Value];
            if (mutable.PipType == PipType.SealDirectory)
            {
                return ((SealDirectoryMutablePipState)mutable).SealDirectoryKind;
            }

            return default(SealDirectoryKind);
        }

        /// <summary>
        /// Should the seal directory be scrubbed before seal. 
        /// </summary>
        public bool ShouldScrubFullSealDirectory(PipId pipId)
        {
            Contract.Requires(!IsDisposed);
            Contract.Requires(IsValid(pipId));
            var mutable = m_mutables[pipId.Value];
            if (mutable.PipType == PipType.SealDirectory)
            {
                return ((SealDirectoryMutablePipState)mutable).Scrub;
            }

            return false;
        }


        /// <summary>
        /// Get whether the seal directory is a composite one without the need to hydrate the pip
        /// </summary>
        public bool IsSealDirectoryComposite(PipId pipId)
        {
            Contract.Requires(!IsDisposed);
            Contract.Requires(IsValid(pipId));
            var mutable = m_mutables[pipId.Value];
            if (mutable.PipType == PipType.SealDirectory)
            {
                return ((SealDirectoryMutablePipState)mutable).IsComposite;
            }

            return false;
        }

        /// <summary>
        /// Get seal directory kind without the need to hydrate the pip
        /// </summary>
        public ReadOnlyArray<StringId> GetSourceSealDirectoryPatterns(PipId pipId)
        {
            Contract.Requires(!IsDisposed);
            Contract.Requires(IsValid(pipId));
            var mutable = m_mutables[pipId.Value];
            if (mutable.PipType == PipType.SealDirectory)
            {
                var sealMutable = ((SealDirectoryMutablePipState)mutable);
                Contract.Assert(sealMutable.SealDirectoryKind.IsSourceSeal(), "Pattern is only avaialable for source seal directories.");
                return sealMutable.Patterns;
            }

            return ReadOnlyArray<StringId>.Empty;
        }

        /// <summary>
        /// Get a pip semi stable hash without the need to hydrate the pip
        /// </summary>
        public long GetPipSemiStableHash(PipId pipId)
        {
            Contract.Requires(!IsDisposed);
            Contract.Requires(IsValid(pipId));
            return m_mutables[pipId.Value].SemiStableHash;
        }

        /// <summary>
        /// Get a scheduling priority for a pip
        /// </summary>
        public int GetPipPriority(PipId pipId)
        {
            Contract.Requires(!IsDisposed);
            Contract.Requires(IsValid(pipId));
            var mutablePipState = m_mutables[pipId.Value];
            return mutablePipState.PipType == PipType.Process ? ((ProcessMutablePipState)mutablePipState).Priority : 0;
        }

        /// <summary>
        /// Get a formatted pip semi stable hash without the need to hydrate the pip
        /// </summary>
        public string GetFormattedSemiStableHash(PipId pipId)
        {
            return Pip.FormatSemiStableHash(GetPipSemiStableHash(pipId));
        }

        /// <summary>
        /// Re-creates a pip that was added earlier.
        /// </summary>
        /// <remarks>
        /// Calling is function is potentially quite expensive, and should be avoided when possible.
        /// This obviously returns a strong reference to a Pip. As long as someone holds on to that strong reference, the Pip will
        /// stay in memory.
        /// As soon as the last reference gets released, the Pip may get garbage collected, which is a good thing (and the Pip
        /// might then get recreated later by deserialization).
        /// </remarks>
        public Pip HydratePip(PipId pipId, PipQueryContext context)
        {
            Contract.Requires(!IsDisposed);
            Contract.Requires(IsValid(pipId));
            Contract.Ensures(Contract.Result<Pip>() != null);

            return GetMutable(pipId)
                .InternalGetOrSetPip(
                    this,
                    pipId,
                    context,
                    (table, pipId2, storeId, context2) =>
                    {
                        Interlocked.Increment(ref table.m_deserializationContexts[(int)context2]);

                        return ExceptionUtilities.HandleRecoverableIOException(
                            (table, table, storeId, pipId2),
                            tuple =>
                            {
                                var table1 = tuple.Item1;
                                var @this = tuple.Item2;
                                var storeId1 = tuple.Item3;
                                var pipId3 = tuple.Item4;

                                var start = Stopwatch.GetTimestamp();
                                var pip = table1.m_store.Read<Pip>(storeId1, reader => ((PipReader)reader).ReadPip());
                                pip.PipId = pipId3;
                                var end = Stopwatch.GetTimestamp();
                                Interlocked.Add(ref @this.m_readTicks, end - start);
                                return pip;
                            },
                            (tuple, ex) => ExceptionHandling.OnFatalException(ex));
                    });
        }

        /// <summary>
        /// Stops any background serialization of values. This method is idempotent and thread-safe.
        /// </summary>
        public void StopBackgroundSerialization()
        {
            m_serializationScheduler.Complete();
        }

        /// <summary>
        /// Task that completes when no more serialization tasks are running in the background.
        /// </summary>
        public Task WhenDone()
        {
            Contract.Requires(!IsDisposed);

            return m_serializationScheduler.WhenDone();
        }

        /// <summary>
        /// Disposes this instance; only call <code>WhenDone</code>.
        /// </summary>
        public void Dispose()
        {
            StopBackgroundSerialization();
            IsDisposed = true;
        }

        #region Serialization

        /// <summary>
        /// Deserializes
        /// </summary>
        public static async Task<PipTable> DeserializeAsync(BuildXLReader reader, Task<PathTable> pathTableTask, Task<SymbolTable> symbolTableTask, int initialBufferSize, int maxDegreeOfParallelism, bool debug)
        {
            Contract.Requires(reader != null);
            Contract.Requires(pathTableTask != null);
            Contract.Requires(symbolTableTask != null);
            Contract.Requires(maxDegreeOfParallelism >= -1);

            PageablePipStore store = await PageablePipStore.DeserializeAsync(reader, pathTableTask, symbolTableTask, initialBufferSize);
            if (store != null)
            {
                var mutables = new ConcurrentDenseIndex<MutablePipState>(debug);
                int pipCount = reader.ReadInt32();
                for (uint i = 0; i < pipCount; i++)
                {
                    mutables[i + 1] = MutablePipState.Deserialize(reader);
                }

                return new PipTable(store, mutables, pipCount, maxDegreeOfParallelism);
            }

            return null;
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer, int maxDegreeOfParallelism)
        {
            Contract.Requires(!IsDisposed);
            Contract.Requires(writer != null);
            Contract.Assume(m_lastId == m_count);

            m_serializationScheduler.IncreaseConcurrencyTo(maxDegreeOfParallelism);

            // Wait for all serialization tasks to finish.
            WhenDone().GetAwaiter().GetResult();

            m_store.Serialize(writer);

            writer.Write(m_lastId);
            for (uint i = 0; i < m_lastId; i++)
            {
                var item = m_mutables[i + 1];
                Contract.Assume(item != null);
                item.Serialize(writer);
            }
        }

        #endregion
    }
}
