// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Threading;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Defines functionality for logging a series of compact binary log events
    ///
    /// Event format is:
    ///  Event ID (compact uint32)
    ///  Timestamp ticks since initialization of logger (compact int64)
    ///  Event payload length in bytes (compact int32)
    ///  Event payload
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
    public sealed class BinaryLogger : IDisposable
    {
        /// <summary>
        /// The length of the log id in the header
        /// </summary>
        public const int LogIdByteLength = 16;

        /// <summary>
        /// The log id
        /// </summary>
        public readonly Guid LogId;

        private readonly BuildXLWriter m_logStreamWriter;
        private readonly Stopwatch m_watch;
        private readonly ObjectPool<EventWriter> m_writerPool;

        // These maps intern frequently-repeated values (paths, strings, content hashes) to compact
        // integer indices in the event stream. The bool value tracks whether the corresponding
        // side-channel registration event (AddPath, AddStringId, AddContentHash) has been written
        // (true) or is still pending (false). Multiple threads may race to add the same key; each
        // thread that sees the bool as false writes a (possibly duplicate but harmless) registration
        // event, ensuring the reader always processes the definition before any reference regardless
        // of event queue ordering.
        private readonly ConcurrentBigMap<AbsolutePath, bool> m_capturedPaths;
        private readonly ConcurrentBigMap<StringId, bool> m_capturedStrings;
        private readonly ConcurrentBigMap<ContentHashKey, bool> m_capturedContentHashes;
        private readonly PipExecutionContext m_context;
        private readonly Action m_onEventWritten;

        private readonly ActionBlockSlim<IQueuedAction> m_logWriterBlock;

        /// <summary>
        /// The integral value of the last absolute path available in a statically loaded path table
        /// </summary>
        private readonly int m_lastStaticAbsolutePathIndex;

        private long m_pendingEventsCount;
        private long m_maxPendingEventsCount;
        private long m_contentHashEntriesWritten;
        private long m_contentHashOverflowCount;

        /// <summary>
        /// Max pending events in the binary logger.
        /// </summary>
        public long MaxPendingEventsCount => Volatile.Read(ref m_maxPendingEventsCount);

        /// <summary>
        /// Current pending events in the binary logger.
        /// </summary>
        public long PendingEventCount => Volatile.Read(ref m_pendingEventsCount);

        /// <summary>
        /// Number of event writer creations.
        /// </summary>
        public long EventWriterFactoryCalls => m_writerPool.FactoryCalls;

        /// <summary>
        /// Number of unique content hashes deduplicated in the log.
        /// </summary>
        public long UniqueContentHashCount => m_capturedContentHashes.Count;

        /// <summary>
        /// Total number of content hash references written (including duplicates).
        /// </summary>
        public long ContentHashEntriesWritten => Volatile.Read(ref m_contentHashEntriesWritten);

        /// <summary>
        /// Number of content hash writes that bypassed interning because the table was full.
        /// </summary>
        public long ContentHashOverflowCount => Volatile.Read(ref m_contentHashOverflowCount);

        /// <summary>
        /// When true, WriteContentHash writes raw bytes instead of interning.
        /// Used for benchmarking to compare serialization sizes.
        /// </summary>
        public bool SuppressContentHashInterning { get; set; }

        /// <summary>
        /// Creates a new binary logger to write to the given stream
        /// </summary>
        /// <param name="logStream">the stream to write events to</param>
        /// <param name="context">the context containing the path table</param>
        /// <param name="logId">the log id to place in the header of execution used to verify with other data structures on load.</param>
        /// <param name="lastStaticAbsolutePathIndex">the last absolute path guaranteed to be in the serialized form of the corresponding path table</param>
        /// <param name="closeStreamOnDispose">specifies whether the stream is closed on disposal of the logger</param>
        /// <param name="onEventWritten">optional callback after each event is written to underlying stream</param>
        public BinaryLogger(Stream logStream, PipExecutionContext context, Guid logId, int lastStaticAbsolutePathIndex = int.MinValue, bool closeStreamOnDispose = true, Action onEventWritten = null)
        {
            m_context = context;
            LogId = logId;
            m_lastStaticAbsolutePathIndex = lastStaticAbsolutePathIndex;
            m_logStreamWriter = new BuildXLWriter(debug: false, stream: logStream, leaveOpen: !closeStreamOnDispose, logStats: false);
            m_watch = Stopwatch.StartNew();
            m_capturedPaths = new ConcurrentBigMap<AbsolutePath, bool>();
            m_capturedStrings = new ConcurrentBigMap<StringId, bool>
            {
                { StringId.Invalid, true }
            };
            m_capturedContentHashes = new ConcurrentBigMap<ContentHashKey, bool>();
            m_writerPool = new ObjectPool<EventWriter>(() => new EventWriter(this), writer => { writer.Clear(); return writer; });
            m_onEventWritten = onEventWritten;
            m_logWriterBlock = ActionBlockSlim.Create<IQueuedAction>(
                1, // Only one writer thread because we need to serialize it to a file stream.
                action =>
                {
                    long pendingCount = Interlocked.Decrement(ref m_pendingEventsCount) + 1;
                    m_maxPendingEventsCount = Math.Max(m_maxPendingEventsCount, pendingCount);
                    action.Run();
                });

            var logIdBytes = logId.ToByteArray();
            Contract.Assert(logIdBytes.Length == LogIdByteLength);
            logStream.Write(logIdBytes, 0, logIdBytes.Length);
            LogStartTime(DateTime.UtcNow);
        }

        private void WriteEventDataAndReturnWriter(EventWriter eventWriter)
        {
            if (eventWriter.Exception == null)
            {
                m_logStreamWriter.WriteCompact(eventWriter.EventId);
                m_logStreamWriter.WriteCompact(eventWriter.WorkerId);
                m_logStreamWriter.WriteCompact(eventWriter.Timestamp);

                var eventPayloadStream = (MemoryStream)eventWriter.BaseStream;
                m_logStreamWriter.WriteCompact((int)eventPayloadStream.Position);
                m_logStreamWriter.Write(eventPayloadStream.GetBuffer(), 0, (int)eventPayloadStream.Position);

                m_onEventWritten?.Invoke();
            }

            if (eventWriter.Capacity < 5_000_000)
            {
                // Avoid returning a writer that has a large memory stream to the pool.
                m_writerPool.PutInstance(eventWriter);
            }
        }

        /// <summary>
        /// Queue a flush underlying stream action, it will be executed when all pending events are processed.
        /// </summary>
        public Task FlushAsync()
        {
            // There are event still being processed. Ensure they are all sent 
            // before flushing the underlying stream, by adding a flush event
            // which will be trigger when current pending events are processed
            // and the underlying stream has been flushed.
            var flushAction = new FlushAction(this);
            QueueAction(flushAction);
            return flushAction.Completion;
        }

        /// <summary>
        /// Starts a scope for writing an event with the given id
        /// </summary>
        /// <param name="eventId">the event id</param>
        /// <param name="workerId">the worker id</param>
        /// <returns>scope containing writer for event payload</returns>
        public EventScope StartEvent(uint eventId, uint workerId)
        {
            return new EventScope(GetEventWriterWrapper(checked(eventId + (uint)LogSupportEventId.Max), workerId: workerId));
        }

        private EventScope StartEvent(LogSupportEventId eventId)
        {
            return new EventScope(GetEventWriterWrapper((uint)eventId, workerId: 0));
        }

        private EventWriter GetEventWriterWrapper(uint eventId, uint workerId)
        {
            var writer = m_writerPool.GetInstance().Instance;
            writer.EventId = eventId;
            writer.WorkerId = workerId;
            writer.Timestamp = m_watch.Elapsed.Ticks;
            return writer;
        }

        private void QueueAction(IQueuedAction action)
        {
            bool posted = m_logWriterBlock.TryPost(action);
            if (!posted)
            {
                // There could be a race condition that cause this method to be called when the writer block is finished.
                // In this case, the action is simply disposed.
                action.Dispose();
            }
            else
            {
                Interlocked.Increment(ref m_pendingEventsCount);
            }
        }

        /// <summary>
        /// Logs the given time as the sync time for events logged by this logger. The timestamp for events
        /// will be relative to this time.
        /// </summary>
        /// <param name="startTime">the date</param>
        private void LogStartTime(DateTime startTime)
        {
            using var eventScope = StartEvent(LogSupportEventId.StartTime);
            eventScope.Writer.Write(startTime.ToFileTimeUtc());
        }

        /// <summary>
        /// Disposes of the logger and closes the underlying stream
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public void Dispose()
        {
            m_logWriterBlock.Complete();
            m_logWriterBlock.Completion.Wait();

            // Close ther underlying writer.
            m_logStreamWriter.Dispose();
        }

        /// <summary>
        /// Describes events ids for internal log writer events
        /// </summary>
        internal enum LogSupportEventId
        {
            /// <summary>
            /// Logs the start time for events in this logger
            /// </summary>
            StartTime = 0,

            /// <summary>
            /// Adds a dynamic path id
            /// </summary>
            AddPath = 1,

            /// <summary>
            /// Adds a dynamic StringId
            /// </summary>
            AddStringId = 2,

            /// <summary>
            /// Adds a deduplicated content hash
            /// </summary>
            AddContentHash = 3,

            // First 20 event Ids are reserved for use as internal events
            Max = 20,
        }

        /// <summary>
        /// The type of absolute path that we are trying to read/write
        /// </summary>
        internal enum AbsolutePathType : byte
        {
            /// <summary>
            /// AbsolutePath.Invalid
            /// </summary>
            Invalid = 0,

            /// <summary>
            /// The path is known at parse time
            /// </summary>
            Static = 1,

            /// <summary>
            /// The path is only known at run time
            /// </summary>
            Dynamic = 2,
        }

        /// <summary>
        /// Scope for writing data for an event
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public readonly struct EventScope : IDisposable
        {
            /// <summary>
            /// The writer for writing event data
            /// </summary>
            public EventWriter Writer { get; }

            /// <summary>
            /// Class constructor. INTERNAL USE ONLY.
            /// </summary>
            internal EventScope(EventWriter writer) => Writer = writer;

            /// <summary>
            /// Sets the exception that happened during writing event data with this scope.
            /// </summary>
            /// <remarks>
            /// When the exception is set, the event is not logged.
            /// </remarks>
            public void SetException(Exception exception) => Writer.Exception = exception;

            /// <summary>
            /// Writes the event data
            /// </summary>
            public void Dispose()
            {
                Writer.Logger.QueueAction(Writer);
            }
        }

        private interface IQueuedAction : IDisposable
        {
            void Run();
        }

        private class FlushAction : IQueuedAction
        {
            private readonly TaskSourceSlim<Unit> m_taskSource = TaskSourceSlim.Create<Unit>();
            private readonly BinaryLogger m_binaryLogger;

            public FlushAction(BinaryLogger binaryLogger) => m_binaryLogger = binaryLogger;

            public Task Completion => m_taskSource.Task;

            public void Run()
            {
                // Flush the base stream as a part of running this action
                m_binaryLogger.m_logStreamWriter.BaseStream.Flush();

                // Then signal completion
                m_taskSource.TrySetResult(Unit.Void);
            }

            public void Dispose()
            {
                // Also complete the action on Dispose()
                Run();
            }
        }

        /// <summary>
        /// An extended BuildXL writer that can write primitive BuildXL values.
        /// </summary>
        public sealed class EventWriter : BuildXLWriter, IQueuedAction
        {
            internal uint EventId;
            internal long Timestamp;
            internal uint WorkerId;
            private readonly BinaryLogger m_logWriter;

            /// <summary>
            /// Returns the underlying log logger. Should be used only inside EventScope.Dispose()
            /// </summary>
            internal BinaryLogger Logger => m_logWriter;

            internal PathTable PathTable => m_logWriter.m_context.PathTable;

            internal Exception Exception { get; set; }

            internal int Capacity => (OutStream as MemoryStream).Capacity;

            /// <summary>
            /// Class constructor
            /// </summary>
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
            internal EventWriter(BinaryLogger logWriter)
                : base(debug: false, stream: new MemoryStream(), leaveOpen: false, logStats: false) => m_logWriter = logWriter;

            /// <summary>
            /// Writes an absolute path.
            /// </summary>
            /// <remarks>
            /// The first time a path is written, its string form is used. From then on, it is memoized
            /// and an integer index is used to refer to the path
            /// </remarks>
            public override void Write(AbsolutePath value)
            {
                if (!value.IsValid)
                {
                    Write((byte)AbsolutePathType.Invalid);
                }
                else if (value.Value.Index <= m_logWriter.m_lastStaticAbsolutePathIndex)
                {
                    Write((byte)AbsolutePathType.Static);
                    base.Write(value);
                }
                else
                {
                    Write((byte)AbsolutePathType.Dynamic);
                    var result = m_logWriter.m_capturedPaths.GetOrAdd(value, false);
                    if (!result.Item.Value)
                    {
                        using (var eventScope = m_logWriter.StartEvent(LogSupportEventId.AddPath))
                        {
                            AbsolutePath parent = value.GetParent(m_logWriter.m_context.PathTable);
                            eventScope.Writer.Write(parent);
                            eventScope.Writer.WriteCompact(result.Index);
                            eventScope.Writer.Write(parent.IsValid
                                ? value.GetName(m_logWriter.m_context.PathTable).ToString(m_logWriter.m_context.StringTable)
                                : value.ToString(m_logWriter.m_context.PathTable));
                        }

                        m_logWriter.m_capturedPaths[value] = true;
                    }

                    WriteCompact(result.Index);
                }
            }

            /// <summary>
            /// Writes a StringId as a string rather than int
            /// </summary>
            public void WriteDynamicStringId(StringId value)
            {
                var result = m_logWriter.m_capturedStrings.GetOrAdd(value, false);
                if (!result.Item.Value)
                {
                    using (var eventScope = m_logWriter.StartEvent(LogSupportEventId.AddStringId))
                    {
                        eventScope.Writer.WriteCompact(result.Index);
                        eventScope.Writer.Write(value.ToString(m_logWriter.m_context.StringTable));
                    }

                    m_logWriter.m_capturedStrings[value] = true;
                }

                WriteCompact(result.Index);
            }

            /// <summary>
            /// Whether content hash interning is suppressed (for benchmarking).
            /// </summary>
            public bool SuppressContentHashInterning => m_logWriter.SuppressContentHashInterning;

            /// <summary>
            /// Current number of unique content hashes in the intern table.
            /// </summary>
            public int ContentHashInternCount => m_logWriter.m_capturedContentHashes.Count;

            /// <summary>
            /// Looks up or adds a content hash key in the intern table.
            /// Returns the result including the boolean flag indicating whether the
            /// AddContentHash side-channel event has been written for this entry.
            /// </summary>
            internal ConcurrentBigSet<KeyValuePair<ContentHashKey, bool>>.GetAddOrUpdateResult GetOrAddContentHash(ContentHashKey key)
            {
                return m_logWriter.m_capturedContentHashes.GetOrAdd(key, false);
            }

            /// <summary>
            /// Looks up a content hash key in the intern table without adding.
            /// Used when the table is over capacity to check for previously interned hashes.
            /// </summary>
            internal ConcurrentBigSet<KeyValuePair<ContentHashKey, bool>>.GetAddOrUpdateResult TryGetContentHash(ContentHashKey key)
            {
                return m_logWriter.m_capturedContentHashes.TryGet(key);
            }

            /// <summary>
            /// Writes an AddContentHash side-channel event that registers hash bytes at the given
            /// index, then marks the entry as written so other threads skip duplicate registration.
            /// </summary>
            internal void WriteAddContentHashEvent(ContentHashKey key, int index, byte[] hashBytes, int offset, int length)
            {
                using (var eventScope = m_logWriter.StartEvent(LogSupportEventId.AddContentHash))
                {
                    eventScope.Writer.WriteCompact(index);
                    eventScope.Writer.WriteCompact(length);
                    eventScope.Writer.Write(hashBytes, offset, length);
                }

                m_logWriter.m_capturedContentHashes[key] = true;
            }

            /// <summary>
            /// Increments the interned content hash reference counter.
            /// </summary>
            public void IncrementContentHashEntries()
            {
                Interlocked.Increment(ref m_logWriter.m_contentHashEntriesWritten);
            }

            /// <summary>
            /// Increments the overflow (inline) content hash write counter.
            /// </summary>
            public void IncrementContentHashOverflow()
            {
                Interlocked.Increment(ref m_logWriter.m_contentHashOverflowCount);
            }

            void IQueuedAction.Run() => m_logWriter.WriteEventDataAndReturnWriter(this);

            /// <summary>
            /// Clears this instance of <see cref="EventWriter"/>.
            /// </summary>
            internal void Clear()
            {
                Seek(0, SeekOrigin.Begin);
                Exception = null;
            }
        }

        /// <summary>
        /// Lightweight struct used as a dictionary key for content hash deduplication.
        /// Stores raw hash bytes (up to 33 bytes) without depending on the ContentHash type.
        /// </summary>
        /// <remarks>
        /// ContentHash (from BuildXL.Cache.ContentStore.Hashing) cannot be used directly here
        /// because BuildXL.Utilities does not depend on that assembly. Adding that dependency
        /// would violate the existing module layering. A raw byte[] also cannot serve as a
        /// dictionary key because arrays use reference equality. This struct provides value-based
        /// equality over the hash bytes by packing them into fixed-size primitive fields.
        ///
        /// The struct stores the first 32 bytes of the hash (4 × 8-byte longs). This is sufficient
        /// because all hashes in a single build use the same algorithm, so the hash type byte
        /// (byte 33 for VSO0) does not need to be included for uniqueness. Hashes shorter than
        /// 32 bytes are zero-padded in the upper fields.
        /// </remarks>
        internal readonly struct ContentHashKey : IEquatable<ContentHashKey>
        {
            /// <summary>
            /// Maximum number of hash bytes this struct can represent.
            /// </summary>
            public const int MaxSupportedLength = 33;

            private readonly long _a, _b, _c, _d;

            public ContentHashKey(byte[] data, int offset, int length)
            {
                Contract.Requires(data != null);
                Contract.Requires(offset >= 0);
                Contract.Requires(length > 0, "Content hash must have at least 1 byte");
                Contract.Requires(length <= MaxSupportedLength, $"Content hash length {length} exceeds maximum supported length of {MaxSupportedLength} bytes");
                Contract.Requires(offset + length <= data.Length);

                // Fast path for hashes that fill complete 8-byte longs (SHA256 = 32 bytes, VSO0 = 33 bytes)
                if (length >= 32)
                {
                    var span = data.AsSpan(offset);
                    _a = BinaryPrimitives.ReadInt64LittleEndian(span);
                    _b = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(8));
                    _c = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(16));
                    _d = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(24));
                }
                else
                {
                    // Slower path for shorter hashes (e.g. SHA1 = 20 bytes, MD5 = 16 bytes)
                    _a = ReadPartialLong(data, offset, length, 0);
                    _b = ReadPartialLong(data, offset, length, 8);
                    _c = ReadPartialLong(data, offset, length, 16);
                    _d = ReadPartialLong(data, offset, length, 24);
                }
            }

            private static long ReadPartialLong(byte[] data, int offset, int totalLength, int longOffset)
            {
                if (longOffset >= totalLength)
                {
                    return 0;
                }

                int remaining = totalLength - longOffset;

                // If at least 8 bytes remain, read a full long in one shot
                if (remaining >= 8)
                {
                    return BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset + longOffset));
                }

                // Otherwise, assemble the partial long byte-by-byte
                long result = 0;
                for (int i = 0; i < remaining; i++)
                {
                    result |= (long)data[offset + longOffset + i] << (i * 8);
                }

                return result;
            }

            /// <inheritdoc />
            public bool Equals(ContentHashKey other) =>
                _a == other._a && _b == other._b && _c == other._c && _d == other._d;

            /// <inheritdoc />
            public override bool Equals(object obj) => obj is ContentHashKey k && Equals(k);

            /// <inheritdoc />
            /// <remarks>
            /// Only _a and _b (first 16 bytes) are used for the hash code. This is sufficient because
            /// content hashes are cryptographic and have excellent entropy in every byte.
            /// </remarks>
            public override int GetHashCode()
            {
                unchecked
                {
                    return (int)(_a ^ (_a >> 32)) * 397 ^ (int)(_b ^ (_b >> 32));
                }
            }
        }
    }
}
