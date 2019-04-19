// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Allows the events of an log file to be read and deserialized in parallel
    /// </summary>
    /// <remarks>
    /// Events will be sent to the "IExecutionLogTarget" out of order
    /// </remarks>
    public sealed class ParallelBinaryLogReader : BinaryLogReader
    {
        /// <summary>
        /// The name of the log file being read
        /// </summary>
        private readonly string m_logFilename;

        /// <summary>
        /// Many of the event types depend on the read path event. This
        /// collection provides a way to resolve those dependencies.
        /// </summary>
        /// <remarks>
        /// Every time any event tries to read a dynamic absolute path that has
        /// not yet been resolved, it will add the index for that absolute path
        /// to this collection along with a signaling mechanism. When the
        /// absolute path is then resolved, all those events that were blocked
        /// will be signaled so they can continue deserialization.
        /// </remarks>
        private readonly ConcurrentDictionary<uint, ManualResetEvent> m_absolutePathsBlockingEvents = new ConcurrentDictionary<uint, ManualResetEvent>();

        /// <summary>
        /// Constructs a new parallel binary log reader for the given file path
        /// </summary>
        /// <param name="logFilename">The path of the log file to read</param>
        /// <param name="context">Used for path events</param>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "The file stream opened here will be disposed by the base class")]
        public ParallelBinaryLogReader(string logFilename, PipExecutionContext context)
            : base(File.Open(logFilename, FileMode.Open, FileAccess.Read, FileShare.Read), context, true)
        {
            m_logFilename = logFilename;
        }

        /// <summary>
        /// Deserializes a path event using the base class implementation. Signals any events that are waiting for this path event to be deserialized
        /// </summary>
        /// <param name="reader">The reader to use to process the path event</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        private void ParallelReadPathEvent(EventReader reader)
        {
            // Read the path event and get the index that was just read
            uint index = ReadPathEvent(reader);

            // If other events are waiting on this path event, signal them that the path is now available
            ManualResetEvent manualResetEvent;
            if (m_absolutePathsBlockingEvents.TryRemove(index, out manualResetEvent))
            {
                manualResetEvent.Set();
            }
        }

        /// <summary>
        /// Starts a task that is responsible for deserializing all path events as fed to it through the specified queue
        /// </summary>
        /// <param name="pathEventsToDeserialize">The queue where path events (as file positions) to deserialize will be fed</param>
        /// <returns>A task that will finish when the queue that it consumes from is both marked completed and empty</returns>
        private Task CreatePathEventConsumerTask(BlockingCollection<long> pathEventsToDeserialize)
        {
            return Task.Run(() =>
            {
                using (FileStream fileStream = File.Open(m_logFilename, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    ParallelEventReader parallelEventReader = new ParallelEventReader(this, fileStream);
                    while (!pathEventsToDeserialize.IsCompleted)
                    {
                        // Get next AddPath event file position
                        long positionToDeserialize;
                        try
                        {
                            positionToDeserialize = pathEventsToDeserialize.Take();
                        }
                        catch (InvalidOperationException)
                        {
                            // This exception is thrown when CompleteAdding is called while we are blocked on the call to Take
                            // If we see this exception, we know we are done processing path events and the task can exit
                            return;
                        }

                        // Seek to the start of the next AddPath event to deserialize
                        fileStream.Seek(positionToDeserialize, SeekOrigin.Begin);

                        EventHeader header = EventHeader.ReadFrom(parallelEventReader);

                        // Ensure event id it is for an AddPath event
                        Contract.Assert((BinaryLogger.LogSupportEventId)header.EventId == BinaryLogger.LogSupportEventId.AddPath);
                        // Determine what position the file stream should have after calling the event handler
                        var startOfNextEvent = fileStream.Position + header.EventPayloadSize;

                        // Handle the event
                        ParallelReadPathEvent(parallelEventReader);

                        // Ensure that the correct number of bytes were read out of the file
                        Contract.Assert(fileStream.Position == startOfNextEvent);
                    }
                }
            });
        }

        /// <summary>
        /// Starts a task that is responsible for deserializing all non-path events as fed to it through the specified queue
        /// </summary>
        /// <param name="nonPathEventsToDeserialize">The queue where non-path events (as file positions) to deserialize will be fed</param>
        /// <returns>A task that will finish when the queue that it consumes from is both marked completed and empty</returns>
        private Task CreateNonPathEventConsumerTask(BlockingCollection<long> nonPathEventsToDeserialize)
        {
            return Task.Run(() =>
            {
                using (FileStream fileStream = File.Open(m_logFilename, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    ParallelEventReader parallelEventReader = new ParallelEventReader(this, fileStream);
                    while (!nonPathEventsToDeserialize.IsCompleted)
                    {
                        // Get next event file position
                        long positionToDeserialize;
                        try
                        {
                            positionToDeserialize = nonPathEventsToDeserialize.Take();
                        }
                        catch (InvalidOperationException)
                        {
                            // This exception is thrown when CompleteAdding is called while we are blocked on the call to Take
                            // If we see this exception, we know we are done processing events and the task can exit
                            return;
                        }

                        // Seek to the start of the next event to deserialize
                        fileStream.Seek(positionToDeserialize, SeekOrigin.Begin);

                        EventHeader header = EventHeader.ReadFrom(parallelEventReader);
                        // Ensure that event id is NOT an AddPath event
                        Contract.Assert((BinaryLogger.LogSupportEventId)header.EventId != BinaryLogger.LogSupportEventId.AddPath);

                        var startOfNextEvent = fileStream.Position + header.EventPayloadSize;

                        // Handle the internal events
                        if (header.EventId < (uint)BinaryLogger.LogSupportEventId.Max)
                        {
                            switch ((BinaryLogger.LogSupportEventId)header.EventId)
                            {
                                case BinaryLogger.LogSupportEventId.StartTime:
                                    ReadStartTimeEvent(parallelEventReader);
                                    break;
                                case BinaryLogger.LogSupportEventId.AddStringId:
                                    ReadStringIdEvent(parallelEventReader);
                                    break;
                            }

                            Contract.Assert(fileStream.Position == startOfNextEvent);
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
                            handler(header.EventId, header.WorkerId, header.Timestamp, parallelEventReader);
                            Contract.Assert(fileStream.Position <= startOfNextEvent, "Event handler read beyond the event payload");
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Reads all events in the log file and processes them in parallel
        /// </summary>
        /// <returns>If all events are read successfully, <see cref="BinaryLogReader.EventReadResult.EndOfStream"/> will be returned</returns>
        public EventReadResult ReadAllEvents()
        {
            int numPathEventConsumers = 2;
            int numNotPathEventConsumers = 5;

            // Create a boolean array to decide which events to process
            bool[] shouldProcessEvent = new bool[((int)BinaryLogger.LogSupportEventId.Max) + m_handlers.Length];
            for (int i = 0; i < shouldProcessEvent.Length; ++i)
            {
                if (i < (int)BinaryLogger.LogSupportEventId.Max)
                {
                    // Always process internal events
                    shouldProcessEvent[i] = true;
                }
                else if (m_handlers[i - (int)BinaryLogger.LogSupportEventId.Max] != null)
                {
                    // Only process event if handler is defined
                    shouldProcessEvent[i] = true;
                }
                else
                {
                    shouldProcessEvent[i] = false;
                }
            }

            BlockingCollection<long>[] addPathEventsToDeserialize = new BlockingCollection<long>[numPathEventConsumers];
            BlockingCollection<long>[] notAddPathEventsToDeserialize = new BlockingCollection<long>[numNotPathEventConsumers];

            try
            {
                // Initialize the queues
                for (int i = 0; i < addPathEventsToDeserialize.Length; ++i)
                {
                    addPathEventsToDeserialize[i] = new BlockingCollection<long>();
                }

                for (int i = 0; i < notAddPathEventsToDeserialize.Length; ++i)
                {
                    notAddPathEventsToDeserialize[i] = new BlockingCollection<long>();
                }

                // Start the event consumers
                Task[] pathEventConsumers = new Task[numPathEventConsumers];
                for (int i = 0; i < pathEventConsumers.Length; ++i)
                {
                    pathEventConsumers[i] = CreatePathEventConsumerTask(addPathEventsToDeserialize[i]);
                }

                Task[] notPathEventConsumers = new Task[numNotPathEventConsumers];
                for (int i = 0; i < notPathEventConsumers.Length; ++i)
                {
                    notPathEventConsumers[i] = CreateNonPathEventConsumerTask(notAddPathEventsToDeserialize[i]);
                }

                // Event positions are added to the queues in a round robin manner
                // These variables indicate which queue to put the next event position in
                int pathEventQueueToAddTo = 0;
                int notPathEventQueueToAddTo = 0;

                EventReadResult result;
                try
                {
                    while (true)
                    {
                        if (m_nextReadPosition != null)
                        {
                            LogStream.Seek(m_nextReadPosition.Value, SeekOrigin.Begin);
                        }

                        var position = LogStream.Position;

                        if (position == LogLength)
                        {
                            result = EventReadResult.EndOfStream;
                            break;
                        }

                        // Read the header
                        EventHeader header = EventHeader.ReadFrom(m_logStreamReader);

                        if (shouldProcessEvent[header.EventId])
                        {
                            // Add event to appropriate queue
                            if ((BinaryLogger.LogSupportEventId)header.EventId == BinaryLogger.LogSupportEventId.AddPath)
                            {
                                addPathEventsToDeserialize[pathEventQueueToAddTo].Add(position);
                                pathEventQueueToAddTo++;
                                pathEventQueueToAddTo %= numPathEventConsumers;
                            }
                            else
                            {
                                notAddPathEventsToDeserialize[notPathEventQueueToAddTo].Add(position);
                                notPathEventQueueToAddTo++;
                                notPathEventQueueToAddTo %= numNotPathEventConsumers;
                            }
                        }

                        m_currentEventPayloadSize = header.EventPayloadSize;
                        position = LogStream.Position;

                        // There are less bytes than specified by the payload
                        // The file is corrupted or truncated
                        if (position + header.EventId > LogLength)
                        {
                            result = EventReadResult.UnexpectedEndOfStream;
                            break;
                        }

                        m_nextReadPosition = position + header.EventPayloadSize;
                    }
                }
                catch (EndOfStreamException)
                {
                    result = EventReadResult.UnexpectedEndOfStream;
                }

                // We are done adding events to the queues so mark all the queues as complete for adding
                foreach (var q in addPathEventsToDeserialize)
                {
                    q.CompleteAdding();
                }

                foreach (var q in notAddPathEventsToDeserialize)
                {
                    q.CompleteAdding();
                }

                // Wait for all events to be processed
                Task.WaitAll(pathEventConsumers);
                Task.WaitAll(notPathEventConsumers);

                return result;
            }
            finally
            {
                // Dispose the queues
                foreach (var q in addPathEventsToDeserialize)
                {
                    q.Dispose();
                }

                foreach (var q in notAddPathEventsToDeserialize)
                {
                    q.Dispose();
                }
            }
        }

        /// <summary>
        /// An extended BuildXL reader for reading event payloads
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public class ParallelEventReader : EventReader
        {
            private ParallelBinaryLogReader ParallelLogReader => LogReader as ParallelBinaryLogReader;

            /// <summary>
            /// Class constructor
            /// </summary>
            public ParallelEventReader(ParallelBinaryLogReader logReader, Stream stream)
                : base(logReader: logReader, stream: stream)
            {
            }

            /// <summary>
            /// Reads an AbsolutePath (which may have been added after the serialization of the path table)
            /// </summary>
            /// <remarks>
            /// When a log file is being read serially and the absolute path
            /// type is dynamic, the CapturedPaths array will always have a
            /// valid AbsolutePath value at the specified index. When reading
            /// in parallel, this is not guaranteed. To fix this, manual reset
            /// events are used to allow for the read path event to signal when
            /// the needed AbsolutePath value becomes valid.
            /// </remarks>
            public override AbsolutePath ReadAbsolutePath()
            {
                var absolutePathType = (BinaryLogger.AbsolutePathType)ReadByte();
                switch (absolutePathType)
                {
                    case BinaryLogger.AbsolutePathType.Invalid:
                        return AbsolutePath.Invalid;
                    case BinaryLogger.AbsolutePathType.Static:
                        return BuildXLReaderReadAbsolutePath();
                    default:
                        Contract.Assert(absolutePathType == BinaryLogger.AbsolutePathType.Dynamic);
                        uint index = (uint)ReadInt32Compact();
                        AbsolutePath result = ParallelLogReader.m_capturedPaths[index];
                        if (result == AbsolutePath.Invalid)
                        {
                            // The read path event for the needed AbsolutePath value has not been processed yet so we must wait
                            // We register a manual reset event for the index of the AbsolutePath value that we need
                            ManualResetEvent manualResetEvent = ParallelLogReader.m_absolutePathsBlockingEvents.GetOrAdd(index, (i) => new ManualResetEvent(false));

                            // While we were registering, the AbsolutePath value could have become valid so we check
                            result = ParallelLogReader.m_capturedPaths[index];

                            if (result != AbsolutePath.Invalid)
                            {
                                // If it became valid, we try to remove the manual reset event and signal it
                                if (ParallelLogReader.m_absolutePathsBlockingEvents.TryRemove(index, out manualResetEvent))
                                {
                                    // It's possible that other events are waiting on this AbsolutePath becoming valid so signal them
                                    manualResetEvent.Set();
                                }
                            }
                            else
                            {
                                // Block until the AbsolutePath becomes valid
                                manualResetEvent.WaitOne();
                                result = ParallelLogReader.m_capturedPaths[index];
                            }
                        }

                        return result;
                }
            }

            /// <nodoc />
            protected override StringId GetStringFromDynamicIndex(uint index)
            {
                // Currently, ParallelEventReader/ParallelBinaryLogReader do not support dynamically written StringIds.
                // ParallelLogReader.m_capturedPaths might not contain a proper mapping when a call to this method is made.
                return StringId.Invalid;
            }
        }
    }
}
