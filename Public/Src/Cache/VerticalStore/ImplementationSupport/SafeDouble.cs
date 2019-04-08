// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// A "SafeDouble" type that safely supports addition in multi-threaded manner
    /// </summary>
    /// <remarks>
    /// This is done as a struct with default construction being cleared/zero value
    /// </remarks>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes", Justification = "A volatile/interlocked type not meant to be compared")]
    public struct SafeDouble
    {
        private double m_value;

        /// <summary>
        /// Add a number to this SafeDouble in a safe way
        /// </summary>
        /// <param name="amount">The amount to add</param>
        public void Add(double amount = 1.0)
        {
            // This is our first (and only non-cmpexg) read of the field
            double exchg = m_value;
            double prior;

            do
            {
                prior = exchg;

                // If the count ever goes to NaN there is nothing more to do
                // We are done as NaN never changes and it never compares with
                // itself so the CAS loop would fail.
                if (double.IsNaN(prior))
                {
                    return;
                }

                // Get the result of the cmpexg.  This will be the new prior value if
                // we raced or will be the prior value if all was good.  Thus we only
                // touch memory in this one call.  (Well, once the JIT does a good job
                // of making this loop efficient and all in registers)
                exchg = Interlocked.CompareExchange(ref m_value, prior + amount, prior);
            }
            while (prior != exchg);
        }

        /// <summary>
        /// Safely read this value
        /// </summary>
        public double Value
        {
            get
            {
                // Cute trick - there is no Interlocked.Read for Double but by doing
                // an Interlocked.CompareExchange in this way, we get the current value
                // of the Double in an untorn read - and we may exchange a 0.0 with 0.0
                // NOTE:  If we know we are on x64 and the JIT produces SSE floating point
                // memory accesses, then we could read the double as a single atomic
                // operation.  But we have no assurances of that so this is the atomic
                // read that is assured.
                // We should technically not need this since we should only
                // read the value when not updating but I am being extra careful...
                return Interlocked.CompareExchange(ref m_value, 0.0, 0.0);
            }
        }
    }
}
