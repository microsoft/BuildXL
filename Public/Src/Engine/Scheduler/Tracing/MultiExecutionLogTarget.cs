// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Logs execution log events to multiple execution log targets
    /// </summary>
    public class MultiExecutionLogTarget : ExecutionLogTargetBase
    {
        private IExecutionLogTarget[] m_targets;

        /// <nodoc />
        public MultiExecutionLogTarget(IExecutionLogTarget[] targets)
        {
            // Defensive clone of targets
            m_targets = targets.ToArray();
        }

        /// <summary>
        /// Adds a new execution log target. This is not thread-safe.
        /// </summary>
        internal void AddExecutionLogTarget(IExecutionLogTarget target)
        {
            Contract.Requires(target != null);
            m_targets = m_targets.ConcatAsArray(new[] { target });
        }

        /// <summary>
        /// Removes a execution log target. This is not thread-safe.
        /// </summary>
        internal void RemoveExecutionLogTarget(IExecutionLogTarget target)
        {
            Contract.Requires(target != null);
            m_targets = m_targets.Except(new[] { target }).ToArray();
        }

        /// <summary>
        /// Gets a combined execution log target for the given execution log targets
        /// </summary>
        public static MultiExecutionLogTarget CombineTargets(params IExecutionLogTarget[] targets)
        {
            targets = targets.Where(target => target != null).ToArray();
            if (targets.Length == 0)
            {
                return null;
            }

            return new MultiExecutionLogTarget(targets);
        }

        /// <nodoc />
        public override bool CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
        {
            bool canHandle = false;
            foreach (var target in m_targets)
            {
                canHandle |= target.CanHandleEvent(eventId, workerId, timestamp, eventPayloadSize);
            }

            return canHandle;
        }

        /// <inheritdoc />
        public override IExecutionLogTarget CreateWorkerTarget(uint workerId)
        {
            List<IExecutionLogTarget> workerTargets = new List<IExecutionLogTarget>();
            foreach (var target in m_targets)
            {
                var workerTarget = target?.CreateWorkerTarget(workerId);
                if (workerTarget != null)
                {
                    workerTargets.Add(workerTarget);
                }
            }

            return new MultiExecutionLogTarget(workerTargets.ToArray());
        }

        /// <inheritdoc />
        protected override void ReportUnhandledEvent<TEventData>(TEventData data)
        {
            foreach (var target in m_targets)
            {
                data.Metadata.LogToTarget(data, target);
            }
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            foreach (var target in m_targets)
            {
                target.Dispose();
            }
        }
    }
}
