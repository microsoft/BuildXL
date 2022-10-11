// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.Serialization;

namespace BuildXL.Cache.Host.Configuration
{
    [DataContract]
    public sealed class CacheActivityTrackerConfiguration
    {
        [DataMember]
        public TimeSpanSetting CounterActivityWindow { get; init; } = TimeSpan.FromMinutes(1);

        [DataMember]
        public TimeSpanSetting CounterSnapshotPeriod { get; init; } = TimeSpan.FromSeconds(10);

        [DataMember]
        public TimeSpanSetting CounterReportingPeriod { get; init; } = TimeSpan.FromSeconds(30);

        [DataMember]
        public TimeSpanSetting PerformanceReportingPeriod { get; init; } = TimeSpan.FromSeconds(30);

        [DataMember]
        public TimeSpanSetting PerformanceCollectionFrequency { get; init; } = TimeSpan.FromSeconds(30);

        [DataMember]
        public bool PerformanceLogWmiCounters { get; init; } = false;
    }
}
