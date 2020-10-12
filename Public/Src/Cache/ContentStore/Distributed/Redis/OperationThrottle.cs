// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Makes sure that we can only perform a certain amounts of operations in a given timespan.
    ///
    /// Note: if you start at 00:00 and the window is 1 minute we reset all the operations at 00:01.
    /// So its possible that for a given minute(from 00:00:30 to 00:01:30) we can have at most 2x of number of operations.
    /// </summary>
    internal class OperationThrottle

    {
        private readonly TimeSpan _operationLimitSpan;
        private readonly long _operationLimitCount;
        private readonly IClock _clock;

        private long _nextOperationLimitTicks;
        private long _count = 0;
        private BoolResult _errorResult;

        /// <nodoc />
        public OperationThrottle(TimeSpan operationLimitSpan, long operationLimitCount, IClock clock)
        {
            _operationLimitSpan = operationLimitSpan;
            _operationLimitCount = operationLimitCount;
            _clock = clock;
            _nextOperationLimitTicks = _clock.UtcNow.Ticks;
        }

        /// <summary>
        /// Determines whether we can still perform an operation based on current state.
        /// </summary>
        public BoolResult CheckAndRegisterOperation()
        {
            if (_clock.UtcNow.Ticks >= Interlocked.Read(ref _nextOperationLimitTicks))
            {
                lock (this)
                {
                    if (_clock.UtcNow.Ticks >= _nextOperationLimitTicks)
                    {
                        Interlocked.Exchange(ref _nextOperationLimitTicks, _clock.UtcNow.Ticks + _operationLimitSpan.Ticks);
                        Interlocked.Exchange(ref _count, 0);
                        var nextSpan = new DateTime(ticks: _nextOperationLimitTicks);
                        _errorResult = new BoolResult($"Operation limit has been reached. Throttling operation. Limit={_operationLimitCount}, SpanDuration=[{_operationLimitSpan}] NextSpanStart=[{nextSpan:MM/dd/yyyy hh:mm:ss.ffff}]");
                    }
                }
            }

            if (Interlocked.Increment(ref _count) > _operationLimitCount)
            {
                return _errorResult;
            }

            return BoolResult.Success;
        }
    }
}
