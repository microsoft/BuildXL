// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Engine.Distribution
{
    internal interface IWorkerNotificationManager 
    {
        /// <summary>
        /// Starts the notification manager, which will start to listen
        /// and forwarding events to the orchestrator
        /// </summary>
        void Start(IOrchestratorClient orchestratorClient, EngineSchedule schedule, IPipResultSerializer serializer);

        /// <summary>
        /// Report an (error / warning) event
        /// </summary>
        void ReportEventMessage(EventMessage eventMessage);

        /// <summary>
        /// Report a pip result
        /// </summary>
        public void ReportResult(ExtendedPipCompletionData pipCompletion);

        /// <summary>
        /// Stop trying to communicate with the orchestrator and processing messages
        /// </summary>
        public void Cancel();

        /// <summary>
        /// Stop listening for events and wait for any pending messages to be sent
        /// </summary>
        public void Exit();
    }

    /// <summary>
    /// Manages notification sending from worker to orchestrator
    /// </summary>
    internal partial class WorkerNotificationManager : IWorkerNotificationManager
    {
        internal readonly IWorkerPipExecutionService ExecutionService;
        private readonly LoggingContext m_loggingContext;
        internal readonly DistributionService DistributionService;
        private CancellationTokenSource m_sendCancellationSource;
        private volatile bool m_started;

        /// Individual sources for notifications
        private PipResultListener m_pipResultListener;
        private ForwardingEventListener m_forwardingEventListener;
        private readonly BlockingCollection<EventMessage> m_outgoingEvents = new BlockingCollection<EventMessage>();
        private NotifyOrchestratorExecutionLogTarget m_executionLogTarget;
        private readonly MemoryStream m_flushedExecutionLog = new MemoryStream();

        /// Notification sending
        private IOrchestratorClient m_orchestratorClient;
        private Thread m_sendThread;
        private readonly int m_maxMessagesPerBatch = EngineEnvironmentSettings.MaxMessagesPerBatch.Value;

        private int m_xlgBlobSequenceNumber = 0;
        private int m_numBatchesSent = 0;
        private volatile bool m_finishedSendingPipResults;

        // Reusable objects for send thread
        private readonly List<ExtendedPipCompletionData> m_executionResults = new List<ExtendedPipCompletionData>();
        private readonly List<EventMessage> m_eventList = new List<EventMessage>();
        private readonly WorkerNotificationArgs m_notification = new WorkerNotificationArgs();

        /// <nodoc/>
        public WorkerNotificationManager(DistributionService distributionService, IWorkerPipExecutionService executionService, LoggingContext loggingContext)
        {
            DistributionService = distributionService;
            ExecutionService = executionService;
            m_loggingContext = loggingContext;
        }

        public void Start(IOrchestratorClient orchestratorClient, EngineSchedule schedule, IPipResultSerializer serializer)
        {
            Contract.AssertNotNull(orchestratorClient);

            m_orchestratorClient = orchestratorClient;
            m_executionLogTarget = new NotifyOrchestratorExecutionLogTarget(this, schedule);
            m_forwardingEventListener = new ForwardingEventListener(this);
            m_pipResultListener = new PipResultListener(this, serializer);
            m_sendCancellationSource = new CancellationTokenSource();
            m_sendThread = new Thread(() => SendNotifications(m_sendCancellationSource.Token));
            m_sendThread.Start();

            m_started = true;
        }

        public void Exit()
        {
            if (!m_started)
            {
                return;
            }

            // Stop listening to events
            m_pipResultListener.Cancel();
            m_forwardingEventListener.Cancel();

            // The execution log target can be null if the worker failed to attach to the orchestrator
            m_executionLogTarget?.Deactivate();

            if (m_sendThread.IsAlive)
            {
                // Wait for the queues to drain
                m_sendThread.Join();
            }

            m_executionLogTarget?.Dispose();
            m_forwardingEventListener?.Dispose();
            m_sendCancellationSource.Cancel();
        }

        /// <inheritdoc/>
        public void Cancel()
        {
            if (m_started)
            {
                m_executionLogTarget?.Deactivate();
                m_pipResultListener.Cancel();
                m_forwardingEventListener.Cancel();
                m_sendCancellationSource.Cancel();
            }
        }

        public void ReportResult(ExtendedPipCompletionData pipCompletion)
        {
            Contract.Assert(m_started);
            m_pipResultListener.ReportResult(pipCompletion);
        }

        internal void FlushExecutionLog(MemoryStream listenerStream)
        {
            // Because the execution log target is filling its own 
            // buffer in a different thread, we have to copy its current buffer
            // to our own buffer to have a static blob to send with the notification
            // TODO: A better way of doing this without copying bytes. 
            // Maybe add a "Pause" operation to the binary logger so it blocks its thread
            // while we read/send the buffer.
            listenerStream.WriteTo(m_flushedExecutionLog);
            if (m_finishedSendingPipResults
                && !m_sendCancellationSource.IsCancellationRequested
                && (m_flushedExecutionLog.Length > 0 || m_pendingMessages.Any()))
            {
                var orphanMessages = m_pendingMessages.SelectMany(x => x.Value).ToList();
                foreach (var orphan in orphanMessages)
                {
                    // The general assumption is that all events will be forwarded along with the corresponding
                    // pip result. If for some reason the event gets to the m_pendingMessages queue
                    // *after* the pip result was already sent back, then it will not be sent and remain in that queue.
                    // We leverage this final message to the orchestrator to send any pending events.
                    // Still, this is an anomaly so let's log a warning.
                    Tracing.Logger.Log.DistributionWorkerForwardedOrphanMessage(m_loggingContext, orphan.Text);
                }

                // A final flush of the execution log may come after all the pip results are sent
                // In that case, we send the blob individually:
                m_orchestratorClient.NotifyAsync(new WorkerNotificationArgs()
                {
                    ExecutionLogBlobSequenceNumber = m_xlgBlobSequenceNumber++,
                    ExecutionLogData = new ArraySegment<byte>(m_flushedExecutionLog.GetBuffer(), 0, (int)m_flushedExecutionLog.Length),
                    ForwardedEvents = orphanMessages
                },
                null,
                m_sendCancellationSource.Token).Wait();

                m_flushedExecutionLog.SetLength(0);
            }
        }

        private readonly ConcurrentDictionary<long, List<EventMessage>> m_pendingMessages = new();

        /// <nodoc/>
        public void ReportEventMessage(EventMessage eventMessage)
        {
            Contract.Assert(m_started);
            if (m_sendCancellationSource.IsCancellationRequested)
            {
                // We are not sending messages anymore
                return;
            }

            if (TryExtractSemistableHashFromEvent(eventMessage, out var hash))
            {
                // Add to the queue of pending messages
                m_pendingMessages.AddOrUpdate(hash,
                    _ => new() { eventMessage },
                    (_, v) => { v.Add(eventMessage); return v; });
            }
            else
            {
                // If we couldn't associate the message to a pip id, then just send it 
                QueueEvent(eventMessage);
            }
        }

        private void QueueEvent(EventMessage eventMessage)
        {
            try
            {
                m_outgoingEvents.Add(eventMessage);
            }
            catch (InvalidOperationException)
            {
                Contract.Assert(m_outgoingEvents.IsAddingCompleted);
                // m_outgoingEvents is marked as complete: this means we are shutting down,
                // don't try to send more events to the orchestrator.
                //
                // Events can occur after the shutdown in communications is started: in
                // builds with FireForgetMaterializeOutputs we may still be executing output
                // materialization while closing down communications with the orchestrator 
                // (which called Exit on the worker already after sending the MaterializeOutput requests).
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void QueuePendingEvents(ExtendedPipCompletionData item)
        {
            if (m_pendingMessages.TryRemove(item.SemiStableHash, out var eventList))
            {
                foreach (var e in eventList)
                {
                    QueueEvent(e);
                }
            }
        }

        private bool TryExtractSemistableHashFromEvent(EventMessage eventMessage, out long hash)
        {
            // If the event is a PipProcessError, the semistable hash was already parsed for metadata
            // so extract it from there rather than using the regex
            if (eventMessage.EventId == (int)BuildXL.Processes.Tracing.LogEventId.PipProcessError)
            {
                hash = eventMessage.PipProcessErrorEvent.PipSemiStableHash;
                return true;
            }

            if (string.IsNullOrEmpty(eventMessage.Text))
            {
                hash = 0;
                return false;
            }

            // Else try to extract a pipid from the error message
            var extractedPipIds = Pip.ExtractSemistableHashes(eventMessage.Text);
            if (extractedPipIds.Count == 0)
            {
                hash = 0;
                return false;
            }
            else
            {
                hash = extractedPipIds[0];
                return true;
            }
        }

        private void SendNotifications(CancellationToken cancellationToken)
        {
            ExtendedPipCompletionData firstItem;
            while (!m_pipResultListener.ReadyToSendResultList.IsCompleted && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Sending of notifications is driven by pip results - block until we have a new result to send
                    // but also send a message every two minutes to keep the execution log and potential delayed
                    // events flowing.
                    if (!m_pipResultListener.ReadyToSendResultList.TryTake(out firstItem, (int)TimeSpan.FromMinutes(2).TotalMilliseconds, cancellationToken))
                    {
                        // Timeout is hit, we don't have any result to send right now
                        firstItem = null;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Sending was cancelled 
                    break;
                }
                catch (InvalidOperationException)
                {
                    // The results queue was completed
                    // But we still may have events / xlg to send
                    firstItem = null;
                }

                // 1. Pip results
                m_executionResults.Clear();
                if (firstItem != null)
                {
                    QueuePendingEvents(firstItem);
                    m_executionResults.Add(firstItem);

                    while (m_executionResults.Count < m_maxMessagesPerBatch && m_pipResultListener.ReadyToSendResultList.TryTake(out var item))
                    {
                        // Add any pending events to the outgoing queue
                        QueuePendingEvents(item);
                        m_executionResults.Add(item);
                    }
                }

                // 2. Forwarded events 
                m_eventList.Clear();
                while (m_outgoingEvents.TryTake(out var item))
                {
                    m_eventList.Add(item);
                }

                // 3. Pending execution log events
                using (DistributionService.Counters.StartStopwatch(DistributionCounter.WorkerFlushExecutionLogDuration))
                {
                    // Flush execution log to m_pendingExecutionLog
                    m_executionLogTarget.FlushAsync().Wait();
                }

                if (m_executionResults.Count == 0 && m_eventList.Count == 0 && m_flushedExecutionLog.Length == 0)
                {
                    // Nothing to send. This can potentially happen while exiting or if the timeout for
                    // ReadyToSendResultList.TryTake above is hit and there is no new data to send
                    continue;
                }

                // Send notification
                m_notification.WorkerId = ExecutionService.WorkerId;
                m_notification.CompletedPips = m_executionResults.Select(p => p.SerializedData).ToList();
                m_notification.ForwardedEvents = m_eventList;

                if (m_flushedExecutionLog.Length > 0)
                {
                    m_notification.ExecutionLogBlobSequenceNumber = m_xlgBlobSequenceNumber++;
                    m_notification.ExecutionLogData = new ArraySegment<byte>(m_flushedExecutionLog.GetBuffer(), 0, (int)m_flushedExecutionLog.Length);
                }
                else
                {
                    m_notification.ExecutionLogBlobSequenceNumber = 0;
                    m_notification.ExecutionLogData = new ArraySegment<byte>();
                }

                using (DistributionService.Counters.StartStopwatch(DistributionCounter.SendNotificationDuration))
                {
                    var callResult = m_orchestratorClient.NotifyAsync(m_notification,
                        m_executionResults.Select(a => a.SemiStableHash).ToList(),
                        cancellationToken).GetAwaiter().GetResult();

                    if (callResult.Succeeded)
                    {
                        foreach (var result in m_executionResults)
                        {
                            Tracing.Logger.Log.DistributionWorkerFinishedPipRequest(m_loggingContext, result.SemiStableHash, ((PipExecutionStep)result.SerializedData.Step).ToString());
                            ExecutionService.Transition(result.PipId, WorkerPipState.Done);
                        }

                        m_numBatchesSent++;
                    }
                    else if (!cancellationToken.IsCancellationRequested)
                    {
                        // Fire-forget exit call with failure.
                        // If we fail to send notification to orchestrator and we were not cancelled, the worker should fail.
                        m_executionLogTarget.Deactivate();
                        DistributionService.ExitAsync(failure: "Notify event failed to send to orchestrator", isUnexpected: true).Forget();
                        break;
                    }
                }

                m_flushedExecutionLog.SetLength(0);
            }

            m_finishedSendingPipResults = true;
            m_outgoingEvents.CompleteAdding();
            DistributionService.Counters.AddToCounter(DistributionCounter.BuildResultBatchesSentToOrchestrator, m_numBatchesSent);
        }
    }
}