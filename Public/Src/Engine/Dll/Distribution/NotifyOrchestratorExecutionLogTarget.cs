// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Logging target for workers sending execution logs to the orchestrator
    /// </summary>
    public class NotifyOrchestratorExecutionLogTarget : ExecutionLogFileTarget
    {
        private volatile bool m_isDisposed = false;
        private readonly NotifyStream m_notifyStream;
        private readonly BinaryLogger m_logger;
        private readonly Scheduler.Scheduler m_scheduler;

        internal NotifyOrchestratorExecutionLogTarget(Action<MemoryStream> notifyAction, EngineSchedule engineSchedule, bool flushIfNeeded)
            : this(new NotifyStream(notifyAction), flushIfNeeded, engineSchedule.Context, engineSchedule.Scheduler.PipGraph.GraphId, engineSchedule.Scheduler.PipGraph.MaxAbsolutePathIndex)
        {
            m_scheduler = engineSchedule?.Scheduler;
        }

        private NotifyOrchestratorExecutionLogTarget(NotifyStream notifyStream, bool flushIfNeeded, PipExecutionContext context, Guid logId, int lastStaticAbsolutePathIndex) 
            : this(CreateBinaryLogger(notifyStream, flushIfNeeded, context, logId, lastStaticAbsolutePathIndex))
        {
            m_notifyStream = notifyStream;
        }

        private NotifyOrchestratorExecutionLogTarget(BinaryLogger logger)
            : base(logger, closeLogFileOnDispose: true)
        {
            m_logger = logger;
        }

        private static BinaryLogger CreateBinaryLogger(NotifyStream stream, bool flushIfNeeded, PipExecutionContext context, Guid logId, int lastStaticAbsolutePathIndex)
        {
            return new BinaryLogger(
                stream,
                context,
                logId,
                lastStaticAbsolutePathIndex,
                closeStreamOnDispose: true,
                onEventWritten: () =>
                {
                    if (flushIfNeeded)
                    {
                        stream.FlushIfNeeded();
                    }
                });
        }

        internal Task FlushAsync()
        {
            return m_logger.FlushAsync();
        }

        internal void Deactivate()
        {
            m_notifyStream.Deactivate();

            // Remove target to ensure no further events are sent
            // Otherwise, the events that are sent to a disposed target would cause crashes.
            m_scheduler?.RemoveExecutionLogTarget(this);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (!m_isDisposed)
            {
                m_isDisposed = true;
                base.Dispose();
            }
        }

        /// <inheritdoc />
        protected override void ReportUnhandledEvent<TEventData>(TEventData data)
        {
            if (m_isDisposed)
            {
                return;
            }

            base.ReportUnhandledEvent(data);
        }

        private class NotifyStream : Stream
        {
            /// <summary>
            /// Threshold over which events are sent to master. 32MB limit
            /// </summary>
            private const int EventDataSizeThreshold = 1 << 25;

            private MemoryStream m_eventDataBuffer = new MemoryStream();
            private readonly Action<MemoryStream> m_notifyAction;

            /// <summary>
            /// If deactivated, functions stop writing or flushing <see cref="m_eventDataBuffer"/>.
            /// </summary>
            private bool m_isDeactivated = false;

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => 0;

            public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public NotifyStream(Action<MemoryStream> notifyAction)
            {
                m_notifyAction = notifyAction;
            }

            public void FlushIfNeeded()
            {
                if (m_eventDataBuffer.Length >= EventDataSizeThreshold)
                {
                    Flush();
                }
            }
            
            public override void Flush()
            {
                if (m_eventDataBuffer == null || m_eventDataBuffer.Length == 0)
                {
                    // A flush action can be run after the stream is closed
                    // because the action runs anyway on dispose (see FlushAction)
                    // Ignore it, we already flushed everything while closing.
                    return;
                }

                m_notifyAction(m_eventDataBuffer);
                m_eventDataBuffer.SetLength(0);
            }

            public override void Close()
            {
                if (m_eventDataBuffer == null)
                {
                    // Closed already
                    return;
                }

                Deactivate();

                // Report residual data on close
                if (m_eventDataBuffer.Length != 0)
                {
                    Flush();
                }

                m_eventDataBuffer = null;
                base.Close();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (m_isDeactivated)
                {
                    return;
                }

                m_eventDataBuffer.Write(buffer, offset, count);
            }

            public void Deactivate()
            {
                m_isDeactivated = true;
            }
        }
    }
}