// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// Counter of number of calls, sum of bytes, sum-of-squares of bytes,
    /// and the sum of time per count and sum-of-squares of time per count.
    /// </summary>
    public class CountedBytesDistribution : BaseCounters
    {
        private SafeLong m_count = default(SafeLong);
        private readonly SumsCounter m_bytes = new SumsCounter();
        private readonly SumsCounter m_time = new SumsCounter();

        /// <summary>
        /// Add the number of bytes and time to the counter
        /// </summary>
        /// <param name="bytes">Number of bytes for this call</param>
        /// <param name="time">Elapsed time for this call</param>
        public virtual void Add(long bytes, ElapsedTimer time)
        {
            m_count.Add();
            m_bytes.Add(bytes);
            m_time.Add(time.TotalMilliseconds);
        }
    }
}
