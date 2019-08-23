// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Debugger.Tracing;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    /// Shared debugger session state, accessible both from:
    ///     (1) the debugger client thread, and
    ///     (2) the DScript evaluation threads.
    ///
    /// Synchronization discipline: implemented as a monitor:
    ///     - every method is protected with <code>this</code> used as the lock.
    ///
    /// Weakly immutable (fields are not re-assignable, but their values are mutable).
    ///
    /// Thread safe (no data races; methods are atomic).
    /// </summary>
    public sealed class DebuggerState
    {
        private readonly object m_lock = new object();

        /// <summary>Maps a thread ID to an <code cref="ThreadState"/>.</summary>
        private readonly IDictionary<int, ThreadState> m_stoppedThreads;

        private volatile bool m_debuggerStopped;

        /// <nodoc />
        public Renderer.CustomRenderer CustomRenderer { get; }

        /// <nodoc />
        public IExpressionEvaluator ExpressionEvaluator { get; }

        /// <summary>Whether the debugger has disconnected.</summary>
        public bool DebuggerStopped => m_debuggerStopped;

        /// <nodoc/>
        public PathTable PathTable { get; }

        /// <nodoc/>
        public IMasterStore MasterBreakpoints { get; }

        /// <nodoc/>
        public Logger Logger { get; }

        /// <nodoc/>
        public LoggingContext LoggingContext { get; }

        /// <nodoc/>
        public DebuggerState(
            PathTable pathTable, 
            LoggingContext loggingContext,
            Renderer.CustomRenderer customRenderer,
            IExpressionEvaluator expressionEvaluator,
            Logger logger = null)
        {
            PathTable = pathTable;
            LoggingContext = loggingContext;
            CustomRenderer = customRenderer;
            ExpressionEvaluator = expressionEvaluator;
            MasterBreakpoints = BreakpointStoreFactory.CreateMaster();
            m_stoppedThreads = new Dictionary<int, ThreadState>();
            m_debuggerStopped = false;
            Logger = logger ?? Logger.CreateLogger();
        }

        // ===========================================================================================
        // === OPERATIONS ON m_stoppedThreads ========================================================
        // ===========================================================================================

        /// <summary>
        ///     Clears all stopped threads.
        /// </summary>
        /// <returns>List of removed ThreadId -> ThreadState key value pairs.</returns>
        public IReadOnlyDictionary<int, ThreadState> ClearStoppedThreads()
        {
            lock (m_lock)
            {
                var clone = GetStoppedThreadsClone();
                m_stoppedThreads.Clear();
                return clone;
            }
        }

        /// <summary>
        ///     Returns a clone of the 'stopped threads' dictionary.
        /// </summary>
        public IReadOnlyDictionary<int, ThreadState> GetStoppedThreadsClone()
        {
            lock (m_lock)
            {
                return m_stoppedThreads.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }
        }

        /// <summary>
        ///     Returns the state for a given stopped thread id, or <code>null</code> of no such thread is found.
        /// </summary>
        public ThreadState GetThreadStateOrDefault(int threadId, ThreadState @default = null)
        {
            lock (m_lock)
            {
                ThreadState evalState;
                return m_stoppedThreads.TryGetValue(threadId, out evalState)
                    ? evalState
                    : @default;
            }
        }

        /// <summary>
        ///     Returns the state for a given stopped thread id, or throws <see cref="ThreadNotFoundException"/> of no such thread is found.
        /// </summary>
        public ThreadState GetThreadState(int threadId)
        {
            Contract.Ensures(Contract.Result<ThreadState>() != null);

            var ans = GetThreadStateOrDefault(threadId);
            if (ans == null)
            {
                throw new ThreadNotFoundException(threadId);
            }

            return ans;
        }

        /// <summary>
        ///     Removes the thread state associated with a given thread id.
        /// </summary>
        public ThreadState RemoveStoppedThread(int threadId)
        {
            lock (m_lock)
            {
                var evalState = GetThreadState(threadId);
                if (!m_stoppedThreads.Remove(threadId))
                {
                    throw new ThreadNotFoundException(threadId);
                }

                return evalState;
            }
        }

        /// <summary>
        ///     Sets the state for a given thread id.
        /// </summary>
        public void SetThreadState(ThreadState threadState)
        {
            lock (m_lock)
            {
                m_stoppedThreads[threadState.ThreadId] = threadState;
            }
        }

        /// <summary>
        ///     Stops debugging:
        ///         (1) clears all breakpoints,
        ///         (2) clears all thread states for stopped threads, and
        ///         (3) resumes all stopped threads>
        /// </summary>
        public void StopDebugging()
        {
            lock (m_lock)
            {
                m_debuggerStopped = true;
                MasterBreakpoints.Clear();
                var stoppedThreads = ClearStoppedThreads();

                foreach (var threadState in stoppedThreads.Values)
                {
                    threadState.Resume();
                }
            }
        }
    }
}
