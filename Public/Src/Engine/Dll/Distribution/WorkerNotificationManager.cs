// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Distribution.Grpc;
using BuildXL.Engine.Distribution.Grpc;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Google.Protobuf;
using Grpc.Core;

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
        /// Should be called when a pip step has started processing in this worker
        /// </summary>
        void MarkPipProcessingStarted(long semistableHash);

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
        public void Exit(bool isClean);
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
        private volatile bool m_uncleanExit;

        /// Individual sources for notifications
        private PipResultListener m_pipResultListener;
        private ForwardingEventListener m_forwardingEventListener;
        private readonly BlockingCollection<EventMessage> m_outgoingEvents = new BlockingCollection<EventMessage>();
        private NotifyOrchestratorExecutionLogTarget m_executionLogTarget;
        private NotifyOrchestratorExecutionLogTarget m_manifestExecutionLog;

        private readonly MemoryStream m_flushedManifestEvents = new MemoryStream();

        /// Notification sending
        private IOrchestratorClient m_orchestratorClient;
        private Thread m_sendThread;
        private readonly int m_maxMessagesPerBatch = EngineEnvironmentSettings.MaxMessagesPerBatch.Value;

        private int m_manifestEventsSequenceNumber = 0;
        private int m_executionLogSequenceNumber = 0;
        private int m_numBatchesSent = 0;
        private int m_numResultsSent = 0;
        private volatile bool m_finishedSendingPipResults;

        // Reusable objects for send thread
        private readonly List<ExtendedPipCompletionData> m_executionResults = new List<ExtendedPipCompletionData>();
        private readonly List<EventMessage> m_eventList = new List<EventMessage>();

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
            m_executionLogTarget = new NotifyOrchestratorExecutionLogTarget(
                notifyAction: (stream) =>
                {
                    if (!ReportExecutionLog(stream))
                    {
                        m_executionLogTarget.Deactivate();
                    }
                }, 
                flushIfNeeded: true,
                engineSchedule: schedule);
            schedule.Scheduler.AddExecutionLogTarget(m_executionLogTarget);

            m_manifestExecutionLog = new NotifyOrchestratorExecutionLogTarget(
                notifyAction: FlushManifestEvents,
                flushIfNeeded: false,
                engineSchedule: schedule);
            schedule.Scheduler.SetManifestExecutionLog(m_manifestExecutionLog);

            m_forwardingEventListener = new ForwardingEventListener(this);
            m_pipResultListener = new PipResultListener(this, serializer);
            m_sendCancellationSource = new CancellationTokenSource();
            m_sendThread = new Thread(() => SendNotifications(m_sendCancellationSource.Token));
            m_sendThread.Start();

            m_started = true;
        }

        public void Exit(bool isClean)
        {
            if (!isClean)
            {
                m_uncleanExit = true;
            }

            if (!m_started)
            {
                return;
            }

            // Stop listening to events
            m_pipResultListener.Cancel();
            m_forwardingEventListener.Cancel();

            // The execution log target can be null if the worker failed to attach to the orchestrator
            m_executionLogTarget?.Deactivate();
            m_manifestExecutionLog?.Deactivate();

            if (m_sendThread.IsAlive)
            {
                // Wait for the queues to drain
                m_sendThread.Join();
            }

            m_executionLogTarget?.Dispose();
            m_manifestExecutionLog?.Dispose();
            m_forwardingEventListener?.Dispose();
            m_sendCancellationSource.Cancel();

            if (!m_orchestratorClient.TryFinalizeStreaming())
            {
                Tracing.Logger.Log.DistributionStreamingNetworkFailure(m_loggingContext, "localhost");
            }

            DistributionService.Counters.AddToCounter(DistributionCounter.ExecutionLogSentSize, m_executionLogTarget?.TotalSize ?? 0);
        }

        /// <inheritdoc/>
        public void Cancel()
        {
            m_uncleanExit = true;
            if (m_started)
            {
                m_executionLogTarget?.Deactivate();
                m_manifestExecutionLog?.Deactivate();
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

        internal void FlushManifestEvents(MemoryStream listenerStream)
        {
            // This method is called every time the execution log is flushed,
            // i.e., when we call m_executionLogTarget.FlushAsync() before sending a message
            // to the orchestrator. The reason is the execution log target filling its own 
            // buffer in a different thread, so we have to copy its current contents
            // to our own buffer to have a static blob to send with the notification.
            // TODO: A better way of doing this without copying bytes. 
            // Maybe add a "Pause" operation to the binary logger so it blocks its thread
            // while we read/send the buffer. 
            // This logic will be removed when/if we go back to sending the XLG separately (see work item 1883805)

            listenerStream.WriteTo(m_flushedManifestEvents);

            if (m_finishedSendingPipResults
                && !m_sendCancellationSource.IsCancellationRequested
                && m_flushedManifestEvents.Length > 0)
            {
                var message = new PipResultsInfo()
                {
                    BuildManifestEvents = new ExecutionLogData()
                    {
                        DataBlob = m_flushedManifestEvents.AsByteString(),
                        SequenceNumber = m_manifestEventsSequenceNumber++
                    }
                };

                // A final flush of the execution log may come after all the pip results are sent
                // In that case, we send the blob individually:
                m_orchestratorClient.ReportPipResultsAsync(
                    message,
                    GetPipResultsDescription(message, null),
                    m_sendCancellationSource.Token).GetAwaiter().GetResult();

                m_flushedManifestEvents.SetLength(0);
            }
        }

        // We keep message queues for every active pip step in this worker
        private readonly ConcurrentDictionary<long, PooledObjectWrapper<ConcurrentQueue<EventMessage>>> m_pendingMessages = new();

        private readonly ObjectPool<ConcurrentQueue<EventMessage>> m_queuePool = new (
            () => new(), 
            q => 
            { 
                while (q.TryDequeue(out _)) 
                {
                    // Queue has no .Clear(), dequeue everything. 
                    // Note that we will drain the queue anyways before
                    // disposing the pooled object wrapper so this should be no-op 
                }
                return q; 
            });

        /// <nodoc/>
        public void ReportEventMessage(EventMessage eventMessage)
        {
            Contract.Assert(m_started);
            if (m_sendCancellationSource.IsCancellationRequested)
            {
                // We are not sending messages anymore
                return;
            }

            if (TryExtractSemistableHashFromEvent(eventMessage, out var hash) 
                && m_pendingMessages.TryGetValue(hash, out var messageQueueForPip))
            {
                messageQueueForPip.Instance.Enqueue(eventMessage);
            }
            else
            {
                // Either we couldn't associate the message to a pip id, or the extracted id doesn't match any pip
                // that is currently being processed in this worker (the event might arrive after we have sent
                // the pip result to the orchestrator).
                // In these cases, just send the event as soon as possible.
                QueueOutboundEvent(eventMessage);
            }
        }

        private void QueueOutboundEvent(EventMessage eventMessage)
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
            // The pip step has been processed and the result is ready to be sent
            // to the orchestrator - remove the pending message queue and queue the events
            if (m_pendingMessages.TryRemove(item.SemiStableHash, out var eventQueue))
            {
                using (eventQueue)
                {
                    while (eventQueue.Instance.TryDequeue(out var e))
                    {
                        QueueOutboundEvent(e);
                    }
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
                    using (DistributionService.Counters.StartStopwatch(DistributionCounter.WorkerOutgoingMessageProcessingDuration))
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
                }

                // 2. Forwarded events 
                m_eventList.Clear();
                while (m_outgoingEvents.TryTake(out var item))
                {
                    m_eventList.Add(item);
                }

                // 3. Pending build manifest events
                using (DistributionService.Counters.StartStopwatch(DistributionCounter.WorkerFlushExecutionLogDuration))
                {
                    // Flush those events to m_flushedManifestEvents
                    m_manifestExecutionLog.FlushAsync().Wait();
                }

                if (m_executionResults.Count == 0 && m_eventList.Count == 0 && m_flushedManifestEvents.Length == 0)
                {
                    // Nothing to send. This can potentially happen while exiting or if the timeout for
                    // ReadyToSendResultList.TryTake above is hit and there is no new data to send
                    continue;
                }

                // Send notification
                var notification = new PipResultsInfo
                {
                    WorkerId = ExecutionService.WorkerId
                };

                notification.CompletedPips.AddRange(m_executionResults.Select(p => p.SerializedData));
                notification.ForwardedEvents.AddRange(m_eventList);

                notification.BuildManifestEvents = m_flushedManifestEvents.Length <= 0 ?
                    null :
                    new ExecutionLogData()
                    {
                        DataBlob = m_flushedManifestEvents.AsByteString(),
                        SequenceNumber = m_manifestEventsSequenceNumber++
                    };

                string description;
                RpcCallResult<Unit> callResult;

                using (DistributionService.Counters.StartStopwatch(DistributionCounter.GetPipResultsDescriptionDuration))
                {
                    description = GetPipResultsDescription(notification, m_executionResults);
                }

                using (DistributionService.Counters.StartStopwatch(DistributionCounter.ReportPipResultsDuration))
                {
                    callResult = m_orchestratorClient.ReportPipResultsAsync(notification,
                        description,
                        cancellationToken).GetAwaiter().GetResult();
                }

                if (callResult.Succeeded)
                {
                    using (DistributionService.Counters.StartStopwatch(DistributionCounter.PrintFinishedLogsDuration))
                    {
                        foreach (var result in m_executionResults)
                        {
                            Tracing.Logger.Log.DistributionWorkerFinishedPipRequest(m_loggingContext, result.SemiStableHash, ((PipExecutionStep)result.SerializedData.Step).AsString());
                            m_numResultsSent++;
                        }

                        m_numBatchesSent++;
                    }
                }
                else if (!cancellationToken.IsCancellationRequested)
                {
                    // Fire-forget exit call with failure.
                    // If we fail to send notification to orchestrator and we were not cancelled, the worker should fail.
                    m_executionLogTarget?.Deactivate();
                    m_manifestExecutionLog?.Deactivate();
                    m_uncleanExit = true;
                    DistributionService.ExitAsync(failure: "Notify event failed to send to orchestrator", isUnexpected: true).Forget();
                    break;
                }

                m_flushedManifestEvents.SetLength(0);
            }

            m_finishedSendingPipResults = true;
            m_outgoingEvents.CompleteAdding();

            if (m_pendingMessages.Any())
            {
                var pendingPipDetails = new List<string>();
                var orphanMessages = new List<(long PipId, EventMessage Message)>();
                foreach (var kvp in m_pendingMessages)
                {
                    var (pipId, queue) = (kvp.Key, kvp.Value.Instance);

                    pendingPipDetails.Add($"{Pip.FormatSemiStableHash(pipId)} (count: {queue.Count})");

                    foreach (var e in queue)
                    {
                        orphanMessages.Add((pipId, e));
                    };
                }

                foreach (var orphan in orphanMessages)
                {
                    // All events should have been forwarded along with the corresponding
                    // pip result, or immediately if we don't have a running pip step to associate to the message.
                    Tracing.Logger.Log.DistributionWorkerOrphanMessage(m_loggingContext, Pip.FormatSemiStableHash(orphan.PipId), orphan.Message.Text);
                }

                // Log to track the ocurrence of this. Remove 
                Tracing.Logger.Log.DistributionWorkerPendingMessageQueues(m_loggingContext, m_uncleanExit, string.Join(", ", pendingPipDetails.ToArray()));
            }

            DistributionService.Counters.AddToCounter(DistributionCounter.BuildResultBatchesSentToOrchestrator, m_numBatchesSent);
            DistributionService.Counters.AddToCounter(DistributionCounter.BuildResultsSentToOrchestrator, m_numResultsSent);
        }

        private bool ReportExecutionLog(MemoryStream memoryStream)
        {
            var message = new ExecutionLogInfo()
            {
                WorkerId = ExecutionService.WorkerId,
                Events = new ExecutionLogData()
                {
                    DataBlob = memoryStream.AsByteString(),
                    SequenceNumber = m_executionLogSequenceNumber++
                }
            };

            using (DistributionService.Counters.StartStopwatch(DistributionCounter.ReportExecutionLogDuration))
            {
                // Send event data to orchestrator synchronously. This will only block the dedicated thread used by the binary logger.
                return m_orchestratorClient.ReportExecutionLogAsync(message).GetAwaiter().GetResult().Succeeded;
            }
        }

        public void MarkPipProcessingStarted(long semistableHash)
        {
            // Add a queue for pending messages for this pip step
            Contract.Assert(m_pendingMessages.TryAdd(semistableHash, m_queuePool.GetInstance()), 
                "There shouldn't be a pending message queue for a pip we are about to process"); 
        }

        private static string GetPipResultsDescription(PipResultsInfo notificationArgs, IList<ExtendedPipCompletionData> pips)
        {
            using (var sbPool = Pools.GetStringBuilder())
            {
                var sb = sbPool.Instance;

                if (pips?.Count > 0)
                {
                    sb.Append("ReportPipResults: ");
                    foreach (var pip in pips)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, "{0:X16} ", pip.SemiStableHash);
                    }
                }

                var xlgDataCount = notificationArgs.BuildManifestEvents?.DataBlob.Count();
                if (xlgDataCount > 0)
                {
                    sb.AppendFormat(" BuildManifestEvents: Size={0}, SequenceNumber={1}", xlgDataCount, notificationArgs.BuildManifestEvents.SequenceNumber);
                }

                if (notificationArgs.ForwardedEvents?.Count > 0)
                {
                    sb.AppendFormat(" ForwardedEvents: Count={0}", notificationArgs.ForwardedEvents.Count);
                }

                return sb.ToString();
            }
        }
    }
}