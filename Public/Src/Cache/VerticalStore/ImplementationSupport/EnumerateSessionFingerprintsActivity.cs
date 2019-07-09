// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.Tracing;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// ICache Session API specific activity
    /// </summary>
    public sealed class EnumerateSessionFingerprintsActivity : CacheActivity
    {
        /// <summary>
        /// Activity Constructor
        /// </summary>
        /// <param name="eventSource">The cache's event source</param>
        /// <param name="relatedActivityId">The related activity ID as passed to the API</param>
        /// <param name="cache">The ICache session instance (this)</param>
        public EnumerateSessionFingerprintsActivity(EventSource eventSource, Guid relatedActivityId, ICacheReadOnlySession cache)
            : base(
                eventSource,
                new EventSourceOptions
                {
                    Level = EventLevel.Informational,
                    Keywords = Keywords.TelemetryKeyword | Keywords.EnumerateSessionFingerprints,
                },
                relatedActivityId,
                InterfaceNames.EnumerateSessionFingerprints,
                cache.CacheId,
                mayTerminateEarly: true)
        {
        }
    }
}
