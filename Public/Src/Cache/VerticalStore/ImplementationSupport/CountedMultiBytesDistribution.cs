// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// Counter of number of calls, sum of bytes, sum-of-squares of bytes,
    /// and the sum of time per count and sum-of-squares of time per count.
    /// </summary>
    public sealed class CountedMultiBytesDistribution : BaseCounters
    {
        private SumsCounter m_filecount = new SumsCounter();
        private SumsCounter m_skipped = new SumsCounter();
        private SumsCounter m_unknown = new SumsCounter();
        private SafeLong m_count = default(SafeLong);
        private readonly SumsCounter m_bytes = new SumsCounter();
        private readonly SumsCounter m_time = new SumsCounter();
        private readonly SumsCounter m_failed = new SumsCounter();

        /// <summary>
        /// Add the number of bytes and time to the counter
        /// </summary>
        /// <param name="fileCount">The number of files the bytes are spread over.</param>
        /// <param name="skipped">Number of files skipped</param>
        /// <param name="unknownSize">Number of files whose size could not be determined.</param>
        /// <param name="failed">Nnumber of files that failed to transit.</param>
        /// <param name="bytes">Number of bytes for this call</param>
        /// <param name="time">Elapsed time for this call</param>
        public void Add(long fileCount, long skipped, long unknownSize, long failed, long bytes, ElapsedTimer time)
        {
            m_filecount.Add(fileCount);
            m_skipped.Add(skipped);
            m_unknown.Add(unknownSize);
            m_count.Add();
            m_bytes.Add(bytes);
            m_time.Add(time.TotalMilliseconds);
            m_failed.Add(failed);
        }
    }
}
