// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using BuildXL.Utilities;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// Provides support for EventSource activities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An activity consists
    /// of one Start event, any number of normal events, and one Stop event.
    /// All events written by CacheActivity (Start, normal, and Stop)
    /// are tagged with a unique activity ID (a GUID). In addition, the Start
    /// event can optionally include the ID of a related (parent) activity,
    /// making it possible to track activity nesting.
    /// </para>
    /// <para>
    /// This class inherits from IDisposable to enable its use in using blocks,
    /// not because it owns any unmanaged resources. It is not necessarily an
    /// error to abandon an activity without calling Dispose.
    /// Calling Dispose() on an activity in the "Started" state is equivalent
    /// to calling Stop("Dispose"). Calling Dispose() on an activity in any
    /// other state is a no-op.
    /// </para>
    /// <para>
    /// This class adheres to Engineering Systems-specific requirements for
    /// propagating activity root information through the activity ID tree,
    /// thus the class name 'EventSource*Rooted*Activity'.
    /// </para>
    /// <para>
    /// Notable differences from the general-purpose EventSourceActivity class
    /// provided in Windows sources:
    /// <list type="bullet">
    /// <item><description>
    ///   The public constructor forces 'chaining' of activities off a parent
    ///   activity. Only the outer-most layer (OptionsParser) should create
    ///   a top-level activity.
    /// </description></item>
    /// <item><description>
    ///   Events must be logged through the activity instance (the contained EventSource
    ///   instance is no longer exposed) to encourage activity-associated events.
    /// </description></item>
    /// <item><description>
    ///   There are subclasses of this CacheActivity type that provide ICache API
    ///   specific support, including argument types, result logging, and dispose
    ///   behavior handling.
    /// </description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public class CacheActivity : IDisposable
    {
        /// <summary>
        /// String used as the event / activity name for indicating that Close() was not called.
        /// </summary>
        public const string DisposedEventName = "Disposed";

        // Some unique activity identification
        private readonly EventSource m_eventSource;
        private readonly string m_cacheId;
        private readonly string m_activityName;

        // Indicates that an activity may naturally terminate early w/o calling Stop.
        // For use by enumerators. An activity that terminates early and is disposed is
        // traced as "terminated early" and not as "disposed"
        private readonly bool m_mayTerminateEarly;

        // At construction time we start this timer (really just a single number stored)
        private readonly ElapsedTimer m_elapsedTimer = ElapsedTimer.StartNew();

        // The state of the activity being enabled as determined at construction
        // time based on the activity options
        private readonly bool m_enabled;

        // These could be read-only except they are passed by reference in various cases
        // This just means that they can't be marked that way as we have no deep read-only
        // references.
        private EventSourceOptions m_options;
        private Guid m_relatedId;
        private Guid m_id;

        // This mutates during use  (the state of the activity)
        private State m_state;

        #region Constants

        /// <summary>
        /// A set of options to enable verbose logging with no keywords.
        /// </summary>
        public static readonly EventSourceOptions VerboseOptions = new EventSourceOptions() { Level = EventLevel.Verbose };

        /// <summary>
        /// Options to trace parameters for each method.
        /// </summary>
        public static readonly EventSourceOptions ParameterOptions = new EventSourceOptions() { Level = EventLevel.Verbose, Keywords = Keywords.MethodArguments };

        /// <summary>
        /// Options to trace success return values for each method.
        /// </summary>
        public static readonly EventSourceOptions ReturnOptions = new EventSourceOptions() { Level = EventLevel.Verbose, Keywords = Keywords.ReturnValue };

        /// <summary>
        /// Options to trace statistics
        /// </summary>
        public static readonly EventSourceOptions StatisticOptions = new EventSourceOptions() { Level = EventLevel.Verbose, Keywords = Keywords.Statistics };

        /// <summary>
        /// Options with the CriticalDataKeyword and Informational level set.
        /// </summary>
        public static readonly EventSourceOptions CriticalDataOptions = new EventSourceOptions() { Level = EventLevel.Informational, Keywords = Keywords.CriticalDataKeyword };

        /// <summary>
        /// Options with the CriticalDataKeyword and Verbose level set.
        /// </summary>
        public static readonly EventSourceOptions VerboseCriticalDataOptions = new EventSourceOptions() { Level = EventLevel.Verbose, Keywords = Keywords.CriticalDataKeyword };

        /// <summary>
        /// Options with the TelemetryKeyword and Information level set.
        /// </summary>
        public static readonly EventSourceOptions TelemetryOptions = new EventSourceOptions() { Level = EventLevel.Informational, Keywords = Keywords.TelemetryKeyword };

        #endregion Constants

        /// <summary>
        /// Initializes a new instance of the CacheActivity class that
        /// is attached to the specified event source. The new activity will
        /// be attached to the related (parent) activity as specified by
        /// relatedActivityId.
        /// The activity is created in the Initialized state. Call Start() to
        /// write the activity's Start event.
        /// </summary>
        /// <param name="eventSource">
        /// The event source to which the activity events should be written.
        /// </param>
        /// <param name="options">
        /// The options to use for the start and stop events of the activity.
        /// Note that the Opcode property will be ignored.
        /// </param>
        /// <param name="relatedActivityId">
        /// The id of the related (parent) activity to which this new activity
        /// should be attached.
        /// </param>
        /// <param name="activityName">
        /// The name of the activity being created
        /// </param>
        /// <param name="cacheId">
        /// Name of the cache this activity is in.
        /// </param>
        /// <param name="mayTerminateEarly">
        /// If set to true, the activity may terminate with dispose
        /// without Stop.  Used mainly for enumerators.
        /// </param>
        public CacheActivity(
            EventSource eventSource,
            EventSourceOptions options,
            Guid relatedActivityId,
            string activityName,
            string cacheId,
            bool mayTerminateEarly = false)
        {
            Contract.Requires(eventSource != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(activityName));
            Contract.Requires(!string.IsNullOrWhiteSpace(cacheId));

            m_cacheId = cacheId;
            m_eventSource = eventSource;
            m_activityName = activityName;
            m_relatedId = relatedActivityId;
            m_id = CreateId(m_relatedId);
            m_mayTerminateEarly = mayTerminateEarly;

            // We use this to have an activity turned on/off for its lifetime
            // during construction.  This way a turn on of activity logging
            // will not suddenly turn on the logging for an activity that had
            // not logged its start operation.
            m_enabled = m_eventSource.IsEnabled(options.Level, options.Keywords);

            // We set the op-code to stop and store that
            options.Opcode = EventOpcode.Stop;
            m_options = options;
        }

        /// <summary>
        /// Initializes a new instance of the CacheActivity class that
        /// is attached to the specified related (parent) activity.
        /// The activity is created in the Initialized state. Call Start() to
        /// write the activity's Start event.
        /// </summary>
        /// <param name="activityName">
        /// Name for this activity.
        /// </param>
        /// <param name="relatedActivity">
        /// The related (parent) activity. Activity events will be written
        /// to the event source attached to this activity.
        /// </param>
        /// <param name="mayTerminateEarly">
        /// If set to true, the activity may terminate with dispose
        /// without Stop.  Used for enumerators.
        /// </param>
        public CacheActivity(string activityName, CacheActivity relatedActivity, bool mayTerminateEarly = false)
            : this(
                relatedActivity.m_eventSource,
                relatedActivity.m_options,
                relatedActivity.m_id,
                activityName,
                relatedActivity.m_cacheId,
                mayTerminateEarly)
        {
            Contract.Requires(relatedActivity != null);
        }

        /// <summary>
        /// Initializes a new instance of the CacheActivity class that
        /// is attached to the specified event source. The new activity will
        /// be attached to the related (parent) activity as specified by
        /// relatedActivityId.
        /// The activity is created in the Initialized state. Call Start() to
        /// write the activity's Start event.
        /// </summary>
        /// <param name="eventSource">
        /// The event source to which the activity events should be written.
        /// </param>
        /// <param name="relatedActivityId">
        /// The id of the related (parent) activity to which this new activity
        /// should be attached.
        /// </param>
        /// <param name="activityName">
        /// The name of the activity being created
        /// </param>
        /// <param name="cacheId">
        /// Name of the cache this activity is in.
        /// </param>
        /// <param name="mayTerminateEarly">
        /// If set to true, the activity may terminate with dispose
        /// without Stop.  Used for enumerators.
        /// </param>
        public CacheActivity(EventSource eventSource, Guid relatedActivityId, string activityName, string cacheId, bool mayTerminateEarly = false)
            : this(eventSource, CacheActivity.TelemetryOptions, relatedActivityId, activityName, cacheId, mayTerminateEarly)
        {
        }

        /// <summary>
        /// Gets this activity's unique identifier.
        /// </summary>
        public Guid Id
        {
            get { return m_id; }
        }

        private bool IsEnabled(EventLevel level, EventKeywords keywords)
        {
            return m_eventSource.IsEnabled(level, keywords);
        }

        /// <summary>
        /// True if method arguments should be logged
        /// </summary>
        /// <returns>
        /// True if the activity method argument should be logged
        /// </returns>
        /// <remarks>
        /// If the activity was enabled for logging and parameter keyword is
        /// enabled, this will return true.
        /// </remarks>
        protected bool TraceMethodArgumentsEnabled()
        {
            return m_enabled && IsEnabled(ParameterOptions.Level, ParameterOptions.Keywords);
        }

        /// <summary>
        /// True if method results should be logged
        /// </summary>
        /// <returns>
        /// True if the activity method results should be logged
        /// </returns>
        /// <remarks>
        /// If the activity was enabled for logging and return options keyword is
        /// enabled, this will return true.
        /// </remarks>
        protected bool TraceReturnValuesEnabled()
        {
            return m_enabled && IsEnabled(ReturnOptions.Level, ReturnOptions.Keywords);
        }

        /// <summary>
        /// True if statistics keyword is enabled
        /// </summary>
        /// <returns>
        /// If statistics logging is enabled, this will return true
        /// </returns>
        private bool TraceStatisticsEnabled()
        {
            return m_enabled && IsEnabled(StatisticOptions.Level, StatisticOptions.Keywords);
        }

        /// <summary>
        /// The elapsed time from the creation of this activity until now
        /// </summary>
        public TimeSpan ElapsedTime { get { return m_elapsedTimer.TimeSpan; } }

        /// <summary>
        /// Writes the Start Activity event
        /// </summary>
        /// <remarks>
        /// May only be called when the activity is in the Initialized state.
        /// Sets the activity to the Started state.
        /// </remarks>
        public void Start()
        {
            Start(default(EmptyStruct));
        }

        /// <summary>
        /// Writes a Start event with the data.
        /// </summary>
        /// <param name="data">The data to include in the event.</param>
        /// <remarks>
        /// May only be called when the activity is in the Initialized state.
        /// Sets the activity to the Started state.
        /// </remarks>
        private void Start<T>(T data)
        {
            Contract.Requires(m_state == State.Initialized);

            m_state = State.Started;

            if (m_enabled)
            {
                var finalData = new CacheETWData<T>() { CacheId = m_cacheId, Data = data };
                var options = m_options;
                options.Opcode = EventOpcode.Start;
                m_eventSource.Write(m_activityName, ref options, ref m_id, ref m_relatedId, ref finalData);
            }
        }

        /// <summary>
        /// Starts the activity and logs the arguments to it.
        /// </summary>
        /// <typeparam name="T">Type containg the method arguments.</typeparam>
        /// <param name="data">Instance of T</param>
        public void StartWithMethodArguments<T>(T data)
        {
            Start();

            if (TraceMethodArgumentsEnabled())
            {
                Write(ParameterOptions, data);
            }
        }

        /// <summary>
        /// Writes a Stop event while translating the Failure parameter to something
        /// traceable by ETW.
        /// </summary>
        /// <param name="failure">Failure to write to event stream.</param>
        /// <returns>Returns the same failure as passed in for easy chaining</returns>
        public Failure StopFailure(Failure failure)
        {
            if (m_eventSource.IsEnabled(EventLevel.Error, Keywords.Failurevalue))
            {
                Write(new EventSourceOptions() { Keywords = Keywords.Failurevalue, Level = EventLevel.Error }, failure.ToETWFormat());
            }

            Stop();

            return failure;
        }

        /// <summary>
        /// Write a stop event for an unexpected exception
        /// </summary>
        /// <param name="e">The exception</param>
        public void StopException(Exception e)
        {
            if (m_eventSource.IsEnabled(EventLevel.Error, Keywords.Failurevalue))
            {
                Write(new EventSourceOptions() { Keywords = Keywords.Failurevalue, Level = EventLevel.Error }, e.ToString());
            }

            Stop();
        }

        /// <summary>
        /// Writes a Stop event with the specified name. Sets the activity
        /// to the Stopped state.
        /// May only be called when the activity is in the Started state.
        /// </summary>
        public void Stop()
        {
            Stop(default(EmptyStruct));
        }

        /// <summary>
        /// Generic stop with return result
        /// </summary>
        /// <typeparam name="T">Type of the successful return result</typeparam>
        /// <param name="result">The return result</param>
        /// <remarks>Use StopFailure for failures</remarks>
        public void StopResult<T>(T result)
        {
            if (TraceReturnValuesEnabled())
            {
                Write(ReturnOptions, new { Result = result });
            }

            Stop();
        }

        /// <summary>
        /// Writes a Stop event with the specified name and data. Sets the
        /// activity to the Stopped state.
        /// May only be called when the activity is in the Started state.
        /// </summary>
        /// <param name="data">The data to include in the event.</param>
        public void Stop<T>(T data)
        {
            Stop(m_activityName, ref data);
        }

        /// <summary>
        /// Writes an event associated with this activity.
        /// May only be called when the activity is in the Started state.
        /// </summary>
        /// <param name="data">The data to include in the event.</param>
        public void Write<T>(T data)
        {
            var options = default(EventSourceOptions);

            var finalData = new CacheETWData<T>() { CacheId = m_cacheId, Data = data };
            Write(m_activityName, ref options, ref finalData);
        }

        /// <summary>
        /// Writes an event associated with this activity.
        /// May only be called when the activity is in the Started state.
        /// </summary>
        /// <param name="options">
        /// The options to use for the event.
        /// </param>
        /// <param name="data">The data to include in the event.</param>
        public void Write<T>(EventSourceOptions options, T data)
        {
            var finalData = new CacheETWData<T>() { CacheId = m_cacheId, Data = data };
            Write(m_activityName, ref options, ref finalData);
        }

        /// <summary>
        /// Writes an event record for each of the key/value pairs
        /// in the statistics enumeration passed in.
        /// </summary>
        /// <typeparam name="TName">Name of the statistic type (usually string)</typeparam>
        /// <typeparam name="TValue">Value of the statistic type (usually double)</typeparam>
        /// <param name="statistics">Enumeration of the key/value pairs</param>
        /// <remarks>
        /// This checks the tracing statistics enabled flag before doing the enumeration
        /// such that no statistics are traced if not enabled.
        ///
        /// Note that the statistics are output as an event for each named value
        /// This is not the best layout but it turns out a dictionary does not work
        /// for the generic case.
        /// </remarks>
        public void WriteStatistics<TName, TValue>(IEnumerable<KeyValuePair<TName, TValue>> statistics)
        {
            if (TraceStatisticsEnabled())
            {
                var options = StatisticOptions;
                foreach (KeyValuePair<TName, TValue> kv in statistics)
                {
                    var data = new { CacheId = m_cacheId, Name = kv.Key, Value = kv.Value };
                    m_eventSource.Write(m_activityName, ref options, ref m_id, ref m_relatedId, ref data);
                }
            }
        }

        /// <summary>
        /// If the activity is in the Started state, calls Stop("Dispose").
        /// If the activity is in any other state, this is a no-op.
        /// Note that this class inherits from IDisposable to enable use in
        /// using blocks, not because it owns any unmanaged resources. It is
        /// not necessarily an error to abandon an activity without calling
        /// Dispose, especially if you call Stop directly.
        /// </summary>
        public void Dispose()
        {
            if (m_state == State.Started)
            {
                m_state = State.Stopped;

                if (m_enabled)
                {
                    if (m_mayTerminateEarly)
                    {
                        var finalData = new CacheETWData<string>() { CacheId = m_cacheId, Data = "Terminated Early" };
                        m_eventSource.Write(m_activityName, ref m_options, ref m_id, ref m_relatedId, ref finalData);
                    }
                    else
                    {
                        var data = default(EmptyStruct);

                        // While we could use the activity name here and embed the "disposed" comment in the data, writing it as the
                        // event name makes testing for it easier, and we can still track back to the correct start call via the activityID.
                        m_eventSource.Write(DisposedEventName, ref m_options, ref m_id, ref m_relatedId, ref data);
                    }
                }
            }
        }

        private void Write<T>(string activityName, ref EventSourceOptions options, ref T data)
        {
            Contract.Requires(m_state == State.Started);

            if (m_enabled)
            {
                var finalData = new CacheETWData<T>() { CacheId = m_cacheId, Data = data };
#if NET_FRAMEWORK
                // Fails on .Net Standard build: System.ArgumentException: The API supports only anonymous types or types decorated with the EventDataAttribute. Non-compliant type: Failure dataType.
                // finalData seems to be the problematic argument
                m_eventSource.Write(activityName, ref options, ref m_id, ref m_relatedId, ref finalData);
#else
                m_eventSource.Write(activityName, options);
#endif
            }
        }

        private void Stop<T>(string activityName, ref T data)
        {
            Contract.Requires(m_state == State.Started);

            m_state = State.Stopped;

            if (m_enabled)
            {
                var finalData = new CacheETWData<T>() { CacheId = m_cacheId, Data = data };
                m_eventSource.Write(activityName, ref m_options, ref m_id, ref m_relatedId, ref finalData);
            }
        }

        /// <summary>
        /// <para>
        /// If the relatedId is marked with the 0x80 bit in its eighth byte,
        /// it shall be treated as a "rooted" activity; in that case,
        /// its activity root (the first six bytes) are copied to the new id.
        /// All activity ids generated by this class mark themselves as "rooted".
        /// </para>
        /// <para>
        /// Visual aid:
        ///                ..8.. to signify rooted ID (the eighth byte)
        ///   {00000000-0000-0000-0000-000000000000}
        ///    ^^^^^^^^-^^^^ root bytes (six)
        /// </para>
        /// </summary>
        private static Guid CreateId(Guid relatedId)
        {
            const int MarkerByteIndex = 7;
            const byte MarkerValue = 0x80;

            byte[] idBytes = Guid.NewGuid().ToByteArray();
            byte[] relatedIdBytes = relatedId.ToByteArray();

            if ((relatedIdBytes[MarkerByteIndex] & MarkerValue) == MarkerValue)
            {
                // RelatedId is also rooted -> copy over root bytes
                Array.Copy(relatedIdBytes, idBytes, 6);
            }

            idBytes[MarkerByteIndex] |= MarkerValue;

            return new Guid(idBytes);
        }

        private enum State
        {
            Initialized,
            Started,
            Stopped,
        }

        [EventData]
        private readonly struct EmptyStruct
        {
        }

        /// <summary>
        /// Wrapper to allow the cache ID to be present on each ETW line.
        /// </summary>
        /// <typeparam name="T">Type beng traced</typeparam>
        [EventData]
        private struct CacheETWData<T>
        {
            [EventField]
            public string CacheId { get; set; }

            [EventField]
            public T Data { get; set; }
        }
    }
}
