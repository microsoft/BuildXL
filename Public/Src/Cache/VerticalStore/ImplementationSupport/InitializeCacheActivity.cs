// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.Tracing;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// ICache Session API specific activity
    /// </summary>
    public sealed class InitializeCacheActivity : CacheActivity
    {
        /// <summary>
        /// Activity Constructor
        /// </summary>
        /// <param name="eventSource">The cache's event source</param>
        /// <param name="relatedActivityId">The related activity ID as passed to the API</param>
        /// <param name="cacheTypeName">The name of the type of cache being initialized</param>
        /// <remarks>
        /// Note that the "CacheId" of this activity is "new Cache.Type.Name" and it is only on
        /// completetion of the construction activity that the actual CacheId is known (and logged).
        /// </remarks>
        public InitializeCacheActivity(EventSource eventSource, Guid relatedActivityId, string cacheTypeName)
            : base(
                eventSource,
                new EventSourceOptions
                {
                    Level = EventLevel.Informational,
                    Keywords = Keywords.TelemetryKeyword | Keywords.InitializeCache,
                },
                relatedActivityId,
                InterfaceNames.InitializeCache,
                "new:" + cacheTypeName)
        {
        }

        /// <summary>
        /// Writes the Start Activity event
        /// </summary>
        public void Start(ICacheConfigData configData)
        {
            Start();

            if (TraceMethodArgumentsEnabled())
            {
                Write(
                    ParameterOptions,
                    new
                    {
                        CacheConfig = configData.Serialize(),
                    });
            }
        }

        /// <summary>
        /// Return result for the activity
        /// </summary>
        /// <param name="result">The return result</param>
        /// <returns>
        /// Returns the same value as given
        /// </returns>
        /// <remarks>
        /// This puts into telemetry the CacheId of the cache that was created
        /// since that ID is not known until after the InitializeCache activity
        /// is started.
        /// </remarks>
        /// <notes>
        /// Returns the same value as given such that code can be
        /// written that looks like:
        /// <code>return activity.Returns(result);</code>
        /// If we had a formal way to do tail-calls in C# we would
        /// have done that.  This is as close as it gets.
        /// </notes>
        public Possible<ICache, Failure> Returns(Possible<ICache, Failure> result)
        {
            if (result.Succeeded)
            {
                if (TraceReturnValuesEnabled())
                {
                    Write(
                        ReturnOptions,
                        new
                        {
                            CacheId = result.Result.CacheId,
                        });
                }

                Stop();
            }
            else
            {
                StopFailure(result.Failure);
            }

            return result;
        }
    }
}
