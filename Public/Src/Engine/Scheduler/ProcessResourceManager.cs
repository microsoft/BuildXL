// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Interop;
using BuildXL.Utilities.Configuration;
using BuildXL.Pips.Operations;
using BuildXL.Processes;


namespace BuildXL.Scheduler
{
    /// <summary>
    /// Restricts execution of processes when resources are constrained and allows cancelling pips to free resources.
    /// Pips are cancelled to free up required amount of RAM specified to <see cref="TryManageResources"/>.
    /// Cancellation preference is as follows:
    /// 1. Greatest RAM utilization over expected amount
    /// 2. Greatest overall RAM utilization
    /// 3. Last started first
    /// </summary>
    public sealed class ProcessResourceManager
    {
        /// <nodoc />
        public ProcessResourceManager(LoggingContext loggingContext)
        {
            m_loggingContext = loggingContext;
        }

        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Number of pips which have acquired resources and are actively running
        /// </summary>
        private int m_activeExecutionCount = 0;

        private int m_nextScopeId;

        private readonly object m_syncLock = new object();

        /// <summary>
        /// Current scopes tracking pips currently executing to allow cancellation in order to free resources
        /// </summary>
        private readonly Dictionary<PipId, ResourceScope> m_pipResourceScopes = new Dictionary<PipId, ResourceScope>();

        // Head of a double linked stack of resource scopes for pips
        // The last pip to be added is the first to be cancelled when
        // freeing resources
        private ResourceScope m_headScope;

        private int m_manageResourcesReentrancyCheck;
        private readonly List<ResourceScope> m_manageResourcesScopeList = new List<ResourceScope>();
        
        /// <summary>
        /// Comparer used to get pips in shortest running (i.e. most recently started) first order
        /// </summary>
        private static readonly IComparer<ResourceScope> s_shortestRunningTimeFirstComparer =
            Comparer<ResourceScope>.Create((s1, s2) => -s1.ScopeId.CompareTo(s2.ScopeId));

        private static readonly IComparer<ResourceScope> s_longestRunningTimeFirstComparer =
            Comparer<ResourceScope>.Create((s1, s2) => s1.ScopeId.CompareTo(s2.ScopeId));

        private static readonly IComparer<ResourceScope> s_largestWorkingSetFirstComparer =
            Comparer<ResourceScope>.Create((s1, s2) => -s1.MemoryCounters.LastWorkingSetMb.CompareTo(s2.MemoryCounters.LastWorkingSetMb));

        /// <nodoc />
        public long TotalUsedWorkingSet { get; private set; }

        /// <nodoc />
        public long TotalUsedPeakWorkingSet { get; private set; }

        /// <nodoc />
        public long TotalRamMbNeededForResume { get; private set; }

        /// <nodoc />
        public int NumSuspended { get; set; }

        /// <nodoc />
        public int NumActive { get; set; }

        /// <nodoc />
        public long LastRequiredSizeMb { get; set; }

        /// <nodoc />
        public ManageMemoryMode? LastManageMemoryMode { get; set; }

        /// <summary>
        /// Updates the ram usage indicators for all cancelable resource scopes
        /// </summary>
        public void RefreshMemoryCounters()
        {
            long totalWorkingSet = 0;
            long totalPeakWorkingSet = 0;
            long totalRamNeededForResume = 0;
            lock (m_syncLock)
            {
                foreach (var scope in m_pipResourceScopes.Values)
                {
                    if (!scope.IsSuspended)
                    {
                        // If we refresh memorycounters for suspended processes, 
                        // their average memory counters can be lower than their actual data.
                        scope.RefreshMemoryCounters();
                    }
                    else
                    {
                        totalRamNeededForResume += scope.RamMbNeededForResume;
                    }
                   
                    totalWorkingSet += scope.MemoryCounters.LastWorkingSetMb;
                    totalPeakWorkingSet += scope.MemoryCounters.PeakWorkingSetMb;
                }

                TotalUsedWorkingSet = totalWorkingSet;
                TotalUsedPeakWorkingSet = totalPeakWorkingSet;
                TotalRamMbNeededForResume = totalRamNeededForResume;
                NumSuspended = m_pipResourceScopes.Count(a => a.Value.IsSuspended);
                NumActive = m_pipResourceScopes.Count(a => !a.Value.IsSuspended);
            }
        }

        /// <summary>
        /// Attempt to manage resources by using the mode
        /// </summary>
        public void TryManageResources(int requiredSizeMb, ManageMemoryMode mode)
        {
            if (requiredSizeMb <= 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref m_manageResourcesReentrancyCheck, 1, 0) == 0)
            {
                try
                {
                    InitializeScopeList(mode);

                    ManageResourcesByPreference(mode, requiredSizeMb);
                }
                finally
                {
                    m_manageResourcesReentrancyCheck = 0;
                }
            }

            LastRequiredSizeMb = requiredSizeMb;
            LastManageMemoryMode = mode;
        }

        private void InitializeScopeList(ManageMemoryMode mode)
        {
            m_manageResourcesScopeList.Clear();
            lock (m_syncLock)
            {
                Func<ResourceScope, bool> isEligible;
                IComparer<ResourceScope> comparer;
                switch (mode)
                {
                    case ManageMemoryMode.CancellationRam:
                    case ManageMemoryMode.CancellationCommit:
                        isEligible = (scope) => scope.CanCancel && scope.MemoryCounters.PeakWorkingSetMb != 0;
                        // When cancelling processes, we start from the processes having the shortest running time.
                        comparer = s_shortestRunningTimeFirstComparer;
                        break;

                    case ManageMemoryMode.EmptyWorkingSet:
                        isEligible = (scope) => !scope.IsSuspended && scope.MemoryCounters.LastWorkingSetMb != 0;
                        comparer = s_largestWorkingSetFirstComparer;
                        break;

                    case ManageMemoryMode.Suspend:
                        isEligible = (scope) => !scope.IsSuspended && scope.MemoryCounters.PeakWorkingSetMb != 0;
                        comparer = s_largestWorkingSetFirstComparer;
                        break;

                    case ManageMemoryMode.Resume:
                        isEligible = (scope) => scope.IsSuspended;
                        // When resuming processes, we start from the processes having the longest running time.
                        comparer = s_longestRunningTimeFirstComparer;
                        break;

                    default:
                        isEligible = null;
                        comparer = null;
                        Contract.Assert(false, $"Unknown mode: {mode}");
                        break;
                }

                foreach (var scope in m_pipResourceScopes.Values)
                {
                    if (isEligible(scope))
                    {
                        m_manageResourcesScopeList.Add(scope);
                    }
                }

                m_manageResourcesScopeList.Sort(comparer);
            }
        }

        private void ManageResourcesByPreference(ManageMemoryMode mode, int requiredSize)
        {
            // Resume and EmptyWorkingSet can be called for all items in the list.
            // However, cancellation and suspend should keep one item untouchable to keep the scheduler progressing.
            int allowedCount = (mode == ManageMemoryMode.Resume || mode == ManageMemoryMode.EmptyWorkingSet) ?
                m_manageResourcesScopeList.Count :
                m_manageResourcesScopeList.Count - 1;

            for (int i = 0; i < m_manageResourcesScopeList.Count && requiredSize > 0 && allowedCount > 0; i++)
            {
                var scope = m_manageResourcesScopeList[i];

                int sizeMb = 0;

                switch (mode)
                {
                    case ManageMemoryMode.CancellationRam:
                    case ManageMemoryMode.CancellationCommit:
                    {
                        sizeMb = mode == ManageMemoryMode.CancellationRam ? scope.MemoryCounters.LastWorkingSetMb : scope.MemoryCounters.LastCommitSizeMb;

                        scope.CancelAsync(ResourceScopeCancellationReason.ResourceLimits).Forget();
                        break;
                    }

                    case ManageMemoryMode.EmptyWorkingSet:
                    case ManageMemoryMode.Suspend:
                    {
                        sizeMb = scope.EmptyWorkingSet(m_loggingContext, mode == ManageMemoryMode.Suspend);
                        break;
                    }

                    case ManageMemoryMode.Resume:
                    {
                        if (scope.RamMbNeededForResume < requiredSize)
                        {
                            sizeMb = scope.ResumeProcess(m_loggingContext);
                        }

                        break;
                    }
                }

                requiredSize -= sizeMb;
                allowedCount--;
            }
        }

        /// <summary>
        /// Attempts to execute the given function for the pip after acquiring the resources to run the pip and
        /// provides cancellation when resources are limited
        /// </summary>
        public async Task<T> ExecuteWithResourcesAsync<T>(
            OperationContext operationContext,
            Process processPip,
            ProcessMemoryCounters expectedMemoryCounters,
            bool allowCancellation,
            ManagedResourceExecute<T> execute)
        {
            ResourceScope scope;
            using (operationContext.StartOperation(PipExecutorCounter.AcquireResourcesDuration))
            {
                scope = AcquireResourceScope(processPip, expectedMemoryCounters, allowCancellation);
            }

            using (scope)
            {
                var result = await execute(scope);
                scope.Complete();
                return result;
            }
        }

        private ResourceScope AcquireResourceScope(Process processPip, ProcessMemoryCounters expectedMemoryCounters, bool allowCancellation)
        {
            Interlocked.Increment(ref m_activeExecutionCount);

            lock (m_syncLock)
            {
                var scope = new ResourceScope(this, processPip, m_nextScopeId++, expectedMemoryCounters, allowCancellation, m_headScope);
                m_headScope = scope;
                m_pipResourceScopes[processPip.PipId] = scope;
                return scope;
            }
        }

        private void RemoveScope(PipId pipId)
        {
            lock (m_syncLock)
            {
                ResourceScope scope;
                if (m_pipResourceScopes.TryGetValue(pipId, out scope))
                {
                    m_pipResourceScopes.Remove(pipId);

                    if (scope == m_headScope)
                    {
                        m_headScope = scope.Next;
                    }

                    if (scope.Prior != null)
                    {
                        scope.Prior.Next = scope.Next;
                    }

                    if (scope.Next != null)
                    {
                        scope.Next.Prior = scope.Prior;
                    }

                    scope.Next = null;
                    scope.Prior = null;
                }
            }
        }

        private void FreeExecution(PipId pipId)
        {
            Interlocked.Decrement(ref m_activeExecutionCount);

            RemoveScope(pipId);
        }

        /// <summary>
        /// Delegate for executing a pip only when resources are available and allowing the pip to be cancelled
        /// if resource thresholds are exceeded. Further, the pip should register a
        /// </summary>
        public delegate Task<T> ManagedResourceExecute<T>(ResourceScope resourceScope);

        /// <summary>
        /// Tracks an executing process and provides a cancellation token source to cancel the execution of the process.
        /// Also, maintains a doubly-linked stack so the last inserted process can be canceled first and processes
        /// which complete can be removed without having an O(N) update operation.
        /// </summary>
        public sealed class ResourceScope : IDisposable
        {
            private readonly CancellationTokenSource m_cancellation = new CancellationTokenSource();
            private readonly ProcessResourceManager m_resourceManager;
            private readonly bool m_allowCancellation;
            private bool m_isDisposed;
            private Func<ProcessMemoryCountersSnapshot> m_queryMemoryCounters;
            private Func<bool, EmptyWorkingSetResult> m_emptyWorkingSet;
            private Func<bool> m_resumeProcess;
            private bool m_completed;
            private bool m_cancelled;
            private readonly object m_refreshLock = new object();

            /// <nodoc />
            public readonly PipId PipId;

            /// <nodoc />
            public readonly string PipSemiStableHash;

            /// <nodoc />
            public readonly ProcessMemoryCounters ExpectedMemoryCounters;

            /// <nodoc />
            public readonly int ScopeId;

            /// <nodoc />
            public ResourceScope Next;

            /// <nodoc />
            public ResourceScope Prior;

            /// <nodoc />
            public bool CanCancel => !m_cancelled && m_allowCancellation;

            /// <nodoc />
            public long SuspendedDurationMs;

            private bool m_isSuspended;
            private DateTime m_suspendStartTime;

            /// <nodoc />
            public bool IsSuspended
            {
                get => m_isSuspended;
                set 
                {
                    if (value)
                    {
                        Contract.Assert(!m_isSuspended, "Scope cannot be suspended if it is not active");

                        m_suspendStartTime = DateTime.UtcNow;
                    }
                    else
                    {
                        Contract.Assert(m_isSuspended, "Scope cannot be resumed if it is not suspended");

                        SuspendedDurationMs += (long)(DateTime.UtcNow - m_suspendStartTime).TotalMilliseconds;
                    }

                    m_isSuspended = value;
                }
            
            }

            /// <summary>
            /// Indicate why we cancelled the resource scope
            /// </summary>
            public ResourceScopeCancellationReason? CancellationReason;

            /// <summary>
            /// Memory needed to resume the suspended scope
            /// </summary>
            public int RamMbNeededForResume;

            /// <summary>
            /// Captured value for RAM usage in megabytes. Updated by <see cref="RefreshMemoryCounters"/>.
            /// </summary>
            /// <remarks>
            /// This value is captured rather than requeried so that sort functions
            /// consume a consistent value
            /// </remarks>
            public ProcessMemoryCountersSnapshot MemoryCounters;

            /// <summary>
            /// StartTime
            /// </summary>
            public DateTime StartTime;

            /// <nodoc />
            public ResourceScope(ProcessResourceManager resourceManager, Process processPip, int scopeId, ProcessMemoryCounters expectedMemoryCounters, bool allowCancellation, ResourceScope next)
            {
                m_resourceManager = resourceManager;
                PipId = processPip.PipId;
                PipSemiStableHash = processPip.FormattedSemiStableHash;
                ScopeId = scopeId;
                ExpectedMemoryCounters = expectedMemoryCounters;
                m_allowCancellation = allowCancellation;
                Next = next;
                if (next != null)
                {
                    next.Prior = this;
                }

                StartTime = DateTime.UtcNow;
            }

            /// <nodoc />
            public CancellationToken Token => m_cancellation.Token;

            /// <nodoc />
            public void RegisterQueryRamUsageMb(Func<ProcessMemoryCountersSnapshot> queryMemoryCounters)
            {
                m_queryMemoryCounters = queryMemoryCounters;
            }

            /// <nodoc />
            public ProcessMemoryCountersSnapshot QueryMemoryCounters()
            {
                lock (m_refreshLock)
                {
                    return m_queryMemoryCounters?.Invoke() ?? default(ProcessMemoryCountersSnapshot);
                }
            }

            /// <nodoc />
            public void RegisterEmptyWorkingSet(Func<bool, EmptyWorkingSetResult> emptyWorkingSet)
            {
                m_emptyWorkingSet = emptyWorkingSet;
            }

            /// <nodoc />
            public void RegisterResumeProcess(Func<bool> resumeProcess)
            {
                m_resumeProcess = resumeProcess;
            }

            /// <nodoc />
            public int EmptyWorkingSet(LoggingContext loggingContext, bool isSuspend)
            {
                Contract.Requires(m_emptyWorkingSet != null, "Scope has called EmptyWorkingSet which was not registered.");

                var memoryCountersBefore = MemoryCounters;
                EmptyWorkingSetResult result = m_emptyWorkingSet.Invoke(isSuspend); 
                
                RefreshMemoryCounters();
                int workingSetAfter = MemoryCounters.LastWorkingSetMb;
                int savedSizeMb = memoryCountersBefore.LastWorkingSetMb - workingSetAfter;

                Tracing.Logger.Log.EmptyWorkingSet(loggingContext, PipSemiStableHash, isSuspend, result.ToString(), ExpectedMemoryCounters.PeakWorkingSetMb, ExpectedMemoryCounters.AverageWorkingSetMb, memoryCountersBefore.PeakWorkingSetMb, memoryCountersBefore.LastWorkingSetMb, memoryCountersBefore.AverageWorkingSetMb, memoryCountersBefore.LastCommitSizeMb, workingSetAfter);

                if (isSuspend)
                {
                    if (result.HasFlag(EmptyWorkingSetResult.SuspendFailed))
                    {
                        CancelAsync(ResourceScopeCancellationReason.SuspendFailure).Forget();
                    }
                    else if (result != EmptyWorkingSetResult.None) // None means that Suspend is not called because process is already over.      
                    {
                        IsSuspended = true;

                        // If UseAverageCountersForResume is enabled, use the average memory counters to require for resume.
                        RamMbNeededForResume = EngineEnvironmentSettings.DisableUseAverageCountersForResume ? memoryCountersBefore.LastWorkingSetMb : memoryCountersBefore.AverageWorkingSetMb;
                    }
                }

                return savedSizeMb;
            }

            /// <nodoc />
            public int ResumeProcess(LoggingContext loggingContext)
            {
                Contract.Requires(m_resumeProcess != null, "Scope has called ResumeProcess which was not registered.");

                int workingSetBefore = MemoryCounters.LastWorkingSetMb;
                int commitSizeBefore = MemoryCounters.LastCommitSizeMb;

                bool result = m_resumeProcess.Invoke();

                Tracing.Logger.Log.ResumeProcess(loggingContext, PipSemiStableHash, result, workingSetBefore, commitSizeBefore, RamMbNeededForResume);

                if (!result)
                {
                    CancelAsync(ResourceScopeCancellationReason.ResumeFailure).Forget();
                    return 0;
                }

                IsSuspended = false;
                return RamMbNeededForResume;
            }

            /// <nodoc />
            public void RefreshMemoryCounters()
            {
                MemoryCounters = QueryMemoryCounters();
            }

            /// <nodoc />
            public async Task CancelAsync(ResourceScopeCancellationReason cancellationReason)
            {
                try
                {
                    m_resourceManager.RemoveScope(PipId);
                    CancellationReason = cancellationReason;
                    m_cancelled = true;
                    if (!m_isDisposed)
                    {
                        await Task.Run(() => m_cancellation.Cancel());
                    }
                }
                catch (ObjectDisposedException)
                {
                    // It's ok if dispose beats us because that
                    // means the operation is already complete
                }
            }

            /// <summary>
            /// Called when process successfully completes (i.e. no <see cref="OperationCanceledException"/> is thrown to indicate cancellation)
            /// </summary>
            public void Complete()
            {
                m_completed = true;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                lock(m_refreshLock)
                {
                    Contract.Assert(!m_completed || m_queryMemoryCounters != null, "Must register query memory counters before completion");
                    m_queryMemoryCounters = null;
                    m_isDisposed = true;
                }

                m_cancellation.Dispose();
                m_resourceManager.FreeExecution(PipId);
            }
        }

        /// <summary>
        /// Type to indicate why ResourceScope has cancelled
        /// </summary>
        public enum ResourceScopeCancellationReason
        { 
            /// <summary>
            /// Cancelled because scope exceeded the resource limits.
            /// </summary>
            ResourceLimits,

            /// <summary>
            /// Cancelled because we suspended some of the threads, but not all.
            /// </summary>
            SuspendFailure,

            /// <summary>
            /// Cancelled because we resumed some of the threads, but not all.
            /// </summary>
            ResumeFailure
        }
    }
}
