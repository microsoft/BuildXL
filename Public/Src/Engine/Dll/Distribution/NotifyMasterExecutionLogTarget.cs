// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Logging target on master for sending execution logs in distributed builds
    /// </summary>
    public class NotifyMasterExecutionLogTarget : ExecutionLogFileTarget
    {
        private volatile bool m_isDisposed = false;

        internal NotifyMasterExecutionLogTarget(uint workerId, IMasterClient masterClient, PipExecutionContext context, Guid logId, int lastStaticAbsolutePathIndex, DistributionServices services)
            : this(new NotifyStream(workerId, masterClient, services), context, logId, lastStaticAbsolutePathIndex)
        {
        }

        private NotifyMasterExecutionLogTarget(NotifyStream stream, PipExecutionContext context, Guid logId, int lastStaticAbsolutePathIndex)
            : base(new BinaryLogger(stream, context, logId, lastStaticAbsolutePathIndex, closeStreamOnDispose: true, onEventWritten: () => stream.FlushIfNeeded()), closeLogFileOnDispose: true)
        {
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
            /// Threshold over which events are sent to master.
            /// </summary>
            private const int EventDataSizeThreshold = 1 << 20;

            private MemoryStream m_eventDataBuffer = new MemoryStream();

            private readonly uint m_workerId;

            private readonly IMasterClient m_masterClient;

            /// <summary>
            /// If deactivated, functions stop writing or flushing <see cref="m_eventDataBuffer"/>.
            /// </summary>
            private bool IsDeactivated = false;

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => 0;

            public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            private int m_blobSequenceNumber = 0;

            private readonly DistributionServices m_services;

            public NotifyStream(uint workerId, IMasterClient masterClient, DistributionServices services)
            {
                m_workerId = workerId;
                m_masterClient = masterClient;
                m_services = services;
            }

            public override void Flush()
            {
                if (IsDeactivated)
                {
                    return;
                }

                var buffer = m_eventDataBuffer.GetBuffer();

                using (m_services.Counters.StartStopwatch(DistributionCounter.SendExecutionLogDuration))
                {
                    // Send event data to master synchronously. This will only block the dedicated thread used by the binary logger.
                    var callResult = m_masterClient.NotifyAsync(new WorkerNotificationArgs()
                    {
                        WorkerId = m_workerId,
                        ExecutionLogData = new ArraySegment<byte>(buffer, 0, (int)m_eventDataBuffer.Length),
                        ExecutionLogBlobSequenceNumber = m_blobSequenceNumber++,
                    },
                    null).GetAwaiter().GetResult();

                    // Reset the buffer now that data is sent to master
                    m_eventDataBuffer.SetLength(0);

                    if (!callResult.Succeeded)
                    {
                        // Deactivate so that no further writes are sent to the master.
                        IsDeactivated = true;
                    }
                }
            }

            public override void Close()
            {
                // Send residual data to master on close
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
                if (IsDeactivated)
                {
                    return;
                }

                m_eventDataBuffer.Write(buffer, offset, count);
            }

            public void FlushIfNeeded()
            {
                if (m_eventDataBuffer.Length >= EventDataSizeThreshold)
                {
                    Flush();
                }
            }
        }
    }
}