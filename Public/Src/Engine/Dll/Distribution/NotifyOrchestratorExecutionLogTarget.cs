// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Core;
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

        internal NotifyOrchestratorExecutionLogTarget(
            Action<MemoryStream> notifyAction,
            EngineSchedule engineSchedule,
            bool flushIfNeeded,
            CounterCollection<DistributionCounter> counters,
            DistributionBufferKind bufferKind)
            : this(new NotifyStream(notifyAction, counters, bufferKind), flushIfNeeded, engineSchedule.Context, engineSchedule.Scheduler.PipGraph.GraphId, engineSchedule.Scheduler.PipGraph.MaxAbsolutePathIndex)
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

        internal void StopObservingEvents()
        {
            // Remove target to ensure no further events are sent
            // Otherwise, the events that are sent to a disposed target would cause crashes.
            m_scheduler?.RemoveExecutionLogTarget(this);
        }

        /// <summary>
        /// Deactivates the stream and cancels the notify action.
        /// Call Deactivate() to remove the target from the scheduler.
        /// </summary>
        internal void DeactivateAndCancel()
        {
            m_notifyStream.Deactivate();

            StopObservingEvents();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (!m_isDisposed)
            {
                m_isDisposed = true;
                base.Dispose();
            }

            // Log remaining events count if any
            if (m_logger.PendingEventCount > 0)
            {
                m_scheduler.LogPendingEventsRemaingAfterDispose(m_logger.PendingEventCount);
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

        /// <inhret />
        public override void PopulateStats()
        {
            Counters.AddToCounter(ExecutionLogCounters.MaxPendingEvents, m_logger.MaxPendingEventsCount);
            Counters.AddToCounter(ExecutionLogCounters.EventWriterFactoryCalls, m_logger.EventWriterFactoryCalls);
            Counters.AddToCounter(ExecutionLogCounters.RemaingPendingEvents, m_logger.PendingEventCount);
        }

        internal sealed class NotifyStream : Stream
        {
            /// <summary>
            /// Batch size threshold at which events are sent to the orchestrator.
            /// </summary>
            internal const int BatchSizeThreshold = DistributionBufferUtilities.MaximumRetainedCapacity;

            /// <summary>
            /// Maximum backing-buffer capacity retained after a batch is sent.
            /// </summary>
            /// <remarks>
            /// A worker owns only one stream for each execution-log connection, so retaining up to 8 MiB per stream
            /// is negligible compared with the worker footprint and avoids reallocating buffers after routine
            /// multi-megabyte capacity growth.
            /// </remarks>
            internal const int MaximumRetainedBatchCapacity = BatchSizeThreshold << 3;

            private MemoryStream m_eventDataBuffer = new MemoryStream();
            private readonly Action<MemoryStream> m_notifyAction;
            private readonly int m_batchSizeThreshold;
            private readonly CounterCollection<DistributionCounter> m_counters;
            private readonly DistributionBufferKind m_bufferKind;

            /// <summary>
            /// If deactivated, functions stop writing or flushing <see cref="m_eventDataBuffer"/>.
            /// </summary>
            private bool m_isDeactivated = false;

            internal int NumFlushes;

            internal long BufferedLength => m_eventDataBuffer?.Length ?? 0;

            internal int BufferedCapacity => m_eventDataBuffer?.Capacity ?? 0;

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => 0;

            public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public NotifyStream(
                Action<MemoryStream> notifyAction,
                CounterCollection<DistributionCounter> counters,
                DistributionBufferKind bufferKind,
                int batchSizeThreshold = BatchSizeThreshold)
            {
                m_notifyAction = notifyAction ?? throw new ArgumentNullException(nameof(notifyAction));
                m_counters = counters;
                m_bufferKind = bufferKind;
                m_batchSizeThreshold = batchSizeThreshold > 0
                    ? batchSizeThreshold
                    : throw new ArgumentOutOfRangeException(nameof(batchSizeThreshold));
            }

            public NotifyStream(Action<MemoryStream> notifyAction, int batchSizeThreshold = BatchSizeThreshold)
            {
                m_notifyAction = notifyAction ?? throw new ArgumentNullException(nameof(notifyAction));
                m_batchSizeThreshold = batchSizeThreshold > 0
                    ? batchSizeThreshold
                    : throw new ArgumentOutOfRangeException(nameof(batchSizeThreshold));
            }

            public void FlushIfNeeded()
            {
                if (m_eventDataBuffer.Length >= m_batchSizeThreshold)
                {
                    Flush();
                    NumFlushes++;
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
                if (m_counters == null)
                {
                    DistributionBufferUtilities.Reset(ref m_eventDataBuffer, MaximumRetainedBatchCapacity);
                }
                else
                {
                    DistributionBufferUtilities.Reset(
                        ref m_eventDataBuffer,
                        m_counters,
                        m_bufferKind,
                        MaximumRetainedBatchCapacity);
                }
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