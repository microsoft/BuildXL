// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using BuildXL.Utilities.Collections;
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
        private readonly ConcurrentBigMap<AbsolutePath, bool> m_capturedPaths;
        private readonly ConcurrentBigMap<StringId, bool> m_capturedStrings;
        private readonly PipExecutionContext m_context;

        // Pending events queue.
        private readonly BlockingCollection<PooledObjectWrapper<EventWriter>> m_pendingEvents = new BlockingCollection<PooledObjectWrapper<EventWriter>>();
        private readonly Thread m_pendingEventsDrainingThread;

        // RW lock that allows for safe disposal of pending events queue.
        private readonly ReadWriteLock m_eventQueueLock = ReadWriteLock.Create();
        private bool m_eventQueueDisposed = false;

        /// <summary>
        /// The integral value of the last absolute path available in a statically loaded path table
        /// </summary>
        private readonly int m_lastStaticAbsolutePathIndex;

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
            m_capturedStrings = new ConcurrentBigMap<StringId, bool>();
            m_capturedStrings.Add(StringId.Invalid, true);
            m_writerPool = new ObjectPool<EventWriter>(
                () => new EventWriter(this),
                writer => { writer.Seek(0, SeekOrigin.Begin); return writer; });

            var logIdBytes = logId.ToByteArray();
            Contract.Assert(logIdBytes.Length == LogIdByteLength);
            logStream.Write(logIdBytes, 0, logIdBytes.Length);
            LogStartTime(DateTime.UtcNow);

            m_pendingEventsDrainingThread = new Thread(
                () =>
                {
                    foreach (PooledObjectWrapper<EventWriter> wrapper in m_pendingEvents.GetConsumingEnumerable())
                    {
                        var eventWriter = wrapper.Instance;
                        WriteEventData(eventWriter);
                        m_writerPool.PutInstance(wrapper);
                        onEventWritten?.Invoke();
                    }
                });
            m_pendingEventsDrainingThread.Start();
        }

        private void WriteEventData(EventWriter eventWriter)
        {
            m_logStreamWriter.WriteCompact(eventWriter.EventId);
            m_logStreamWriter.WriteCompact(eventWriter.WorkerId);
            m_logStreamWriter.WriteCompact(eventWriter.Timestamp);

            var eventPayloadStream = (MemoryStream)eventWriter.BaseStream;
            m_logStreamWriter.WriteCompact((int)eventPayloadStream.Position);
            m_logStreamWriter.Write(eventPayloadStream.GetBuffer(), 0, (int)eventPayloadStream.Position);
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

        private PooledObjectWrapper<EventWriter> GetEventWriterWrapper(uint eventId, uint workerId)
        {
            var writerWrapper = m_writerPool.GetInstance();
            writerWrapper.Instance.EventId = eventId;
            writerWrapper.Instance.WorkerId = workerId;
            writerWrapper.Instance.Timestamp = m_watch.Elapsed.Ticks;
            return writerWrapper;
        }

        private void ReturnEventWriteWrapper(PooledObjectWrapper<EventWriter> writerWrapper)
        {
            using (m_eventQueueLock.AcquireReadLock())
            {
                // Event queue is not disposed yet, so we can safely use it.
                if (!m_eventQueueDisposed)
                {
                    m_pendingEvents.Add(writerWrapper);
                }
                else
                {
                    // Event writer's stream is disposed at this point, so this event can't be logged.
                    // We can only return writer to its pool here.
                    writerWrapper.Dispose();
                }
            }
        }

        /// <summary>
        /// Logs the given time as the sync time for events logged by this logger. The timestamp for events
        /// will be relative to this time.
        /// </summary>
        /// <param name="startTime">the date</param>
        private void LogStartTime(DateTime startTime)
        {
            using (var eventScope = StartEvent(LogSupportEventId.StartTime))
            {
                eventScope.Writer.Write(startTime.ToFileTimeUtc());
            }
        }

        /// <summary>
        /// Disposes of the logger and closes the underlying stream
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public void Dispose()
        {
            // Finish processing events. Acquiring exlusive lock here so no one can add new logging events concurrently.
            using (m_eventQueueLock.AcquireWriteLock())
            {
                if (!m_eventQueueDisposed)
                {
                    m_pendingEvents.CompleteAdding();
                    m_pendingEventsDrainingThread.Join();
                    m_pendingEvents.Dispose();
                    m_eventQueueDisposed = true;
                }
            }

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
        public struct EventScope : IDisposable
        {
            private PooledObjectWrapper<EventWriter> m_writerWrapper;

            /// <summary>
            /// The writer for writing event data
            /// </summary>
            public EventWriter Writer => m_writerWrapper.Instance;

            /// <summary>
            /// Class constructor. INTERNAL USE ONLY.
            /// </summary>
            internal EventScope(PooledObjectWrapper<EventWriter> writerWrapper)
            {
                m_writerWrapper = writerWrapper;
            }

            /// <summary>
            /// Writes the event data
            /// </summary>
            public void Dispose()
            {
                Writer.Logger.ReturnEventWriteWrapper(m_writerWrapper);
            }
        }

        /// <summary>
        /// An extended BuildXL writer that can write primitive BuildXL values.
        /// </summary>
        public sealed class EventWriter : BuildXLWriter
        {
            internal uint EventId;
            internal long Timestamp;
            internal uint WorkerId;
            private readonly BinaryLogger m_logWriter;

            /// <summary>
            /// Returns the underlying log logger. Should be used only inside EventScope.Dispose()
            /// </summary>
            internal BinaryLogger Logger { get { return m_logWriter; } }

            internal PathTable PathTable => m_logWriter.m_context.PathTable;

            /// <summary>
            /// Class constructor
            /// </summary>
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
            internal EventWriter(BinaryLogger logWriter)
                : base(debug: false, stream: new MemoryStream(), leaveOpen: false, logStats: false)
            {
                m_logWriter = logWriter;
            }

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
            
        }
    }
}
