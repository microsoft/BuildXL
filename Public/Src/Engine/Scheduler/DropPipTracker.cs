// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Responsible for whatever tracking of drop pips we might want to do, e.g., drop overhang.
    /// </summary>
    public sealed class DropPipTracker
    {
        private readonly PipExecutionContext m_context;
        private readonly StringId m_dropTagStringId;

        private long m_lastNonDropPipCompletionTimeTicks;
        private long m_lastDropPipCompletionTimeTicks;

        /// <nodoc/>
        public DropPipTracker(PipExecutionContext context)
        {
            m_context = context;
            m_dropTagStringId = StringId.Create(m_context.StringTable, "artifact-services-drop-pip");
        }

        /// <nodoc/>
        public void ReportPipCompleted(Pip pip)
        {
            Contract.Requires(pip != null);

            long completedAt = DateTime.UtcNow.Ticks;
            if (IsDropPip(pip))
            {
                Interlocked.Exchange(ref m_lastDropPipCompletionTimeTicks, completedAt);
            }
            else
            {
                Interlocked.Exchange(ref m_lastNonDropPipCompletionTimeTicks, completedAt);
            }
        }

        /// <summary>
        /// Time of the last completed non-drop pip.
        /// </summary>
        public DateTime LastNonDropPipCompletionTime => new DateTime(Volatile.Read(ref m_lastNonDropPipCompletionTimeTicks));

        /// <summary>
        /// Time of the last completed drop pip.
        /// </summary>
        public DateTime LastDropPipCompletionTime => new DateTime(Volatile.Read(ref m_lastDropPipCompletionTimeTicks));

        /// <summary>
        /// Difference between <see cref="LastDropPipCompletionTime"/> and <see cref="LastNonDropPipCompletionTime"/>.
        /// </summary>
        public TimeSpan DropOverhang => LastDropPipCompletionTime.Subtract(LastNonDropPipCompletionTime);

        /// <nodoc/>
        public void LogStats(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);

            if (m_lastDropPipCompletionTimeTicks > 0 && m_lastNonDropPipCompletionTimeTicks > 0)
            {
                var overhangMs = (long)DropOverhang.TotalMilliseconds;
                Logger.Log.BulkStatistic(loggingContext, new Dictionary<string, long>
                {
                    [Statistics.DropTrackerOverhangMs] = overhangMs,
                });
            }
        }

        private bool IsDropPip(Pip pip)
        {
            return pip.Tags.IsValid && pip.Tags.Any(t => t.Equals(m_dropTagStringId));
        }
    }
}
