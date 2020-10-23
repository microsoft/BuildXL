// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

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

    /// <nodoc />
    public static class ClockExtensions
    {
        /// <nodoc />
        public static DateTime GetUtcNow(this IClock? clock)
        {
            clock ??= SystemClock.Instance;
            return clock.UtcNow;
        }
    }
}
