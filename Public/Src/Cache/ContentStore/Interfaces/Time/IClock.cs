// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Interfaces.Time
{
    /// <summary>
    ///     Mockable clock interface to aid in unit testing
    /// </summary>
    public interface IClock
    {
        /// <summary>
        ///     Gets current UTC time
        /// </summary>
        DateTime UtcNow { get; }
    }

    /// <summary>
    /// For testing purposes only. Allows a clock to handle functionality of Task.Delay where enabled
    /// </summary>
    internal interface ITimerClock : IClock
    {
        Task Delay(TimeSpan interval, CancellationToken token = default);
    }

    /// <nodoc />
    public static class ClockExtensions
    {
        /// <nodoc />
        public static DateTime GetUtcNow(this IClock? clock)
        {
            clock ??= SystemClock.Instance;
            return clock.UtcNow;
        }

        /// <summary>
        /// Performs <see cref="Task.Delay(TimeSpan, CancellationToken)"/>, optionally clock can override the behavior by implementing <see cref="ITimerClock"/>
        /// </summary>
        public static Task Delay(this IClock clock, TimeSpan interval, CancellationToken token = default)
        {
            if (clock is ITimerClock timerClock)
            {
                return timerClock.Delay(interval, token);
            }
            else
            {
                return Task.Delay(interval, token);
            }
        }
    }
}
