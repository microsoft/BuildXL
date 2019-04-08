// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// Count the number of calls and the sum of the time taken
    /// and the sum-of-squares of the time taken.
    /// </summary>
    public sealed class TimedCounter : BaseCounters
    {
        private SafeLong m_count = default(SafeLong);
        private readonly SumsCounter m_time = new SumsCounter();

        /// <summary>
        /// Add another call and its elapsed time
        /// </summary>
        /// <param name="time">Elapsed time for this call</param>
        public void Add(ElapsedTimer time)
        {
            m_count.Add();
            m_time.Add(time.TotalMilliseconds);
        }
    }
}
