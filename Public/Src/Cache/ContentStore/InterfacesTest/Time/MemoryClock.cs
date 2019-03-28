// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Time
{
    public class MemoryClock : ITestClock
    {
        private readonly object _lock = new object();
        private DateTime _utcNow = DateTime.UtcNow;

        public DateTime UtcNow
        {
            get
            {
                lock (_lock)
                {
                    return _utcNow;
                }
            }

            set
            {
                lock (_lock)
                {
                    _utcNow = value;
                }
            }
        }

        public void Increment()
        {
            lock (_lock)
            {
                _utcNow += TimeSpan.FromSeconds(1);
            }
        }
    }
}
