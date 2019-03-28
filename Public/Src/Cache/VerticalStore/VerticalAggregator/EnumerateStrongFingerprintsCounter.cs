// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ImplementationSupport;

namespace BuildXL.Cache.VerticalAggregator
{
    internal sealed class EnumerateStrongFingerprintsCounter : BaseCounters
    {
        private SafeLong m_count = default(SafeLong);
        private readonly SumsCounter m_local = new SumsCounter();
        private SafeLong m_sentinel = default(SafeLong);
        private readonly SumsCounter m_remote = new SumsCounter();
        private readonly SumsCounter m_time = new SumsCounter();

        public void Add(double localCount, double remoteCount, long sentinelCount, ElapsedTimer time)
        {
            m_count.Add();
            m_local.Add(localCount);
            m_remote.Add(remoteCount);
            m_sentinel.Add(sentinelCount);
            m_time.Add(time.TotalMilliseconds);
        }
    }
}
