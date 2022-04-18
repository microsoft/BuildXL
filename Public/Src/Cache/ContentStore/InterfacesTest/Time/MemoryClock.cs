// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Time
{
    public class MemoryClock : ITestClock, ITimerClock
    {
        private readonly object _lock = new object();
        private DateTime _utcNow = DateTime.UtcNow;

        public bool TimerQueueEnabled { get; set; }
        private readonly PriorityQueue<(DateTime FinishTime, SemaphoreSlim Completion)> _timerQueue
            = new(10, (x, y) => x.FinishTime.CompareTo(y.FinishTime));

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
                    UpdateTime(value);
                }
            }
        }

        public DateTime Increment()
        {
            return AddSeconds(1);
        }

        public DateTime AddSeconds(int seconds)
        {
            return Increment(TimeSpan.FromSeconds(seconds));
        }
        
        public DateTime Increment(TimeSpan timeSpan)
        {
            Contract.Requires(timeSpan > TimeSpan.Zero);
            lock (_lock)
            {
                return UpdateTime(_utcNow + timeSpan); 
            }
        }

        private DateTime UpdateTime(DateTime value)
        {
            Contract.Requires(Monitor.IsEntered(_lock));

            _utcNow = value;
            while (TimerQueueEnabled && _timerQueue.Count > 0 && _timerQueue.Top.FinishTime <= value)
            {
                var item = _timerQueue.Top;
                _timerQueue.Pop();

                item.Completion.Release();
            }

            return _utcNow;
        }

        public Task Delay(TimeSpan interval, CancellationToken token = default)
        {
            if (!TimerQueueEnabled || interval == Timeout.InfiniteTimeSpan || interval == TimeSpan.Zero)
            {
                return Task.Delay(interval, token);
            }
            else
            {
                var completion = new SemaphoreSlim(0, 1);
                _timerQueue.Push((UtcNow + interval, completion));
                return completion.WaitAsync(token);
            }
        }
    }
}
