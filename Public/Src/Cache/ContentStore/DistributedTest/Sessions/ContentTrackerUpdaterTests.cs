// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using System;
using System.Linq;
using Xunit;

namespace ContentStoreTest.Distributed.Sessions
{
    public class ContentTrackerUpdaterTests
    {
        private readonly Logger m_logger = new Logger();

        [Fact]
        public void ContentEagerlyUpdated()
        {
            Context context = new Context(m_logger);
            TimeSpan contentHashBumpTime = TimeSpan.FromHours(1);
            ContentTrackerUpdater contentTrackerUpdater = new ContentTrackerUpdater(hashes => { }, contentHashBumpTime);

            ContentHash hash = ContentHash.Random();
            long size = 0;
            DateTime lastAccessTime = DateTime.MinValue; // Content hash will always be below the age limit.
            ContentHashWithSizeAndLastAccessTime contentHashInfo = new ContentHashWithSizeAndLastAccessTime(hash, size, lastAccessTime);

            var eagerUpdate = contentTrackerUpdater.ScheduleHashTouches(context, new[] { contentHashInfo });
            Assert.NotNull(eagerUpdate);
            Assert.Equal(eagerUpdate.Count, 1);
            Assert.Equal(eagerUpdate.Single().Hash, hash);
        }

        [Fact]
        public void ContentLazilyUpdated()
        {
            Context context = new Context(m_logger);
            TimeSpan contentHashBumpTime = TimeSpan.FromHours(1);
            ContentTrackerUpdater contentTrackerUpdater = new ContentTrackerUpdater(hashes => { }, contentHashBumpTime);

            ContentHash hash = ContentHash.Random();
            long size = 0;
            DateTime lastAccessTime = DateTime.MaxValue; // Content hash will always be above the age limit.
            ContentHashWithSizeAndLastAccessTime contentHashInfo = new ContentHashWithSizeAndLastAccessTime(hash, size, lastAccessTime);

            var eagerUpdate = contentTrackerUpdater.ScheduleHashTouches(context, new[] { contentHashInfo });
            Assert.Empty(eagerUpdate);
        }
    }
}
