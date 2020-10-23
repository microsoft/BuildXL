// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

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

        public DateTime Increment()
        {
            lock (_lock)
            {
                _utcNow += TimeSpan.FromSeconds(1);
                return _utcNow;
            }
        }

        public DateTime AddSeconds(int seconds)
        {
            lock (_lock)
            {
                _utcNow += TimeSpan.FromSeconds(seconds);
                return _utcNow;
            }
        }
        
        public DateTime Increment(TimeSpan timeSpan)
        {
            Contract.Requires(timeSpan > TimeSpan.Zero);
            lock (_lock)
            {
                _utcNow += timeSpan;
                return _utcNow;
            }
        }
    }
}
