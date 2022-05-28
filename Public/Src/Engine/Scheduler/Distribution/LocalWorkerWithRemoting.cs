// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Local worker with process remoting ability via AnyBuild.
    /// </summary>
    public class LocalWorkerWithRemoting : LocalWorker
    {
        private readonly SemaphoreSlim m_localExecutionSemaphore;
        private readonly int m_remotingThreshold;
        private readonly HashSet<StringId> m_processCanRunRemoteTags;
        private readonly HashSet<StringId> m_processMustRunLocalTags;
        private int m_currentToRunLocalCount = 0;
        private int m_currentRunLocalCount = 0;
        private int m_currentRunRemoteCount = 0;
        private int m_totalRunRemote = 0;
        private int m_totalRunLocally = 0;
        private int m_totalRemoteFallbackRetryLocally = 0;

        /// <summary>
        /// The number of processes currently being executed remotely.
        /// </summary>
        public int CurrentRunRemoteCount => Volatile.Read(ref m_currentRunRemoteCount);

        /// <summary>
        /// The number of processes currently being executed locally.
        /// </summary>
        public int CurrentRunLocalCount => Volatile.Read(ref m_currentRunLocalCount);

        /// <summary>
        /// Total number of processes that have been executed remotely.
        /// </summary>
        public int TotalRunRemote => Volatile.Read(ref m_totalRunRemote);

        /// <summary>
        /// Total number of processes that ran locally.
        /// </summary>
        public int TotalRunLocally => Volatile.Read(ref m_totalRunLocally);

        /// <summary>
        /// Total number of remoted processes that were retried to run locally as fallback.
        /// </summary>
        public int TotalRemoteFallbackRetryLocally => Volatile.Read(ref m_totalRemoteFallbackRetryLocally);

        /// <summary>
        /// Disable remoting when unexpected scenario (e.g., failed installing AnyBuild) happens.
        /// </summary>
        public bool DisableRemoting { get; set; } = false;

        private StringTable StringTable => PipExecutionContext.StringTable;

        private readonly ISandboxConfiguration m_sandboxConfig;

        private readonly ConcurrentStack<int> m_localThreads = new ();
        private readonly ConcurrentStack<int> m_remoteThreads = new ();

        /// <summary>
        /// Constructor.
        /// </summary>
        public LocalWorkerWithRemoting(
            IScheduleConfiguration scheduleConfig,
            ISandboxConfiguration sandboxConfig,
            IPipQueue pipQueue,
            IDetoursEventListener detoursListener,
            PipExecutionContext pipExecutionContext)
            : base(scheduleConfig, pipQueue, detoursListener, pipExecutionContext)
        {
            int localMaxProcess = scheduleConfig.MaxProcesses;
            m_localExecutionSemaphore = new SemaphoreSlim(localMaxProcess, localMaxProcess);
            m_remotingThreshold = (int)(localMaxProcess * scheduleConfig.RemotingThresholdMultiplier);
            m_processCanRunRemoteTags = scheduleConfig.ProcessCanRunRemoteTags.Select(StringTable.AddString).ToHashSet();
            m_processMustRunLocalTags = scheduleConfig.ProcessMustRunLocalTags.Select(StringTable.AddString).ToHashSet();
            m_sandboxConfig = sandboxConfig;

            for (int tid = localMaxProcess - 1; tid >= 0; --tid)
            {
                m_localThreads.Push(tid);
            }

            for (int tid = scheduleConfig.EffectiveMaxProcesses - 1; tid >= 0; --tid)
            {
                m_remoteThreads.Push(tid);
            }
        }

        /// <inheritdoc />
        public override async Task<ExecutionResult> ExecuteProcessAsync(ProcessRunnablePip processRunnable)
        {
            if (processRunnable.IsLight)
            {
                // Light process does not need remoting, and does not need a slot.
                return await base.ExecuteProcessAsync(processRunnable);
            }

            // Assume that the process is going to run locally (prefer local).
            int runLocalCount = Interlocked.Increment(ref m_currentToRunLocalCount);

            // Run local criteria:
            // - the process is forced to run locally, i.e., run location == ProcessRunLocation.Local, or
            // - the process requires an admin privilege, or
            // - the user specifies "MustRunLocal" tag, and the process has that tag, or
            // - the user specifies "CanRunRemote" tag, and the process does not have that tag, or
            // - the number of processes that are running locally (or are going to run locally)
            //   is below a threshold.
            //
            // When the process requires an admin privilege, then most likely it has to run in a VM hosted
            // by the local worker. Thus, the parallelism of running such process should be the same as running
            // the process on the local worker.
            if (DisableRemoting
                || processRunnable.RunLocation == ProcessRunLocation.Local
                || ProcessRequiresAdminPrivilege(processRunnable.Process)
                || (ExistTags(m_processMustRunLocalTags) && HasTag(processRunnable.Process, m_processMustRunLocalTags))
                || (ExistTags(m_processCanRunRemoteTags) && !HasTag(processRunnable.Process, m_processCanRunRemoteTags))
                || runLocalCount <= m_remotingThreshold)
            {
                
                await m_localExecutionSemaphore.WaitAsync();

                Interlocked.Increment(ref m_currentRunLocalCount);
                
                if (processRunnable.ExecutionResult?.RetryInfo?.RetryReason == RetryReason.RemoteFallback)
                {
                    Interlocked.Increment(ref m_totalRemoteFallbackRetryLocally);
                }

                int tid = -1;
                if (processRunnable.IncludeInTracer)
                {
                    m_localThreads.TryPop(out tid);
                    processRunnable.ThreadId = tid;
                }

                try
                {
                    return await base.ExecuteProcessAsync(processRunnable);
                }
                finally
                {
                    m_localExecutionSemaphore.Release();
                    Interlocked.Decrement(ref m_currentRunLocalCount);
                    Interlocked.Decrement(ref m_currentToRunLocalCount);
                    Interlocked.Increment(ref m_totalRunLocally);

                    if (tid != -1)
                    {
                        m_localThreads.Push(tid);
                    }
                }
            }
            else
            {
                // Retract the assumption that the process is going to run locally.
                Interlocked.Decrement(ref m_currentToRunLocalCount);

                processRunnable.RunLocation = ProcessRunLocation.Remote;
                Interlocked.Increment(ref m_currentRunRemoteCount);

                int tid = -1;
                if (processRunnable.IncludeInTracer)
                {
                    m_remoteThreads.TryPop(out tid);
                    processRunnable.ThreadId = tid;
                }

                try
                {
                    return await base.ExecuteProcessAsync(processRunnable);
                }
                finally
                {
                    Interlocked.Decrement(ref m_currentRunRemoteCount);
                    Interlocked.Increment(ref m_totalRunRemote);

                    if (tid != -1)
                    {
                        m_remoteThreads.Push(tid);
                    }
                }
            }
        }

        private bool ExistTags(HashSet<StringId> tags) => tags != null && tags.Count > 0;

        private bool HasTag(Process process, HashSet<StringId> tags) => ExistTags(tags) && process.Tags.Any(t => tags.Contains(t));

        private bool ProcessRequiresAdminPrivilege(Process process) => process.RequiresAdmin && m_sandboxConfig.AdminRequiredProcessExecutionMode.ExecuteExternal();
    }
}
