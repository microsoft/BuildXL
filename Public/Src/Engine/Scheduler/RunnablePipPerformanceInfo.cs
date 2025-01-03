// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.ProcessPipExecutor;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Utilities.Collections;

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

        internal TimeSpan TotalDuration => CompletedTime - ScheduleTime;

        private DispatcherKind m_currentQueue;

        private DateTime m_queueEnterTime;

        internal Dictionary<PipExecutionStep, TimeSpan> StepDurations { get; }

        // The rest is needed only for pips that are executed remotely, so they are lazily created.

        internal Dictionary<PipExecutionStep, TimeSpan> RemoteStepDurations { get; }

        internal Dictionary<PipExecutionStep, TimeSpan> RemoteQueueDurations { get; }

        internal Dictionary<PipExecutionStep, TimeSpan> PipBuildRequestQueueDurations { get; }

        internal Dictionary<PipExecutionStep, TimeSpan> PipBuildRequestGrpcDurations { get; }

        internal Dictionary<DispatcherKind, TimeSpan> QueueDurations { get; }

        internal Dictionary<PipExecutionStep, uint> Workers { get; }

        internal PipCachePerfInfo CacheLookupPerfInfo => m_cacheLookupPerfInfo ?? (m_cacheLookupPerfInfo = new PipCachePerfInfo());

        private PipCachePerfInfo m_cacheLookupPerfInfo;

        internal TimeSpan CacheMissAnalysisDuration { get; private set; }

        internal TimeSpan ExeDuration { get; set; }

        internal TimeSpan QueueWaitDurationForMaterializeOutputsInBackground { get; private set; }

        internal bool IsExecuted { get; private set; }

        internal long PushOutputsToCacheDurationMs { get; private set; }

        internal int ActualAverageWorkingSetMb;

        /// <summary>
        /// Number of retries attempted on the remote workers
        /// </summary>
        internal int RetryCountOnRemoteWorkers { get; private set; }

        /// <summary>
        /// Number of retries attempted due to low memory
        /// </summary>
        internal int RetryCountDueToLowMemory { get; private set; }

        /// <summary>
        /// Number of retries attempted due to retryable failures in SanboxedProcessPipExecutor errors
        /// </summary>
        internal int RetryCountDueToRetryableFailures { get; private set; }

        internal int RetryCount => RetryCountOnRemoteWorkers + RetryCountDueToLowMemory + RetryCountDueToRetryableFailures;

        /// <summary>
        /// Suspended duration of the process due to memory management
        /// </summary>
        internal long SuspendedDurationMs { get; private set; }

        /// <summary>
        /// The time it took to run all the retries but excluding the last successful execution.
        /// </summary>
        internal long RetryDurationMs { get; private set; }

        /// <remarks>
        /// MaterializeOutput is executed per each worker
        /// so the single index of the array might be concurrently mutated.
        /// </remarks>
        private readonly object m_lock = new object();

        internal RunnablePipPerformanceInfo(DateTime scheduleTime)
        {
            ScheduleTime = scheduleTime;
            StepDurations = new Dictionary<PipExecutionStep, TimeSpan>();

            RemoteStepDurations = new Dictionary<PipExecutionStep, TimeSpan>();
            RemoteQueueDurations = new Dictionary<PipExecutionStep, TimeSpan>();
            PipBuildRequestQueueDurations = new Dictionary<PipExecutionStep, TimeSpan>();
            PipBuildRequestGrpcDurations = new Dictionary<PipExecutionStep, TimeSpan>();
            QueueDurations = new Dictionary<DispatcherKind, TimeSpan>();
            Workers = new Dictionary<PipExecutionStep, uint>();
        }

        internal void Retried(RetryInfo pipRetryInfo, TimeSpan? duration)
        {
            Contract.Requires(pipRetryInfo?.RetryReason != null, "If retry occurs, we need to have a retry reason");

            RetryDurationMs += (long)(duration?.TotalMilliseconds ?? 0);

            switch (pipRetryInfo.RetryReason)
            {
                case RetryReason.ResourceExhaustion:
                    RetryCountDueToLowMemory++;
                    break;
                case RetryReason.RemoteWorkerFailure:
                    RetryCountOnRemoteWorkers++;
                    break;
                default:
                    RetryCountDueToRetryableFailures++;
                    break;
            }
        }

        internal void Suspended(long suspendedDurationMs)
        {
            SuspendedDurationMs += suspendedDurationMs;
        }

        internal void SetPushOutputsToCacheDurationMs(long pushOutputsToCacheDurationMs)
        {
            PushOutputsToCacheDurationMs = pushOutputsToCacheDurationMs;
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
                    QueueDurations[m_currentQueue] = QueueDurations.GetOrDefault(m_currentQueue, new TimeSpan()) + duration;
                }

                m_currentQueue = DispatcherKind.None;
            }
        }

        internal void Executed(PipExecutionStep step, TimeSpan duration)
        {
            lock (m_lock)
            {
                StepDurations[step] = StepDurations.GetOrDefault(step, new TimeSpan()) + duration;
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
            TimeSpan grpcDuration)
        {
            lock (m_lock)
            {
                Workers[step] = workerId;

                RemoteStepDurations[step] = RemoteStepDurations.GetOrDefault(step, new TimeSpan()) + remoteStepDuration;
                RemoteQueueDurations[step] = RemoteQueueDurations.GetOrDefault(step, new TimeSpan()) + remoteQueueDuration;
                PipBuildRequestQueueDurations[step] = PipBuildRequestQueueDurations.GetOrDefault(step, new TimeSpan()) + queueRequestDuration;
                PipBuildRequestGrpcDurations[step] = PipBuildRequestGrpcDurations.GetOrDefault(step, new TimeSpan()) + grpcDuration;
            }
        }

        /// <summary>
        /// Sets the cache lookup perf info that come from workers
        /// </summary>
        internal void SetCacheLookupPerfInfo(PipCachePerfInfo info)
        {
            m_cacheLookupPerfInfo = info;
        }

        internal long CalculatePipDurationMs(IPipExecutionEnvironment environment)
        {
            long pipDuration = 0;
            foreach (KeyValuePair<PipExecutionStep, TimeSpan> kv in StepDurations)
            {
                if (kv.Key.IncludeInRunningTime(environment))
                {
                    pipDuration += (long)kv.Value.TotalMilliseconds;
                }
            }

            return pipDuration;
        }

        internal long CalculateQueueDurationMs()
        {
            return QueueDurations.Values.Sum(a => (long)a.TotalMilliseconds);
        }
    }
}
