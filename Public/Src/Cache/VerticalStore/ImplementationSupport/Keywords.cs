// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.Tracing;

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// Predefined keywords for cache activities.
    /// </summary>
    public static class Keywords
    {
        /// <summary>
        /// Indicates the event is the start parameters of an activity.
        /// </summary>
        public const EventKeywords MethodArguments = (EventKeywords)0x00001;

        /// <summary>
        /// Indicates the event is the return values of an activity.
        /// </summary>
        public const EventKeywords ReturnValue = (EventKeywords)0x00002;

        /// <summary>
        /// Indicates that the value of a failure should be traced.
        /// </summary>
        public const EventKeywords Failurevalue = (EventKeywords)0x00004;

        /// <summary>
        /// Indicates that this is providing statistic name/value events
        /// </summary>
        public const EventKeywords Statistics = (EventKeywords)0x00008;

        /// <summary>
        /// ICache session API call specific keyword
        /// </summary>
        public const EventKeywords Close = (EventKeywords)0x00010;

        /// <summary>
        /// ICache session API call specific keyword
        /// </summary>
        public const EventKeywords EnumerateStrongFingerprints = (EventKeywords)0x00020;

        /// <summary>
        /// ICache session API call specific keyword
        /// </summary>
        public const EventKeywords GetCacheEntry = (EventKeywords)0x00040;

        /// <summary>
        /// ICache session API call specific keyword
        /// </summary>
        public const EventKeywords GetStream = (EventKeywords)0x00080;

        /// <summary>
        /// ICache session API call specific keyword
        /// </summary>
        public const EventKeywords PinToCas = (EventKeywords)0x00100;

        /// <summary>
        /// ICache session API call specific keyword
        /// </summary>
        public const EventKeywords ProduceFile = (EventKeywords)0x00200;

        /// <summary>
        /// ICache session API call specific keyword
        /// </summary>
        public const EventKeywords GetStatistics = (EventKeywords)0x00400;

        /// <summary>
        /// ICache session API call specific keyword
        /// </summary>
        public const EventKeywords AddOrGet = (EventKeywords)0x00800;

        /// <summary>
        /// ICache session API call specific keyword
        /// </summary>
        public const EventKeywords AddToCas = (EventKeywords)0x01000;

        /// <summary>
        /// ICache session API call specific keyword
        /// </summary>
        public const EventKeywords IncorporateRecords = (EventKeywords)0x02000;

        /// <summary>
        /// ICache session API call specific keyword
        /// </summary>
        public const EventKeywords EnumerateSessionFingerprints = (EventKeywords)0x04000;

        /// <summary>
        /// ICache session API call specific keyword
        /// </summary>
        public const EventKeywords ValidateContent = (EventKeywords)0x08000;

        /// <summary>
        /// ICache initialization specific keyword
        /// </summary>
        public const EventKeywords InitializeCache = (EventKeywords)0x10000;

        /// <summary>
        /// Add CriticalDataKeyword to eventSourceOptions.Keywords to indicate that
        /// an event should be transmitted without sampling.
        /// This keyword should not be combined with TelemetryKeyword.
        /// For a matching pre-created EventSourceOptions instance, see <seealso cref="CacheActivity.CriticalDataOptions"/>.
        /// </summary>
        public const EventKeywords CriticalDataKeyword = (EventKeywords)0x0000800000000000;

        /// <summary>
        /// Add TelemetryKeyword to eventSourceOptions.Keywords to indicate that
        /// an event is for general-purpose telemetry.
        /// This keyword should not be combined with CriticalDataKeyword.
        /// For a matching pre-created EventSourceOptions instance, see <seealso cref="CacheActivity.TelemetryOptions"/>.
        /// </summary>
        public const EventKeywords TelemetryKeyword = (EventKeywords)0x0000200000000000;
    }
}
