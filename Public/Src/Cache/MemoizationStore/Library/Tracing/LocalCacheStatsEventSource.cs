// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if NET_FRAMEWORK

using System;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using System.Diagnostics.Tracing;

namespace BuildXL.Cache.MemoizationStore.Tracing
{
    /// <summary>
    ///     ETW event id
    /// </summary>
    public enum StatsEventId
    {
        /// <summary>
        ///     Uninitialized
        /// </summary>
        // ReSharper disable once UnusedMember.Global
        None = 0,

        /// <summary>
        ///     Dump stats
        /// </summary>
        Stats
    }

    /// <summary>
    ///     LocalCache statistics event logger.
    /// </summary>
    /// <remarks>
    ///     This class uses the EventSource from the Nuget package (Microsoft.Diagnostics.Tracing)
    ///     instead of the class built into the framework (System.Diagnostics.Tracing). This is
    ///     necessary to get the self-describing event format with rich payloads while still
    ///     targeting the 4.5 framework version. These features are needed by some customers. Once
    ///     we are able to target the 4.6 framework, the option to converge onto a single provider
    ///     will be available.
    /// </remarks>
    [EventSource(Name = "LocalCacheStats")]
    public sealed class LocalCacheStatsEventSource : EventSource
    {
        private LocalCacheStatsEventSource()
#if NET_FRAMEWORK_451
            : base()
#else
            : base(EventSourceSettings.EtwSelfDescribingEventFormat)
#endif
        {
        }

        /// <summary>
        ///     The singleton instance.
        /// </summary>
        public static readonly LocalCacheStatsEventSource Instance = new LocalCacheStatsEventSource();

        /// <summary>
        ///     Log the statistics event with a collection of statistics gathered during the run.
        ///     The build's activity id is read from the environment variable _MS_BUILD_ACTIVITY_ID and
        ///     then set if present so that the event can be correlated with the rest of the build events.
        /// </summary>
        [Event((int)StatsEventId.Stats, Level = EventLevel.Informational)]
        public void Stats(CounterSet snapshot)
        {
            if (IsEnabled())
            {
                var oldActivity = Guid.Empty;
                var buildActivity = Environment.GetEnvironmentVariable("_MS_ENGSYS_ACTIVITY_ID");
                Guid activity;
                if (buildActivity != null && Guid.TryParse(buildActivity, out activity))
                {
                    SetCurrentThreadActivityId(activity, out oldActivity);
                }

                WriteEvent((int)StatsEventId.Stats, snapshot.ToDictionaryIntegral());

                if (oldActivity != Guid.Empty)
                {
                    SetCurrentThreadActivityId(oldActivity);
                }
            }
        }
    }
}
#endif
