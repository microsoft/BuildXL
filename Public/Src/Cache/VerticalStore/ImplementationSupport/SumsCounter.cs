// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// Simple sum and sum-of-squares counter
    /// </summary>
    public sealed class SumsCounter : BaseCounters
    {
        private SafeDouble m_sum = default(SafeDouble);
        private SafeDouble m_sum2 = default(SafeDouble);

        /// <summary>
        /// Add the value to the sum and sum-of-squares
        /// </summary>
        /// <param name="value">Value to add</param>
        public void Add(double value)
        {
            m_sum.Add(value);
            m_sum2.Add(value * value);
        }
    }
}
