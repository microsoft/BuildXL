// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Stats
{
    /// <summary>
    ///     Call count and cummulative duration.
    /// </summary>
    public sealed class CallCounter
    {
        private readonly string _callName;
        private readonly Counter _count;
        private readonly Counter _ticks;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CallCounter"/> class.
        /// </summary>
        public CallCounter(string callName)
        {
            Contract.Requires(callName != null);

            _callName = callName;

            _count = new Counter($"{callName}CallCount");
            _ticks = new Counter($"{callName}CallTicks");
        }

        /// <summary>
        ///     Append counter data to caller's set.
        /// </summary>
        public void AppendTo(CounterSet counterSet)
        {
            var totalMilliseconds = Duration.TotalMilliseconds;
            counterSet.Add(_count.Name, _count.Value);
            counterSet.Add($"{_callName}AverageMs", _count.Value != 0 ? (long)(totalMilliseconds / _count.Value) : 0);
            counterSet.Add($"{_callName}CallMs", (long)totalMilliseconds);
        }

        /// <summary>
        ///     Gets number of calls made.
        /// </summary>
        public long Calls => _count.Value;

        /// <summary>
        ///     Gets duration of all calls.
        /// </summary>
        public TimeSpan Duration => new TimeSpan(_ticks.Value);

        /// <summary>
        ///     Notify a call has started.
        /// </summary>
        public void Started()
        {
        }

        /// <summary>
        ///     Notify a call has completed.
        /// </summary>
        public void Completed(long ticks)
        {
            _count.Add(1);
            _ticks.Add(ticks);
        }
    }
}
