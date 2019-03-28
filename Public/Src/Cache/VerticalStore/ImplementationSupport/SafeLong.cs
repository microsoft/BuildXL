// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// A "SafeLong" type that safely supports addition in multi-threaded manner
    /// </summary>
    /// <remarks>
    /// This is done as a struct with default construction being cleared/zero value
    /// </remarks>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes", Justification = "A volatile/interlocked type not meant to be compared")]
    public struct SafeLong
    {
        private long m_value;

        /// <summary>
        /// Add a number to this SafeLong in a safe way
        /// </summary>
        /// <param name="amount">The amount to add</param>
        public void Add(long amount = 1)
        {
            Interlocked.Add(ref m_value, amount);
        }

        /// <summary>
        /// Safely read this value
        /// </summary>
        public long Value
        {
            get
            {
                // This is the only cross-CPU way to atomically read 64-bits
                // Note that on a 64-CPU with aligned data, this should be
                // an atomic read with no extra cost.  (By CLR definition)
                return Interlocked.Read(ref m_value);
            }
        }
    }
}
