// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Tracing
{
    /// <summary>
    /// Structure for logging performance measurement.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct PerformanceMeasurement : IDisposable
    {
        /// <summary>
        /// Logging Context valid during this block.
        /// </summary>
        public readonly LoggingContext LoggingContext;

        /// <summary>
        /// Action called on ending the performance measurement.
        /// </summary>
        /// <remarks>
        /// An end action requires to take the root and current trace id as inputs.
        /// Moreover, it takes the amount of elapsed ticks as an additional input.
        /// </remarks>
        private readonly Action<LoggingContext> m_endAction;

        /// <summary>
        /// Internal stopwatch.
        /// </summary>
        private readonly Stopwatch m_stopwatch;

        /// <summary>
        /// Flag indicating if this perf measurment has been disposed.
        /// </summary>
        private bool m_isDisposed;

        private readonly PerformanceCollector.Aggregator m_aggregator;

        /// <summary>
        /// Struct initializer.
        /// </summary>
        private PerformanceMeasurement(
            LoggingContext parentLoggingContext,
            PerformanceCollector.Aggregator aggregator,
            string phaseFriendlyName,
            Action<LoggingContext> endAction)
        {
            Contract.Requires(parentLoggingContext != null);
            Contract.Requires(endAction != null);

            LoggingContext = new LoggingContext(parentLoggingContext, phaseFriendlyName);
            m_aggregator = aggregator;
            m_endAction = endAction;

            if (!string.IsNullOrWhiteSpace(phaseFriendlyName))
            {
                m_stopwatch = new Stopwatch();
                m_stopwatch.Start();
            }
            else
            {
                m_stopwatch = null;
            }

            m_isDisposed = false;
        }

        /// <summary>
        /// Checks if performance measurement has been disposed.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public bool IsDisposed
        {
            get { return m_isDisposed; }
            private set { m_isDisposed = value; }
        }

        /// <summary>
        /// Disposes performance measurement.
        /// </summary>
        public void Dispose()
        {
            if (m_stopwatch != null)
            {
                m_stopwatch.Stop();
                LoggingHelpers.LogCategorizedStatistic(LoggingContext, LoggingContext.LoggerComponentInfo, Statistics.DurationMs, (int)m_stopwatch.ElapsedMilliseconds);
            }

            if (m_aggregator != null)
            {
                LoggingHelpers.LogPerformanceCollector(m_aggregator, LoggingContext, LoggingContext.LoggerComponentInfo);
                m_aggregator.Dispose();
            }

            m_endAction(LoggingContext);

            m_isDisposed = true;
        }

        /// <summary>
        /// Starts performance measurement that does not log a Statistic upon completion.
        /// This is the version using the old Events.Log class.
        /// </summary>
        public static PerformanceMeasurement StartWithoutStatistic(
            LoggingContext parentLoggingContext,
            Action<Guid> startAction,
            Action endAction)
        {
            Contract.Requires(parentLoggingContext != null);
            Contract.Requires(startAction != null);
            Contract.Requires(endAction != null);

            return Start(
                parentLoggingContext,
                null,
                null,
                startAction,
                endAction);
        }

        /// <summary>
        /// Starts performance measurement that does not log a Statistic upon completion.
        /// </summary>
        public static PerformanceMeasurement StartWithoutStatistic(
            LoggingContext parentLoggingContext,
            Action<LoggingContext> startAction,
            Action<LoggingContext> endAction)
        {
            Contract.Requires(parentLoggingContext != null);
            Contract.Requires(startAction != null);
            Contract.Requires(endAction != null);

            return Start(
                parentLoggingContext,
                null,
                null,
                startAction,
                endAction);
        }

        /// <summary>
        /// Starts performance measurement.
        /// This is the version using the old Events.Log class.
        /// </summary>
        public static PerformanceMeasurement Start(
            LoggingContext parentLoggingContext,
            string phaseFriendlyName,
            Action<Guid> startAction,
            Action endAction)
        {
            Contract.Requires(parentLoggingContext != null);
            Contract.Requires(startAction != null);
            Contract.Requires(endAction != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(phaseFriendlyName));

            return Start(
                parentLoggingContext,
                null,
                phaseFriendlyName,
                startAction,
                endAction);
        }

        /// <summary>
        /// Starts performance measurement.
        /// </summary>
        public static PerformanceMeasurement Start(
            LoggingContext parentLoggingContext,
            string phaseFriendlyName,
            Action<LoggingContext> startAction,
            Action<LoggingContext> endAction)
        {
            Contract.Requires(parentLoggingContext != null);
            Contract.Requires(startAction != null);
            Contract.Requires(endAction != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(phaseFriendlyName));

            return Start(
                parentLoggingContext,
                null,
                phaseFriendlyName,
                startAction,
                endAction);
        }

        /// <summary>
        /// Starts performance measurement that collects and logs performance counters for the time window
        /// This is the version using the old Events.Log class.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters",
            Justification = "We specifically want a PerfCounterCollector so its aggregator returns the appropriate type")]
        public static PerformanceMeasurement Start(
            LoggingContext parentLoggingContext,
            PerformanceCollector collector,
            string phaseFriendlyName,
            Action<Guid> startAction,
            Action endAction)
        {
            Contract.Requires(parentLoggingContext != null);
            Contract.Requires(collector == null || !string.IsNullOrWhiteSpace(phaseFriendlyName));
            Contract.Requires(startAction != null);
            Contract.Requires(endAction != null);

            return Start(
                parentLoggingContext,
                collector,
                phaseFriendlyName,
                (context) =>
                {
                    EventSource.SetCurrentThreadActivityId(context.ActivityId);
                    startAction(context.ParentActivityId);
                },
                (context) =>
                {
                    EventSource.SetCurrentThreadActivityId(context.ActivityId);
                    endAction();
                });
        }

        /// <summary>
        /// Starts performance measurement that collects and logs performance counters for the time window
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters",
            Justification = "We specifically want a PerfCounterCollector so its aggregator returns the appropriate type")]
        public static PerformanceMeasurement Start(
            LoggingContext parentLoggingContext,
            PerformanceCollector collector,
            string phaseFriendlyName,
            Action<LoggingContext> startAction,
            Action<LoggingContext> endAction)
        {
            Contract.Requires(parentLoggingContext != null);
            Contract.Requires(collector == null || !string.IsNullOrWhiteSpace(phaseFriendlyName));
            Contract.Requires(startAction != null);
            Contract.Requires(endAction != null);

            var pm = new PerformanceMeasurement(
                parentLoggingContext,
                collector != null ? collector.CreateAggregator() : null,
                phaseFriendlyName,
                endAction);

            startAction(pm.LoggingContext);

            return pm;
        }
    }
}
