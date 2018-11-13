// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Native.Streams
{
    /// <summary>
    /// Disposable scope for <see cref="IIOCompletionManager.StartTracingCompletion"/>.
    /// </summary>
    public sealed class IOCompletionTraceHook : IDisposable
    {
        private readonly IIOCompletionManager m_manager;
        private readonly Dictionary<ulong, TraceInfo> m_active = new Dictionary<ulong, TraceInfo>();

        internal IOCompletionTraceHook(IIOCompletionManager manager)
        {
            m_manager = manager;
        }

        internal void TraceStart(ulong id, IIOCompletionTarget target)
        {
            var info = new TraceInfo
                        {
                            Stack = new StackTrace(fNeedFileInfo: true),
                            Target = target,
                        };

            lock (m_active)
            {
                m_active.Add(id, info);
            }
        }

        internal void TraceComplete(ulong id)
        {
            lock (m_active)
            {
                // We allow tracing to start with I/O in progress, so we may see completions without starts.
                Analysis.IgnoreResult(m_active.Remove(id));
            }
        }

        /// <summary>
        /// Verifies that all I/O initiated within this trace scope has been completed.
        /// Throws an exception if not.
        /// </summary>
        public void AssertTracedIOCompleted()
        {
            lock (m_active)
            {
                if (m_active.Count == 0)
                {
                    return;
                }

                TraceInfo firstIncomplete = m_active.First().Value;
                throw new InvalidOperationException(
                    I($"One or more I/O operations have not been completed. Details for one such operation follow. Completion target {firstIncomplete.Target}. Stack: {firstIncomplete.Stack}"));
            }
        }

        /// <nodoc />
        public void Dispose()
        {
            IOCompletionTraceHook uninstalled = m_manager.RemoveTraceHook();
            Contract.Assume(uninstalled == this, "Incorrect disposal of a trace hook");
        }

        private sealed class TraceInfo
        {
            public StackTrace Stack;
            public IIOCompletionTarget Target;
        }
    }
}
