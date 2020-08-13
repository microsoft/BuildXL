// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.Processes;
using BuildXL.Scheduler.WorkDispatcher;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Performance information for runnable pips
    /// </summary>
    /// <remarks>
    /// TODO: Re-evaluate this class as this might eat a lot of memory.
    /// </remarks>
    public class RunnablePipPerformanceInfo
    {
        internal DateTime ScheduleTime { get; }

        internal DateTime CompletedTime { get; private set; }

        private DispatcherKind m_currentQueue;

        private DateTime m_queueEnterTime;

        internal TimeSpan[] StepDurations { get; }

        // The rest is needed only for pips that are executed remotely, so they are lazily created.

        internal Lazy<TimeSpan[]> RemoteStepDurations { get; }

        internal Lazy<TimeSpan[]> RemoteQueueDurations { get; }

        internal Lazy<TimeSpan[]> QueueRequestDurations { get; }

        internal Lazy<TimeSpan[]> SendRequestDurations { get; }

        internal Lazy<TimeSpan[]> QueueDurations { get; }

        internal Lazy<uint[]> Workers { get; }

        internal CacheLookupPerfInfo CacheLookupPerfInfo => m_cacheLookupPerfInfo ?? (m_cacheLookupPerfInfo = new CacheLookupPerfInfo());

        private CacheLookupPerfInfo m_cacheLookupPerfInfo;

        internal TimeSpan CacheMissAnalysisDuration { get; private set; }

        internal TimeSpan QueueWaitDurationForMaterializeOutputsInBackground { get; private set; }

        internal bool IsExecuted { get; private set; }

        internal long InputMaterializationCostMbForBestWorker { get; private set; }

        internal long InputMaterializationCostMbForChosenWorker { get; private set; }

        /// <summary>
        /// Number of retries attempted due to stopped worker 
        /// </summary>
        internal int RetryCountDueToStoppedWorker { get; private set; }

        /// <summary>
        /// Number of retries attempted due to low memory
        /// </summary>
        internal int RetryCountDueToLowMemory { get; private set; }

        /// <summary>
        /// Number of retries attempted due to retryable failures in SanboxedProcessPipExecutor errors
        /// </summary>
        internal int RetryCountDueToRetryableFailures { get; private set; }

        internal int RetryCount => RetryCountDueToStoppedWorker + RetryCountDueToLowMemory + RetryCountDueToRetryableFailures;

        /// <summary>
        /// Suspended duration of the process due to memory management
        /// </summary>
        internal long SuspendedDurationMs { get; private set; }

        /// <remarks>
        /// MaterializeOutput is executed per each worker
        /// so the single index of the array might be concurrently mutated.
        /// </remarks>
        private readonly object m_lock = new object();

        internal RunnablePipPerformanceInfo(DateTime scheduleTime)
        {
            ScheduleTime = scheduleTime;
            StepDurations = new TimeSpan[(int)PipExecutionStep.Done + 1];

            RemoteStepDurations = new Lazy<TimeSpan[]>(() => new TimeSpan[(int)PipExecutionStep.Done + 1], isThreadSafe: false);
            RemoteQueueDurations = new Lazy<TimeSpan[]>(() => new TimeSpan[(int)PipExecutionStep.Done + 1], isThreadSafe: false);
            QueueRequestDurations = new Lazy<TimeSpan[]>(() => new TimeSpan[(int)PipExecutionStep.Done + 1], isThreadSafe: false);
            SendRequestDurations = new Lazy<TimeSpan[]>(() => new TimeSpan[(int)PipExecutionStep.Done + 1], isThreadSafe: false);
            QueueDurations = new Lazy<TimeSpan[]>(() => new TimeSpan[(int)DispatcherKind.Materialize + 1], isThreadSafe: false);
            Workers = new Lazy<uint[]>(() => new uint[(int)PipExecutionStep.Done + 1], LazyThreadSafetyMode.PublicationOnly);
        }

        internal void Retried(RetryInfo pipRetryInfo)
        {
            Contract.Requires(pipRetryInfo != null, "If retry occurs, we need to have a retry information (reason and location)");

            switch (pipRetryInfo.RetryReason)
            {
                case RetryReason.ResourceExhaustion:
                    RetryCountDueToLowMemory++;
                    break;
                case RetryReason.VmExecutionError:
                case RetryReason.ProcessStartFailure:
                case RetryReason.TempDirectoryCleanupFailure:
                    RetryCountDueToRetryableFailures++;
                    break;
                case RetryReason.StoppedWorker:
                    RetryCountDueToStoppedWorker++;
                    break;
            }
        }

        internal void Suspended(long suspendedDurationMs)
        {
            SuspendedDurationMs += suspendedDurationMs;
        }

        internal void Enqueued(DispatcherKind kind)
        {
            m_currentQueue = kind;
            m_queueEnterTime = DateTime.UtcNow;
        }

        internal void Dequeued(bool hasWaitedForMaterializeOutputsInBackground)
        {
            if (m_currentQueue != DispatcherKind.None)
            {
                var duration = DateTime.UtcNow - m_queueEnterTime;

                if (hasWaitedForMaterializeOutputsInBackground)
                {
                    QueueWaitDurationForMaterializeOutputsInBackground = duration;
                }
                else
                {
                    QueueDurations.Value[(int)m_currentQueue] += duration;
                }

                m_currentQueue = DispatcherKind.None;
            }
        }

        internal void Executed(PipExecutionStep step, TimeSpan duration)
        {
            lock (m_lock)
            {
                StepDurations[(int)step] += duration;
            }

            if (step == PipExecutionStep.ExecuteProcess)
            {
                IsExecuted = true;
            }
        }

        internal void Completed()
        {
            CompletedTime = DateTime.UtcNow;
        }

        internal void PerformedCacheMissAnalysis(TimeSpan duration)
        {
            CacheMissAnalysisDuration = duration;
        }

        internal void RemoteExecuted(
            uint workerId,
            PipExecutionStep step,
            TimeSpan remoteStepDuration,
            TimeSpan remoteQueueDuration,
            TimeSpan queueRequestDuration,
            TimeSpan sendRequestDuration)
        {
            lock (m_lock)
            {
                Workers.Value[(int)step] = workerId;

                RemoteStepDurations.Value[(int)step] += remoteStepDuration;
                RemoteQueueDurations.Value[(int)step] += remoteQueueDuration;
                QueueRequestDurations.Value[(int)step] += queueRequestDuration;
                SendRequestDurations.Value[(int)step] += sendRequestDuration;
            }
        }

        /// <summary>
        /// Sets the cache lookup perf info that come from workers
        /// </summary>
        internal void SetCacheLookupPerfInfo(CacheLookupPerfInfo info)
        {
            m_cacheLookupPerfInfo = info;
        }

        internal long CalculatePipDurationMs(IPipExecutionEnvironment environment)
        {
            long pipDuration = 0;
            for (int i = 0; i < StepDurations.Length; i++)
            {
                var step = (PipExecutionStep)i;
                if (step.IncludeInRunningTime(environment))
                {
                    pipDuration += (long)StepDurations[i].TotalMilliseconds;
                }
            }

            return pipDuration;
        }

        internal long CalculateQueueDurationMs()
        {
            return QueueDurations.Value.Sum(a => (long)a.TotalMilliseconds);
        }

        internal void SetInputMaterializationCost(long costMbForBestWorker, long costMbForChosenWorker)
        {
            InputMaterializationCostMbForBestWorker = costMbForBestWorker;
            InputMaterializationCostMbForChosenWorker = costMbForChosenWorker;
        }
    }
}
