// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Tracing;
using Xunit;

namespace BuildXL.Cache.ContentStore.Test.Tracing
{
    public class CacheActivityTrackerTests
    {
        [Fact]
        public void ReceivedEventHubMessagesIsMasterSpecific()
        {
            Assert.True(CaSaaSActivityTrackingCounters.ProcessedEventHubMessages.IsMasterOnlyCounter());
            Assert.False(CaSaaSActivityTrackingCounters.PushBytes.IsMasterOnlyCounter());
        }

        [Fact]
        public void FilterOutMasterOnlyActivities()
        {
            var activities = new Dictionary<CaSaaSActivityTrackingCounters, ActivityRate>
                             {
                                 [CaSaaSActivityTrackingCounters.ProcessedEventHubMessages] = new ActivityRate(42, 0.5),
                                 [CaSaaSActivityTrackingCounters.PushBytes] = new ActivityRate(42, 0.5),
                             };

            Assert.True(activities.ContainsKey(CaSaaSActivityTrackingCounters.ProcessedEventHubMessages));
            Assert.True(activities.ContainsKey(CaSaaSActivityTrackingCounters.PushBytes));

            activities.FilterOutMasterOnlyCounters();
            Assert.False(activities.ContainsKey(CaSaaSActivityTrackingCounters.ProcessedEventHubMessages));
            Assert.True(activities.ContainsKey(CaSaaSActivityTrackingCounters.PushBytes));
        }
    }
}
