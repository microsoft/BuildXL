// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using System;
using System.Collections.Generic;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// Updates the content tracker lazily or eagerly based on local content age.
    /// </summary>
    public class ContentTrackerUpdater
    {
        private readonly Action<List<ContentHashWithSize>> m_touchContent;

        /// <summary>
        /// Age limit allowed for content to be updated lazily in the content tracker.
        /// </summary>
        private readonly TimeSpan m_bumpTimeThreshold;

        private readonly IClock m_clock;

        /// <nodoc />
        public ContentTrackerUpdater(Action<List<ContentHashWithSize>> touchContent, TimeSpan contentHashBumpTime, IClock clock = null)
        {
            m_touchContent = touchContent;
            m_clock = clock ?? SystemClock.Instance;

            // Set threshold to a fraction of the expiry.
            // Updating eagerly if past this threshold ensures that untracked content will be immediately re-registered.
            m_bumpTimeThreshold = TimeSpan.FromTicks(contentHashBumpTime.Ticks / 2);
        }

        /// <summary>
        /// Decides whether or not content should be lazily updated in the content tracker based on a determined age limit.
        /// </summary>
        /// <returns>Content that needs to be updated synchronously because it may have fallen out of the content tracker.</returns>
        public IList<ContentHashWithSize> ScheduleHashTouches(
            Context context,
            IReadOnlyList<ContentHashWithSizeAndLastAccessTime> contentHashesWithInfo)
        {
            var eagerUpdate = new List<ContentHashWithSize>();

            if (contentHashesWithInfo.Count > 0)
            {
                var ageLimit = m_clock.UtcNow - m_bumpTimeThreshold;
                var lazyUpdate = new List<ContentHashWithSize>();

                foreach (var hashInfo in contentHashesWithInfo)
                {
                    var hashWithSize = new ContentHashWithSize(hashInfo.Hash, hashInfo.Size);

                    // If content is older than the age threshold, we err on 
                    // the side of caution and update the content tracker eagerly.
                    if (hashInfo.LastAccessTime < ageLimit)
                    {
                        eagerUpdate.Add(hashWithSize);
                    }
                    else
                    {
                        lazyUpdate.Add(hashWithSize);
                    }
                }

                m_touchContent(lazyUpdate);
            }

            return eagerUpdate;
        }
    }
}
