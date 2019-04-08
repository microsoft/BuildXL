// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Tracing
{
    /// <summary>
    /// Structure for timing the duration of actions and logging the result
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct TimedBlock<TStartObject, TEndObject> : IDisposable
        where TEndObject : IHasEndTime
        where TStartObject : struct
    {
        /// <summary>
        /// Context of this event
        /// </summary>
        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Action to perform at the end
        /// </summary>
        private readonly Action<LoggingContext, TEndObject> m_endAction;

        /// <summary>
        /// Gets the struct to be used in the end log action.
        /// </summary>
        private readonly Func<TEndObject> m_endObjGetter;

        /// <summary>
        /// Times the block
        /// </summary>
        private readonly Stopwatch m_stopwatch;

        private readonly PerformanceCollector.Aggregator m_aggregator;

        /// <summary>
        /// Whether this has been disposed
        /// </summary>
        private bool m_isDisposed;

        /// <summary>
        /// Private constructor to enforce creation to go through the Start() method
        /// </summary>
        private TimedBlock(
            LoggingContext parentLoggingContext,
            PerformanceCollector.Aggregator aggregator,
            string phaseFriendlyName,
            Action<LoggingContext, TEndObject> endAction,
            Func<TEndObject> endObjGetter)
        {
            Contract.Requires(parentLoggingContext != null);
            Contract.Requires(endAction != null);
            Contract.Requires(endObjGetter != null);

            m_loggingContext = new LoggingContext(parentLoggingContext, phaseFriendlyName);
            m_aggregator = aggregator;
            m_endAction = endAction;
            m_endObjGetter = endObjGetter;
            m_stopwatch = Stopwatch.StartNew();
            m_isDisposed = false;
        }

        /// <summary>
        /// returns the ElapsedTime for the block
        /// </summary>
        public TimeSpan ElapsedTime => m_stopwatch.Elapsed;

        /// <summary>
        /// Return the logging context created by this block
        /// </summary>
        public LoggingContext LoggingContext => m_loggingContext;

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
            TEndObject endObj = m_endObjGetter();
            m_stopwatch.Stop();
            endObj.ElapsedMilliseconds = (int)m_stopwatch.ElapsedMilliseconds;
            m_endAction(m_loggingContext, endObj);

            if (!string.IsNullOrWhiteSpace(m_loggingContext.LoggerComponentInfo))
            {
                LoggingHelpers.LogCategorizedStatistic(m_loggingContext, m_loggingContext.LoggerComponentInfo, Statistics.DurationMs, (int)m_stopwatch.ElapsedMilliseconds);
            }

            if (m_aggregator != null)
            {
                LoggingHelpers.LogPerformanceCollector(m_aggregator, m_loggingContext, m_loggingContext.LoggerComponentInfo);
                m_aggregator.Dispose();
            }

            m_isDisposed = true;
        }

        /// <summary>
        /// Starts a new timed block that calls <paramref name="endAction"/> upon completion
        /// </summary>
        /// <param name="parentLoggingContext">Context for logging</param>
        /// <param name="startAction">Action to perform at the start. May set to null if no action desired</param>
        /// <param name="startObject">Start object provider to startAction</param>
        /// <param name="endAction">Action to perform at the end.</param>
        /// <param name="endObjGetter">Func to get the end object. This is not passed by value to allow the object to
        /// be changed within the TimedBlock</param>
        public static TimedBlock<TStartObject, TEndObject> StartWithoutStatistic(
            LoggingContext parentLoggingContext,
            Action<LoggingContext, TStartObject> startAction,
            TStartObject startObject,
            Action<LoggingContext, TEndObject> endAction,
            Func<TEndObject> endObjGetter)
        {
            // There's no point is using this structure if the end action and struct aren't set
            Contract.Requires(parentLoggingContext != null);
            Contract.Requires(endAction != null);
            Contract.Requires(endObjGetter != null);
            Contract.Requires(startAction != null);

            return Start(parentLoggingContext, null, null, startAction, startObject, endAction, endObjGetter);
        }

        /// <summary>
        /// Starts a new timed block that calls <paramref name="endAction"/> and logs a statistic upon completion
        /// </summary>
        /// <param name="parentLoggingContext">Context for logging</param>
        /// <param name="phaseFriendlyName">Name for the phase being measured</param>
        /// <param name="startAction">Action to perform at the start. May set to null if no action desired</param>
        /// <param name="startObject">Start object provider to startAction</param>
        /// <param name="endAction">Action to perform at the end.</param>
        /// <param name="endObjGetter">Func to get the end object. This is not passed by value to allow the object to
        /// be changed within the TimedBlock</param>
        public static TimedBlock<TStartObject, TEndObject> Start(
            LoggingContext parentLoggingContext,
            string phaseFriendlyName,
            Action<LoggingContext, TStartObject> startAction,
            TStartObject startObject,
            Action<LoggingContext, TEndObject> endAction,
            Func<TEndObject> endObjGetter)
        {
            // There's no point is using this structure if the end action and struct aren't set
            Contract.Requires(parentLoggingContext != null);
            Contract.Requires(endAction != null);
            Contract.Requires(endObjGetter != null);
            Contract.Requires(startAction != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(phaseFriendlyName));

            return Start(parentLoggingContext, null, phaseFriendlyName, startAction, startObject, endAction, endObjGetter);
        }

        /// <summary>
        /// Starts a new timed block that calls <paramref name="endAction"/>, logs a statistic, and performance data upon completion
        /// </summary>
        /// <param name="parentLoggingContext">Context for logging</param>
        /// <param name="collector">Perf counter collector</param>
        /// <param name="phaseFriendlyName">Friendly name for what is being measured. This will be used in logging</param>
        /// <param name="startAction">Action to perform at the start. May set to null if no action desired</param>
        /// <param name="startObject">Start object provider to startAction</param>
        /// <param name="endAction">Action to perform at the end.</param>
        /// <param name="endObjGetter">Func to get the end object. This is not passed by value to allow the object to
        /// be changed within the TimedBlock</param>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters",
            Justification = "We specifically want a PerfCounterCollector so its aggregator returns the appropriate type")]
        public static TimedBlock<TStartObject, TEndObject> Start(
            LoggingContext parentLoggingContext,
            PerformanceCollector collector,
            string phaseFriendlyName,
            Action<LoggingContext, TStartObject> startAction,
            TStartObject startObject,
            Action<LoggingContext, TEndObject> endAction,
            Func<TEndObject> endObjGetter)
        {
            // There's no point is using this structure if the end action and struct aren't set
            Contract.Requires(parentLoggingContext != null);
            Contract.Requires(endAction != null);
            Contract.Requires(endObjGetter != null);
            Contract.Requires(startAction != null);
            Contract.Requires(collector == null || !string.IsNullOrWhiteSpace(phaseFriendlyName));

            startAction(parentLoggingContext, startObject);

            return new TimedBlock<TStartObject, TEndObject>(
                parentLoggingContext,
                collector != null ? collector.CreateAggregator() : null,
                phaseFriendlyName,
                endAction,
                endObjGetter);
        }
    }
}
