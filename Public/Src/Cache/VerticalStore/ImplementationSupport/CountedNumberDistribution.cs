// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// Counter of number of values, sum of values, sum-of-squares of values,
    /// and the sum of time per count and sum-of-squares of time per count.
    /// </summary>
    public sealed class CountedNumberDistribution : BaseCounters
    {
        private SafeLong m_count = default(SafeLong);
        private readonly SumsCounter m_number = new SumsCounter();
        private readonly SumsCounter m_time = new SumsCounter();

        /// <summary>
        /// Add the amount and the time to the counter
        /// </summary>
        /// <param name="amount">The amount to add for this call</param>
        /// <param name="time">Elapsed time for this call</param>
        public void Add(double amount, ElapsedTimer time)
        {
            m_count.Add();
            m_number.Add(amount);
            m_time.Add(time.TotalMilliseconds);
        }
    }
}
