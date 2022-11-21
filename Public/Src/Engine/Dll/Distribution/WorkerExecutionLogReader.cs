// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;
using static BuildXL.Utilities.Tasks.TaskUtilities;
using Logger = BuildXL.Engine.Tracing.Logger;
using BuildXL.Utilities.ConfigurationHelpers;
using BuildXL.Utilities.Instrumentation.Common;
using System.Linq;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Log target which persists execution events to a <see cref="BinaryLogger"/>. The event log may be replayed (to a consuming <see cref="IExecutionLogTarget"/>)
    /// via <see cref="ExecutionLogFileReader"/>.
    /// </summary>
    public class WorkerExecutionLogReader : IDisposable
    {
        private readonly MemoryStream m_bufferStream;
        private readonly SemaphoreSlim m_lock = TaskUtilities.CreateMutex();
        private readonly BlockingCollection<ExecutionLogData> m_queue = new BlockingCollection<ExecutionLogData>(new ConcurrentQueue<ExecutionLogData>());
        private readonly IPipExecutionEnvironment m_environment;
        private readonly LoggingContext m_loggingContext;
        private readonly TaskSourceSlim<bool> m_completionTask;
        private readonly string m_workerName;

        private int m_lastBlobSeqNumber = -1;
        private IExecutionLogTarget m_logTarget;

        private BinaryLogReader m_binaryReader;
        private ExecutionLogFileReader m_executionLogReader;

        /// <nodoc/>
        public WorkerExecutionLogReader(LoggingContext loggingContext, IExecutionLogTarget logTarget, IPipExecutionEnvironment environment, string workerName)
        {
            m_loggingContext = loggingContext;
            m_logTarget = logTarget;
            m_environment = environment;
            m_workerName = workerName;

            m_bufferStream = new MemoryStream();
            m_completionTask = TaskSourceSlim.Create<bool>();
        }

        /// <nodoc/>
        public async Task FinalizeAsync()
        {
            m_queue.CompleteAdding();

            using (m_environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_AwaitExecutionBlobCompletionDuration))
            {
                bool isQueueCompleted = false;
                using (await m_lock.AcquireAsync())
                {
                    // If there are no execution log events, there will be no calls to LogExecutionBlobAsync; as a result,
                    // the completion task will never be set, i.e., await will never return. To avoid this, we are checking
                    // the status of the queue before deciding to wait for the completion task.
                    //
                    // BlockingCollection is completed when it is empty and CompleteAdding is called. We call CompleteAdding just above;
                    // another thread can take the last element from the queue(TryTake in LogExecutionBlobAsync), so the blocking collection
                    // will become completed. However, we need to wait for that thread to process that event; otherwise, we will dispose
                    // the execution log related objects if we continue stopping the worker and the exception will happen during processing
                    // the event in that thread. We wait for that thread to process the event by acquiring the m_logBlobMutex.
                    isQueueCompleted = m_queue.IsCompleted;
                }

                if (!isQueueCompleted)
                {
                    // Wait for execution blobs to be processed.
                    await m_completionTask.Task;
                }
            }
        }

        /// <nodoc/>
        public async Task ReadEventsAsync(ExecutionLogData newData)
        {
            if (m_queue.IsCompleted)
            {
                // If orchestrator already decided to shut-down the worker, there was a connection issue with the worker for a long time and orchestrator was forced to exit the worker. 
                // However, we received execution log event from worker, it means that the worker was still able to connect to orchestrator via its Channel.
                // In that case, we do not process that log event. 
                return;
            }

            m_queue.Add(newData);

            // After we put the executionBlob in a queue, we can unblock the caller and give an ACK to the worker.
            await Task.Yield();

            // Execution log events cannot be logged by multiple threads concurrently since they must be ordered
            SemaphoreReleaser logBlobAcquiredMtx;
            using (m_environment.Counters[PipExecutorCounter.RemoteWorker_ProcessExecutionLogWaitDuration].Start())
            {
                logBlobAcquiredMtx = await m_lock.AcquireAsync();
            }

            using (logBlobAcquiredMtx)
            using (m_environment.Counters[PipExecutorCounter.RemoteWorker_ProcessExecutionLogDuration].Start())
            {
                // We need to dequeue and process the blobs in order. 
                // Here, we do not necessarily process the blob that is just added to the queue above.
                // There might be another thread that adds the next blob to the queue after the current thread, 
                // and that thread might acquire the lock earlier. 

                Contract.Assert(m_queue.TryTake(out var data), "The executionBlob queue cannot be empty");

                int seqNumber = data.SequenceNumber;
                var dataBlob = data.DataBlob;

                if (m_logTarget == null)
                {
                    return;
                }

                try
                {
                    // Workers send execution log blobs one-at-a-time, waiting for a response from the orchestrator between each message.
                    // A sequence number higher than the last logged blob sequence number indicates a worker sent a subsequent blob without waiting for a response.
                    Contract.Assert(seqNumber <= m_lastBlobSeqNumber + 1, "Workers should not send a new execution log blob until receiving a response from the orchestrator for all previous blobs.");

                    // Due to network latency and retries, it's possible to receive a message multiple times.
                    // Ignore any low numbered blobs since they should have already been logged and ack'd at some point before
                    // the worker could send a higher numbered blob.
                    if (seqNumber != m_lastBlobSeqNumber + 1)
                    {
                        return;
                    }

                    // Write the new execution log event content into buffer starting at beginning of buffer stream
                    m_bufferStream.SetLength(0);
#if NETCOREAPP
                    m_bufferStream.Write(dataBlob.Memory.Span);
#else
                    var blobArray = dataBlob.ToByteArray();
                    m_bufferStream.Write(blobArray, 0, blobArray.Length);
#endif
                    m_bufferStream.Position = 0;

                    if (m_binaryReader == null)
                    {
                        m_binaryReader = new BinaryLogReader(m_bufferStream, m_environment.Context);
                        m_executionLogReader = new ExecutionLogFileReader(m_binaryReader, m_logTarget);
                    }

                    m_binaryReader.Reset();

                    // Read all events into worker execution log target
                    if (!m_executionLogReader.ReadAllEvents())
                    {
                        Logger.Log.DistributionCallOrchestratorCodeException(m_loggingContext, nameof(WorkerExecutionLogReader), "Failed to read all worker events");
                        // Disable further processing of execution log since an error was encountered during processing
                        m_logTarget = null;
                    }
                    else
                    {
                        m_lastBlobSeqNumber = seqNumber;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log.DistributionCallOrchestratorCodeException(m_loggingContext, nameof(WorkerExecutionLogReader), ex.ToStringDemystified() + Environment.NewLine
                                                                    + "Message sequence number: " + seqNumber
                                                                    + " Last sequence number logged: " + m_lastBlobSeqNumber);
                    // Disable further processing of execution log since an exception was encountered during processing
                    m_logTarget = null;
                }

                Logger.Log.RemoteWorkerProcessedExecutionBlob(m_loggingContext, $"Worker#{m_workerName}", $"{seqNumber} - {dataBlob.Count()}");

                if (m_queue.IsCompleted)
                {
                    m_completionTask.TrySetResult(true);
                }
            }
        }

        /// <nodoc/>
        public void Dispose()
        {
            m_binaryReader?.Dispose();
        }
    }
}
