// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Storage;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine.Cache.Fingerprints.SinglePhase
{
    /// <summary>
    /// Represents an interval of time in a tree where parent's represent
    /// progressively larger intervals of time
    /// </summary>
    internal sealed class TimeTreeNode
    {
        /// <summary>
        /// Specifies the time interval buckets for the the tree (i.e. the precision used for
        /// separating nodes at a particular level in the tree).
        /// </summary>
        private static readonly ReadOnlyArray<TimeSpan> s_timeBuckets = ReadOnlyArray<TimeSpan>.FromWithoutCopy(new[]
        {
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1),

            TimeSpan.FromHours(2),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            TimeSpan.FromDays(2),

            TimeSpan.FromDays(8),
            TimeSpan.FromDays(24),
            TimeSpan.FromDays(120),
        });

        private readonly long m_timeIntervalStart;
        private readonly int m_timeBucketIndex;
        private readonly long m_timeValue;
        private readonly long m_maxTimeValue;

        /// <summary>
        /// The start time for the span of time represented by this node.
        /// </summary>
        public DateTime StartTime => (DateTime.MinValue + TimeSpan.FromTicks(m_timeIntervalStart)).ToLocalTime();

        /// <summary>
        /// The end time for the span of time represented by this node.
        /// </summary>
        public DateTime EndTime => (DateTime.MinValue + TimeSpan.FromTicks(m_timeIntervalStart) + s_timeBuckets[m_timeBucketIndex]).ToLocalTime();

        /// <summary>
        /// Creates a new time node with the smallest interval corresponding to the given times
        /// </summary>
        public TimeTreeNode(DateTime? time = null)
            : this(0, GetTimeValue(time?.ToUniversalTime() ?? DateTime.UtcNow))
        {
        }

        /// <summary>
        /// Gets the time key which is the <paramref name="timeValue"/> mapped to the its value
        /// in the given time bucket plus the bucket index
        /// </summary>
        private static long GetTimeKey(int bucketIndex, long timeValue)
        {
            return (timeValue / s_timeBuckets[bucketIndex].Ticks) * s_timeBuckets[bucketIndex].Ticks;
        }

        private static long GetTimeValue(DateTime time)
        {
            return (time - DateTime.MinValue).Ticks;
        }

        private TimeTreeNode(int timeBucketIndex, long timeValue, long? maxTimeValue = null)
        {
            m_timeBucketIndex = timeBucketIndex;
            m_timeIntervalStart = GetTimeKey(timeBucketIndex, timeValue);
            m_timeValue = timeValue;
            m_maxTimeValue = maxTimeValue ?? timeValue;
        }

        /// <summary>
        /// Gets the parent node. May be null, if the node is the root.
        /// </summary>
        public TimeTreeNode Parent
        {
            get
            {
                var parentTimeBucketIndex = m_timeBucketIndex + 1;
                if (parentTimeBucketIndex >= s_timeBuckets.Length)
                {
                    return null;
                }

                return new TimeTreeNode(parentTimeBucketIndex, m_timeValue);
            }
        }

        /// <summary>
        /// The fingerprint for the node's time interval
        /// </summary>
        public StrongContentFingerprint NodeFingerprint
        {
            get
            {
                // Add the bucket index when computing the fingerprint
                // because a child and parent can have the same start time
                // but they will have different bucket indices
                var timeBucketKeyBytes = BitConverter.GetBytes(m_timeIntervalStart + m_timeBucketIndex);
                Array.Resize(ref timeBucketKeyBytes, FingerprintUtilities.FingerprintLength);
                return new StrongContentFingerprint(FingerprintUtilities.CreateFrom(timeBucketKeyBytes));
            }
        }

        /// <summary>
        /// Gets the list of child nodes
        /// </summary>
        public IReadOnlyList<TimeTreeNode> Children
        {
            get
            {
                // Example children computation:
                // Node: 7/12/2017 7:00-7:15
                // Time: 7/12/2017 7:11:53
                // Child Interval Width: 0:01
                // => Interval Start: 7/12/2017 7:11
                // => Children:
                // 7/12/2017 7:11 - 7:12
                // 7/12/2017 7:12 - 7:13
                // 7/12/2017 7:13 - 7:14
                // 7/12/2017 7:14 - 7:15
                List<TimeTreeNode> children = new List<TimeTreeNode>();
                if (m_timeBucketIndex == 0)
                {
                    // No children for the most granular time bucket
                    return children;
                }

                // The bucket index of children
                int childBucketIndex = m_timeBucketIndex - 1;

                long intervalStart = m_timeIntervalStart;
                long intervalWidth = s_timeBuckets[childBucketIndex].Ticks;

                // Get the number of children for time bucket
                var intervalCount = (s_timeBuckets[m_timeBucketIndex].Ticks + intervalWidth - 1) / intervalWidth;
                for (int i = 0; i < intervalCount; i++)
                {
                    var childTimeValue = intervalStart + (intervalWidth * i);
                    if (childTimeValue > m_maxTimeValue)
                    {
                        break;
                    }

                    var childTimeMax = childTimeValue + intervalWidth - 1;
                    children.Add(new TimeTreeNode(childBucketIndex, childTimeValue, Math.Min(childTimeMax, m_maxTimeValue)));
                }

                return children;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"{StartTime} - {EndTime} ({NodeFingerprint})");
        }
    }
}
