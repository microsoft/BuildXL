// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
