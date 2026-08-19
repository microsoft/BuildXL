// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.ProcessPipExecutor;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Performance information for runnable pips
    /// </summary>
    public class RunnablePipPerformanceInfo
    {
        internal static readonly int PipExecutionStepCount = (int)EnumTraits<PipExecutionStep>.MaxValue + 1;
        internal static readonly int DispatcherKindCount = (int)EnumTraits<DispatcherKind>.MaxValue + 1;

        // Remote pips normally report only a few distinct steps, so start small and grow on demand.
        private const int InitialRemoteStepCapacity = 3;

        internal DateTime ScheduleTime { get; }

        internal DateTime CompletedTime { get; private set; }

        internal TimeSpan TotalDuration => CompletedTime - ScheduleTime;

        private DispatcherKind m_currentQueue;

        private DateTime m_queueEnterTime;

        // Step durations are followed by dispatcher queue durations in the same dense array.
        private readonly uint[] m_durationsMs = new uint[PipExecutionStepCount + DispatcherKindCount];

        private RemoteStepPerformance[] m_remoteSteps;

        private ushort m_remoteStepCount;

        internal PipCachePerfInfo CacheLookupPerfInfo => m_cacheLookupPerfInfo;

        private PipCachePerfInfo m_cacheLookupPerfInfo;

        internal TimeSpan CacheMissAnalysisDuration { get; private set; }

        internal TimeSpan ExeDuration { get; set; }

        /// <summary>
        /// When <see cref="ExeDuration"/> was injected via a <c>##bxl[runtimeSecs]</c> hint, holds the original locally-measured execution
        /// time; <c>null</c> when the value was measured. See <see cref="IsInjectedExeDuration"/>.
        /// </summary>
        internal TimeSpan? OriginalExeDuration { get; set; }

        /// <summary>
        /// Indicates that <see cref="ExeDuration"/> was injected via a <c>##bxl[runtimeSecs]</c> hint rather than measured.
        /// </summary>
        internal bool IsInjectedExeDuration => OriginalExeDuration.HasValue;

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

        internal RunnablePipPerformanceInfo(DateTime scheduleTime)
        {
            ScheduleTime = scheduleTime;
        }

        internal PipCachePerfInfo GetOrCreateCacheLookupPerfInfo()
        {
            var result = Volatile.Read(ref m_cacheLookupPerfInfo);
            if (result != null)
            {
                return result;
            }

            var newInfo = new PipCachePerfInfo();
            return Interlocked.CompareExchange(ref m_cacheLookupPerfInfo, newInfo, null) ?? newInfo;
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
                    int index = PipExecutionStepCount + (int)m_currentQueue;
                    AddDuration(ref m_durationsMs[index], duration);
                }

                m_currentQueue = DispatcherKind.None;
            }
        }

        internal void Executed(PipExecutionStep step, TimeSpan duration)
        {
            // MaterializeOutputs can be executed concurrently for multiple workers.
            lock (m_durationsMs)
            {
                int index = (int)step;
                AddDuration(ref m_durationsMs[index], duration);
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
            lock (m_durationsMs)
            {
                int index = GetOrAddRemoteStepIndex(step);
                ref var remoteStep = ref m_remoteSteps[index];
                remoteStep.WorkerId = workerId;
                AddDuration(ref remoteStep.StepDurationMs, remoteStepDuration);
                AddDuration(ref remoteStep.QueueDurationMs, remoteQueueDuration);
                AddDuration(ref remoteStep.RequestDurationMs, queueRequestDuration);
                AddDuration(ref remoteStep.GrpcDurationMs, grpcDuration);
            }
        }

        internal long GetStepDurationMs(PipExecutionStep step)
        {
            return m_durationsMs[(int)step];
        }

        internal TimeSpan GetStepDuration(PipExecutionStep step)
        {
            return TimeSpan.FromMilliseconds(GetStepDurationMs(step));
        }

        internal long GetQueueDurationMs(DispatcherKind kind)
        {
            return m_durationsMs[PipExecutionStepCount + (int)kind];
        }

        internal TimeSpan GetQueueDuration(DispatcherKind kind)
        {
            return TimeSpan.FromMilliseconds(GetQueueDurationMs(kind));
        }

        internal uint GetWorkerId(PipExecutionStep step)
        {
            lock (m_durationsMs)
            {
                int index = FindRemoteStepIndex(step);
                return index >= 0 ? m_remoteSteps[index].WorkerId : 0U;
            }
        }

        internal bool WasExecutedRemotely(PipExecutionStep step)
        {
            lock (m_durationsMs)
            {
                return FindRemoteStepIndex(step) >= 0;
            }
        }

        internal long GetRemoteStepDurationMs(PipExecutionStep step)
        {
            lock (m_durationsMs)
            {
                int index = FindRemoteStepIndex(step);
                return index >= 0 ? m_remoteSteps[index].StepDurationMs : 0;
            }
        }

        internal long GetRemoteQueueDurationMs(PipExecutionStep step)
        {
            lock (m_durationsMs)
            {
                int index = FindRemoteStepIndex(step);
                return index >= 0 ? m_remoteSteps[index].QueueDurationMs : 0;
            }
        }

        internal long GetPipBuildRequestQueueDurationMs(PipExecutionStep step)
        {
            lock (m_durationsMs)
            {
                int index = FindRemoteStepIndex(step);
                return index >= 0 ? m_remoteSteps[index].RequestDurationMs : 0;
            }
        }

        internal long GetPipBuildRequestGrpcDurationMs(PipExecutionStep step)
        {
            lock (m_durationsMs)
            {
                int index = FindRemoteStepIndex(step);
                return index >= 0 ? m_remoteSteps[index].GrpcDurationMs : 0;
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
            for (int i = 0; i < PipExecutionStepCount; i++)
            {
                var step = (PipExecutionStep)i;
                if (step.IncludeInRunningTime(environment))
                {
                    pipDuration += DurationForStepMs(step, m_durationsMs[i]);
                }
            }

            return pipDuration;
        }

        /// <summary>
        /// The running-time contribution of a single step. For the <see cref="PipExecutionStep.ExecuteProcess"/> step, when the process
        /// execution time was injected via a <c>##bxl[runtimeSecs]</c> hint (see <see cref="OriginalExeDuration"/>), the injected
        /// <see cref="ExeDuration"/> is substituted for the locally-measured step duration so that the pip's running time reflects the
        /// true duration of the (externally executed) work while still accounting for every other step. All other steps use their
        /// measured duration.
        /// </summary>
        private long DurationForStepMs(PipExecutionStep step, long measuredStepDurationMs)
        {
            if (IsInjectedExeDuration && step == PipExecutionStep.ExecuteProcess)
            {
                return (long)ExeDuration.TotalMilliseconds;
            }

            return measuredStepDurationMs;
        }

        /// <summary>
        /// Calculates the pip's "work" duration: the running-time step durations excluding the time spent queued
        /// on a remote worker. This is the per-pip duration accumulated into
        /// <see cref="BuildXL.Pips.PipRuntimeInfo.CriticalPathDurationMs"/>.
        /// </summary>
        /// <remarks>
        /// For remotely executed steps, the locally-measured step duration includes the time the request
        /// spent waiting in the remote worker's queue (see <see cref="RunnablePip.LogExecutionStepPerformance"/>).
        /// That queue time is a symptom of resource contention rather than the pip's own work, so it is subtracted
        /// here. (Local dispatcher queue time is never part of the recorded step durations, so it is already
        /// excluded.)
        /// </remarks>
        internal long CalculateWorkBasedPipDurationMs(IPipExecutionEnvironment environment)
        {
            long pipDuration = 0;
            for (int i = 0; i < PipExecutionStepCount; i++)
            {
                var step = (PipExecutionStep)i;
                if (step.IncludeInRunningTime(environment))
                {
                    long stepMs = DurationForStepMs(step, m_durationsMs[i]);
                    long remoteQueueMs = GetRemoteQueueDurationMs(step);
                    pipDuration += Math.Max(0, stepMs - remoteQueueMs);
                }
            }

            return pipDuration;
        }

        internal long CalculateQueueDurationMs()
        {
            long durationMs = 0;
            for (int i = 0; i < DispatcherKindCount; i++)
            {
                durationMs += m_durationsMs[PipExecutionStepCount + i];
            }

            return durationMs;
        }

        internal long CalculateRemoteQueueDurationMs()
        {
            lock (m_durationsMs)
            {
                long durationMs = 0;
                for (int i = 0; i < m_remoteStepCount; i++)
                {
                    durationMs += m_remoteSteps[i].QueueDurationMs;
                }

                return durationMs;
            }
        }

        private int GetOrAddRemoteStepIndex(PipExecutionStep step)
        {
            int index = FindRemoteStepIndex(step);
            if (index >= 0)
            {
                return index;
            }

            if (m_remoteSteps == null)
            {
                m_remoteSteps = new RemoteStepPerformance[InitialRemoteStepCapacity];
            }
            else if (m_remoteStepCount == m_remoteSteps.Length)
            {
                Array.Resize(ref m_remoteSteps, Math.Min(PipExecutionStepCount, m_remoteSteps.Length * 2));
            }

            index = m_remoteStepCount++;
            m_remoteSteps[index].Step = checked((byte)step);
            return index;
        }

        private int FindRemoteStepIndex(PipExecutionStep step)
        {
            byte stepValue = checked((byte)step);
            for (int i = 0; i < m_remoteStepCount; i++)
            {
                if (m_remoteSteps[i].Step == stepValue)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void AddDuration(ref uint currentDurationMs, TimeSpan duration)
        {
            double durationMs = duration.TotalMilliseconds;
            if (durationMs <= 0)
            {
                return;
            }

            ulong addedDurationMs = durationMs >= uint.MaxValue ? uint.MaxValue : (uint)durationMs;
            ulong totalDurationMs = currentDurationMs + addedDurationMs;
            currentDurationMs = totalDurationMs >= uint.MaxValue ? uint.MaxValue : (uint)totalDurationMs;
        }

        private struct RemoteStepPerformance
        {
            public uint StepDurationMs;
            public uint QueueDurationMs;
            public uint RequestDurationMs;
            public uint GrpcDurationMs;
            public uint WorkerId;
            public byte Step;
        }

        #region Duration aggregation

        // These methods accumulate compact per-pip values into caller-owned totals without exposing the backing storage or allocating snapshots.

        internal void AddStepDurationsTo(IList<long> durations)
        {
            for (int i = 0; i < PipExecutionStepCount; i++)
            {
                durations[i] += m_durationsMs[i];
            }
        }

        internal void AddQueueDurationsTo(IList<long> durations)
        {
            for (int i = 0; i < DispatcherKindCount; i++)
            {
                durations[i] += m_durationsMs[PipExecutionStepCount + i];
            }
        }

        internal void AddRemoteStepDurationsTo(IList<long> durations)
        {
            lock (m_durationsMs)
            {
                for (int i = 0; i < m_remoteStepCount; i++)
                {
                    durations[m_remoteSteps[i].Step] += m_remoteSteps[i].StepDurationMs;
                }
            }
        }

        internal void AddRemoteQueueDurationsTo(IList<long> durations)
        {
            lock (m_durationsMs)
            {
                for (int i = 0; i < m_remoteStepCount; i++)
                {
                    durations[m_remoteSteps[i].Step] += m_remoteSteps[i].QueueDurationMs;
                }
            }
        }

        internal void AddPipBuildRequestQueueDurationsTo(IList<long> durations)
        {
            lock (m_durationsMs)
            {
                for (int i = 0; i < m_remoteStepCount; i++)
                {
                    durations[m_remoteSteps[i].Step] += m_remoteSteps[i].RequestDurationMs;
                }
            }
        }

        internal void AddPipBuildRequestGrpcDurationsTo(IList<long> durations)
        {
            lock (m_durationsMs)
            {
                for (int i = 0; i < m_remoteStepCount; i++)
                {
                    durations[m_remoteSteps[i].Step] += m_remoteSteps[i].GrpcDurationMs;
                }
            }
        }

        #endregion
    }
}
