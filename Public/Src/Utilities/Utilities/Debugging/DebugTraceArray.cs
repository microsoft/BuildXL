// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Debugging
{
    /// <summary>
    /// Provides an indexable collection of <see cref="DebugTrace"/>, with the same behavior of having minimal overhead when initialized in a disabled state.
    /// Useful for debugging components that operate on a fixed number of elements.
    /// </summary>
    public sealed class DebugTraceArray : IDisposable
    {
        private static readonly ArrayPool<DebugTrace> s_traceArrayPool = new(1024);

        private readonly bool m_enabled;

        private static readonly DebugTrace s_disabled = new(enabled: false);

        private readonly PooledObjectWrapper<DebugTrace[]> m_logs;
        private readonly int m_count; // We should keep track of the count because the pooled arrays might be larger than it

        /// <nodoc />
        public DebugTraceArray(bool enabled, int count)
        {
            // Avoid allocations when this is not enabled
            m_enabled = enabled;
            if (!m_enabled)
            {
                return;
            }

            m_logs = s_traceArrayPool.GetInstance(count);
            m_count = count;
            for (int i = 0; i < count; i++)
            {
                m_logs.Instance[i] = new DebugTrace(enabled: true);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!m_enabled)
            {
                return;
            }

            for (int i = 0; i < m_count; i++)
            {
                m_logs.Instance[i].Dispose();
            }

            m_logs.Dispose();
        }

        /// <nodoc />
        public DebugTrace this[int i] => m_enabled ? m_logs.Instance[i] : s_disabled;
    }
}