// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Reader for reading events from a binary log.
    ///
    /// Handlers are registered to deserialize and handle payload from specific event ids.
    /// </summary>
    public class BinaryLogReader : IDisposable
    {
        /// <summary>
        /// Handles the deserialization and interpretation of an event. This should not read beyond
        /// the size of the event payload
        /// </summary>
        /// <param name="eventId">the event id</param>
        /// <param name="workerId">the worker id</param>
        /// <param name="timestamp">the timestamp for the event</param>
        /// <param name="reader">the reader for reading data from the event payload</param>
        public delegate void EventHandler(uint eventId, uint workerId, long timestamp, EventReader reader);

        private readonly PipExecutionContext m_context;
        private readonly bool m_closeStreamOnDispose;
        private long? m_logStreamLength;
        private bool? m_streamHasFixedSize;

        /// <summary>
        /// The log stream being read
        /// </summary>
        protected Stream LogStream { get; }

        /// <summary>
        /// If the stream is a FileStream, the size can be cached, which makes a large impact on read time.  Distributed builds use memory streams, for which the stream size cannot be cached.
        /// </summary>
        protected bool StreamHasFixedSize => (m_streamHasFixedSize ?? (m_streamHasFixedSize = LogStream is FileStream)).Value;

        /// <summary>
        /// The length of the log being read
        /// </summary>
        protected long LogLength
        {
            get
            {
                if (StreamHasFixedSize)
                {
                    m_logStreamLength = m_logStreamLength ?? LogStream.Length;
                    return m_logStreamLength.Value;
                }
                else
                {
                    return LogStream.Length;
                }
            }
        }

        /// <summary>
        /// The reader being used to read the log
        /// </summary>
        protected EventReader m_logStreamReader;

        /// <summary>
        /// As dynamic AbsolutePath values are read from the log, they are stored here
        /// </summary>
        protected ConcurrentDenseIndex<AbsolutePath> m_capturedPaths;

        /// <summary>
        /// As dynamic StringId values are read from the log, they are stored here
        /// </summary>
        protected ConcurrentDenseIndex<StringId> m_capturedStrings;

        /// <summary>
        /// The event handlers to use when processing events from the log
        /// </summary>
        protected EventHandler[] m_handlers;

        /// <summary>
        /// The position in the log of the next event to read
        /// </summary>
        protected long? m_nextReadPosition;

        /// <summary>
        /// The length of the current event's payload size
        /// </summary>
        protected int m_currentEventPayloadSize;

        /// <summary>
        /// The initialization time of the logger which created the event stream
        /// </summary>
        public DateTime? StartTime { get; private set; }

        /// <summary>
        /// The log id read from the header
        /// </summary>
        public Guid? LogId { get; private set; }

        /// <summary>
        /// Constructs a new binary log reader for the given stream
        /// </summary>
        public BinaryLogReader(Stream logStream, PipExecutionContext context, bool closeStreamOnDispose = true)
        {
            Contract.Requires(logStream != null);
            Contract.Requires(context != null);

            LogStream = logStream;
            m_context = context;
            m_closeStreamOnDispose = closeStreamOnDispose;
            m_capturedPaths = new ConcurrentDenseIndex<AbsolutePath>(debug: false);
            m_capturedStrings = new ConcurrentDenseIndex<StringId>(debug: false);
            m_capturedStrings[0] = StringId.Invalid;
            m_logStreamReader = new EventReader(this);
            m_handlers = new EventHandler[1024];

            var logIdBytes = new byte[BinaryLogger.LogIdByteLength];
            if (logStream.Read(logIdBytes, 0, BinaryLogger.LogIdByteLength) == BinaryLogger.LogIdByteLength)
            {
                LogId = new Guid(logIdBytes);
            }
            else
            {
                LogId = null;
            }
        }

        /// <summary>
        /// Registers a handler for deserializing and interpreting the payload for events with the
        /// given event ID
        /// </summary>
        public void RegisterHandler(uint eventId, EventHandler handler)
        {
            var length = m_handlers.Length;
            while (length <= eventId)
            {
                length *= 2;
            }

            if (m_handlers.Length != length)
            {
                Array.Resize(ref m_handlers, length);
            }

            m_handlers[eventId] = handler;
        }

        /// <summary>
        /// Advanced. Used to reset the reader when operating on a stream whose contents are
        /// overwritten in place.
        /// </summary>
        public void Reset()
        {
            m_nextReadPosition = null;
        }

        /// <summary>
        /// Reads an event
        /// </summary>
        /// <returns>the result of reading the next event</returns>
        public EventReadResult ReadEvent()
        {
            try
            {
                while (true)
                {
                    if (m_nextReadPosition != null && LogStream.Position != m_nextReadPosition.Value)
                    {
                        LogStream.Seek(m_nextReadPosition.Value, SeekOrigin.Begin);
                    }

                    var position = LogStream.Position;

                    if (position == LogLength)
                    {
                        return EventReadResult.EndOfStream;
                    }

                    // Read the header
                    EventHeader header = EventHeader.ReadFrom(m_logStreamReader);
                    m_currentEventPayloadSize = header.EventPayloadSize;
                    position = LogStream.Position;

                    // There are less bytes than specified by the payload
                    // The file is corrupted or truncated
                    if (position + header.EventPayloadSize > LogLength)
                    {
                        return EventReadResult.UnexpectedEndOfStream;
                    }

                    m_nextReadPosition = position + header.EventPayloadSize;

                    // Handle the internal events
                    if (header.EventId < (uint)BinaryLogger.LogSupportEventId.Max)
                    {
                        switch ((BinaryLogger.LogSupportEventId)header.EventId)
                        {
                            case BinaryLogger.LogSupportEventId.StartTime:
                                ReadStartTimeEvent(m_logStreamReader);
                                break;
                            case BinaryLogger.LogSupportEventId.AddPath:
                                ReadPathEvent(m_logStreamReader);
                                break;
                            case BinaryLogger.LogSupportEventId.AddStringId:
                                ReadStringIdEvent(m_logStreamReader);
                                break;
                        }

                        Contract.Assert(LogStream.Position == (position + header.EventPayloadSize));
                        continue;
                    }
                    else
                    {
                        header.EventId -= (uint)BinaryLogger.LogSupportEventId.Max;
                    }

                    EventHandler handler;
                    if ((m_handlers.Length > header.EventId) &&
                        ((handler = m_handlers[header.EventId]) != null))
                    {
                        handler(header.EventId, header.WorkerId, header.Timestamp, m_logStreamReader);
                        Contract.Assert(LogStream.Position <= (position + header.EventPayloadSize), "Event handler read beyond the event payload");
                    }

                    m_logStreamReader.ReadBytes((int)(m_nextReadPosition.Value - LogStream.Position));
                    return EventReadResult.Success;
                }
            }
            catch (EndOfStreamException)
            {
                return EventReadResult.UnexpectedEndOfStream;
            }
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        protected void ReadStartTimeEvent(BuildXLReader reader)
        {
            StartTime = DateTime.FromFileTimeUtc(reader.ReadInt64());
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        protected uint ReadPathEvent(EventReader reader)
        {
            AbsolutePath parent = reader.ReadAbsolutePath();
            int index = reader.ReadInt32Compact();
            string name = reader.ReadString();

            // NOTE: We use PathAtom.UnsafeCreateFrom here because when reading from an xlg produced on a different
            // platform, the rules for path atom validity may change so we should just respect the path atom as-is without
            // doing validation.
            AbsolutePath path = parent.IsValid 
                ? parent.Combine(m_context.PathTable, PathAtom.UnsafeCreateFrom(StringId.Create(m_context.StringTable, name))) 
                : AbsolutePath.Create(m_context.PathTable, name);
            m_capturedPaths[(uint)index] = path;
            return (uint)index;
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        protected uint ReadStringIdEvent(EventReader reader)
        {
            int index = reader.ReadInt32Compact();
            string name = reader.ReadString();
            var stringId = StringId.Create(m_context.StringTable, name);
            m_capturedStrings[(uint)index] = stringId;
            return (uint)index;
        }

        /// <summary>
        /// Disposes of the logger and closes the underlying stream
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public void Dispose()
        {
            m_logStreamReader.Dispose();

            if (m_closeStreamOnDispose)
            {
                LogStream.Dispose();
            }
        }

        /// <summary>
        /// Defines the result of reading an event
        /// </summary>
        public enum EventReadResult
        {
            /// <summary>
            /// The event was successfully read
            /// </summary>
            Success,

            /// <summary>
            /// The reader reached the end of the stream while partially deserializing the event.
            /// </summary>
            UnexpectedEndOfStream,

            /// <summary>
            /// The reader is at the end of the stream and no more events can be read
            /// </summary>
            EndOfStream,
        }

        /// <summary>
        /// An extended BuildXL reader for reading event payloads.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public class EventReader : BuildXLReader
        {
            /// <summary>
            /// Log reader
            /// </summary>
            protected readonly BinaryLogReader LogReader;

            /// <summary>
            /// Gets the size of the current event's payload
            /// </summary>
            public int CurrentEventPayloadSize => LogReader.m_currentEventPayloadSize;

            internal PathTable PathTable => LogReader.m_context.PathTable;

            /// <summary>
            /// Class constructor
            /// </summary>
            public EventReader(BinaryLogReader logReader)
                : base(debug: false, stream: logReader.LogStream, leaveOpen: true)
            {
                LogReader = logReader;
            }

            /// <summary>
            /// Class constructor where stream to actually read from is different then the stream owned by the log reader
            /// </summary>
            protected EventReader(BinaryLogReader logReader, Stream stream)
                : base(debug: false, stream: stream, leaveOpen: true)
            {
                LogReader = logReader;
            }

            /// <summary>
            /// Way for classes that derive from EventReader to call BuildXLReader::ReadAbsolutePath()
            /// </summary>
            /// <returns>Absolute path value read from stream</returns>
            protected AbsolutePath BuildXLReaderReadAbsolutePath()
            {
                return base.ReadAbsolutePath();
            }

            /// <summary>
            /// Reads an AbsolutePath (which may have been added after the serialization of the path table)
            /// </summary>
            public override AbsolutePath ReadAbsolutePath()
            {
                var absolutePathType = (BinaryLogger.AbsolutePathType)ReadByte();
                switch (absolutePathType)
                {
                    case BinaryLogger.AbsolutePathType.Invalid:
                        return AbsolutePath.Invalid;
                    case BinaryLogger.AbsolutePathType.Static:
                        return base.ReadAbsolutePath();
                    default:
                        Contract.Assert(absolutePathType == BinaryLogger.AbsolutePathType.Dynamic);
                        return LogReader.m_capturedPaths[(uint)ReadInt32Compact()];
                }
            }

            /// <summary>
            /// Reads a dynamically stored StringId
            /// </summary>
            public virtual StringId ReadDynamicStringId()
            {
                return GetStringFromDynamicIndex((uint)ReadInt32Compact());
            }

            /// <summary>
            /// Converts a dynamic index into StringId
            /// </summary>
            /// <remarks>
            /// If a derived class changes the logic of how m_capturedStrings is populated,
            /// it must override this method and provide a proper mapping (index -> StringId).
            /// </remarks>
            protected virtual StringId GetStringFromDynamicIndex(uint index)
            {
                return LogReader.m_capturedStrings[index];
            }
        }
    }
}
