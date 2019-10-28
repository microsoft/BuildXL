// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Restricts execution of processes when resources are constrained and allows cancelling pips to free resources.
    /// Pips are cancelled to free up required amount of RAM specified to <see cref="TryFreeResources"/>.
    /// Cancellation preference is as follows:
    /// 1. Greatest RAM utilization over expected amount
    /// 2. Greatest overall RAM utilization
    /// 3. Last started first
    /// </summary>
    public sealed class ProcessResourceManager
    {
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

        private int m_freeResourcesReentrancyCheck;
        private readonly List<ResourceScope> m_freeResourcesScopeList = new List<ResourceScope>();

        // NOTE: We negate the comparison result with RAM comparisons for the comparers to get greatest first

        /// <summary>
        /// Comparer used to get pips in shortest running (i.e. most recently started) first order
        /// </summary>
        private static readonly IComparer<ResourceScope> s_shortestRunningTimeFirstComparer =
            Comparer<ResourceScope>.Create((s1, s2) => -s1.ScopeId.CompareTo(s2.ScopeId));

        /// <summary>
        /// Attempt to free resoures by cancelling the last pip to be executed.
        /// </summary>
        public void TryFreeResources(int requiredRamMb)
        {
            var pendingExecutionCount = Volatile.Read(ref m_activeExecutionCount);
            if (pendingExecutionCount > 1)
            {
                if (Interlocked.CompareExchange(ref m_freeResourcesReentrancyCheck, 1, 0) == 0)
                {
                    try
                    {
                        m_freeResourcesScopeList.Clear();
                        lock (m_syncLock)
                        {
                            foreach (var scope in m_pipResourceScopes.Values)
                            {
                                if (scope.CanCancel)
                                {
                                    m_freeResourcesScopeList.Add(scope);
                                }
                            }
                        }

                        // Refresh the RAM usage
                        foreach (var scope in m_freeResourcesScopeList)
                        {
                            scope.RefreshRamUsage();
                        }

                        // Allow all but one pip to be cancelled
                        int allowedCancelCount = m_freeResourcesScopeList.Count - 1;

                        // Kill pips which started most recently first order
                        FreeResourcesByPreference(s_shortestRunningTimeFirstComparer, ref requiredRamMb, ref allowedCancelCount);
                    }
                    finally
                    {
                        m_freeResourcesReentrancyCheck = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Updates the ram usage indicators for all cancelable resource scopes
        /// </summary>
        public void UpdateRamUsageForResourceScopes()
        {
            lock (m_syncLock)
            {
                foreach (var scope in m_pipResourceScopes.Values)
                {
                    if (scope.CanCancel)
                    {
                        scope.RefreshRamUsage();
                    }
                }
            }
        }

        private void FreeResourcesByPreference(
            IComparer<ResourceScope> scopeComparer,
            ref int requiredRam,
            ref int allowedCancelCount,
            bool freeByOverage = false)
        {
            m_freeResourcesScopeList.Sort(scopeComparer);
            for (int i = 0; i < m_freeResourcesScopeList.Count && requiredRam > 0; i++)
            {
                if (allowedCancelCount == 0)
                {
                    // We have canceled all the pips possible, return
                    return;
                }

                var scope = m_freeResourcesScopeList[i];
                if (scope.CanCancel && (!freeByOverage || scope.RamUsageOverageMb > 0))
                {
                    RemoveScope(scope.PipId);
                    scope.Cancel().Forget();
                    requiredRam -= freeByOverage ? scope.RamUsageOverageMb : scope.RamUsageMb;
                    allowedCancelCount--;
                }
            }
        }

        /// <summary>
        /// Attempts to execute the given function for the pip after acquiring the resources to run the pip and
        /// provides cancellation when resources are limited
        /// </summary>
        public async Task<T> ExecuteWithResources<T>(
            OperationContext operationContext,
            PipId pipId,
            int expectedRamUsageMb,
            bool allowCancellation,
            ManagedResourceExecute<T> execute)
        {
            ResourceScope scope;
            using (operationContext.StartOperation(PipExecutorCounter.AcquireResourcesDuration))
            {
                scope = AcquireResourceScope(pipId, expectedRamUsageMb, allowCancellation);
            }

            using (scope)
            {
                var result = await execute(scope.Token, registerQueryRamUsageMb: scope.RegisterQueryRamUsageMb);
                scope.Complete();
                return result;
            }
        }

        private ResourceScope AcquireResourceScope(PipId pipId, int expectedRamUsageMb, bool allowCancellation)
        {
            Interlocked.Increment(ref m_activeExecutionCount);

            lock (m_syncLock)
            {
                var scope = new ResourceScope(this, pipId, m_nextScopeId++, expectedRamUsageMb, allowCancellation, m_headScope);
                m_headScope = scope;
                m_pipResourceScopes[pipId] = scope;
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

        private void FreeExecution(PipId pipId = default(PipId))
        {
            Interlocked.Decrement(ref m_activeExecutionCount);

            if (pipId.IsValid)
            {
                RemoveScope(pipId);
            }
        }

        /// <summary>
        /// Delegate for executing a pip only when resources are available and allowing the pip to be cancelled
        /// if resource thresholds are exceeded. Further, the pip should register a
        /// </summary>
        /// <typeparam name="T">the result type</typeparam>
        /// <param name="cancellationToken">cancellation token for cancelling the process execution</param>
        /// <param name="registerQueryRamUsageMb">delegate which is called with function which can be used to observe resource usage</param>
        /// <returns>the result of the execution</returns>
        public delegate Task<T> ManagedResourceExecute<T>(
            CancellationToken cancellationToken,
            Action<Func<int>> registerQueryRamUsageMb);

        /// <summary>
        /// Tracks an executing process and provides a cancellation token source to cancel the execution of the process.
        /// Also, maintains a doubly-linked stack so the last inserted process can be canceled first and processes
        /// which complete can be removed without having an O(N) update operation.
        /// </summary>
        private sealed class ResourceScope : IDisposable
        {
            private readonly CancellationTokenSource m_cancellation = new CancellationTokenSource();
            private readonly ProcessResourceManager m_resourceManager;
            private readonly bool m_allowCancellation;
            private bool m_isDisposed;
            private Func<int> m_queryRamUsageMb;
            private bool m_completed;
            private bool m_cancelled;

            public readonly PipId PipId;
            public readonly int ExpectedRamUsageMb;
            public readonly int ScopeId;
            public ResourceScope Next;
            public ResourceScope Prior;

            public bool CanCancel => !m_cancelled && m_allowCancellation;

            /// <summary>
            /// Captured value for RAM usage in megabytes. Updated by <see cref="RefreshRamUsage"/>.
            /// </summary>
            /// <remarks>
            /// This value is captured rather than requeried so that sort functions
            /// consume a consistent value
            /// </remarks>
            public int RamUsageMb { get; private set; }

            public int RamUsageOverageMb => RamUsageMb - ExpectedRamUsageMb;

            public ResourceScope(ProcessResourceManager resourceManager, PipId pipId, int scopeId, int expectedRamUsageMb, bool allowCancellation, ResourceScope next)
            {
                m_resourceManager = resourceManager;
                PipId = pipId;
                ScopeId = scopeId;
                ExpectedRamUsageMb = expectedRamUsageMb;
                m_allowCancellation = allowCancellation;
                Next = next;
                if (next != null)
                {
                    next.Prior = this;
                }
            }

            public CancellationToken Token => m_cancellation.Token;

            public void RegisterQueryRamUsageMb(Func<int> queryRamUsageMb)
            {
                m_queryRamUsageMb = queryRamUsageMb;
            }

            public int QueryRamUsageMb()
            {
                if (!m_cancellation.IsCancellationRequested && !m_cancelled && !m_completed && !m_isDisposed)
                {
                    return m_queryRamUsageMb?.Invoke() ?? 0;
                }

                return 0;
            }

            public void RefreshRamUsage()
            {
                RamUsageMb = QueryRamUsageMb();
            }

            public async Task Cancel()
            {
                try
                {
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

            public void Dispose()
            {
                Contract.Assert(!m_completed || m_queryRamUsageMb != null, "Must register query ram usage before completion");
                m_queryRamUsageMb = null;
                m_isDisposed = true;
                m_cancellation.Dispose();
                m_resourceManager.FreeExecution(PipId);
            }
        }
    }
}
