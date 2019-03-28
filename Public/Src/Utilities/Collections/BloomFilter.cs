// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Compact probabilistic set. A query answers 'definitely not in set' or 'maybe in set'.
    /// </summary>
    public class BloomFilter
    {
        private readonly ConcurrentBitArray m_bits;
        private readonly Parameters m_parameters;

        /// <summary>
        /// Creates an empty filter with the given parameters.
        /// The <see cref="Parameters.NumberOfBits"/> specifies the final size of the filter.
        /// </summary>
        public BloomFilter(Parameters parameters)
        {
            Contract.Requires(parameters != null);

            m_bits = new ConcurrentBitArray(parameters.NumberOfBits);
            m_parameters = parameters;
        }

        /// <summary>
        /// Indicates if an item represented by <paramref name="hash"/> has possibly been added (false positives may occur).
        /// </summary>
        public bool PossiblyContains(in MurmurHash3 hash)
        {
            for (int i = 0; i < m_parameters.NumberOfHashFunctions; i++)
            {
                int index = (int)(hash.GetDerivedHash(i) % (ulong)m_parameters.NumberOfBits);
                if (!m_bits[index])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Adds an item represented by <paramref name="hash"/>. Subsequently, <see cref="PossiblyContains"/> for this item
        /// is guaranteed to return true.
        /// </summary>
        public void Add(in MurmurHash3 hash)
        {
            for (int i = 0; i < m_parameters.NumberOfHashFunctions; i++)
            {
                int index = (int)(hash.GetDerivedHash(i) % (ulong)m_parameters.NumberOfBits);
                m_bits[index] = true;
            }
        }

        /// <summary>
        /// Bloom filter parameters for size and number of hash functions.
        /// </summary>
        public sealed class Parameters
        {
            /// <summary>
            /// Number of hash functions applied to each element. (commonly 'k')
            /// </summary>
            public readonly int NumberOfHashFunctions;

            /// <summary>
            /// Number of bits in the filter. (commonly 'm')
            /// </summary>
            public readonly int NumberOfBits;

            private const double Log2Squared = 0.48045301391820144; // ln(2)^2

            private const double Log2 = 0.6931471805599453;

            /// <summary>
            /// Represents the given (not necessarily optimal) parameters.
            /// </summary>
            public Parameters(int numberOfBits, int numberOfHashFunctions)
            {
                Contract.Requires(numberOfBits > 0);
                Contract.Requires(numberOfHashFunctions > 0);

                NumberOfHashFunctions = numberOfHashFunctions;
                NumberOfBits = numberOfBits;
            }

            /// <summary>
            /// Finds optimal parameters to achieve a given false positive rate - in (0.0, 1.0) - for an expected number of elements.
            /// </summary>
            public static Parameters CreateOptimalWithFalsePositiveProbability(int numberOfElements, double targetFalsePositiveProbability)
            {
                Contract.Requires(numberOfElements > 0);
                Contract.Requires(targetFalsePositiveProbability < 1.0 && targetFalsePositiveProbability > 0.0);

                // m = -n ln(p) / ln(2)^2 = n ln(p^-1) / ln(2)^2
                int numberOfBits = checked((int)Math.Ceiling(numberOfElements * Math.Log(1 / targetFalsePositiveProbability) / Log2Squared));
                Contract.Assert(numberOfBits > 0);

                // k = m / n * ln
                int numberOfHashFunctions = checked((int)Math.Round(numberOfBits / (double)numberOfElements * Log2));

                if (numberOfHashFunctions == 0)
                {
                    numberOfHashFunctions = 1;
                }

                Contract.Assert(numberOfHashFunctions > 0);

                return new Parameters(numberOfBits, numberOfHashFunctions);
            }
        }
    }

    /// <summary>
    /// Compact probabilistic set. A query answers 'definitely not in set' or 'maybe in set'.
    /// Represents a set of items of type <typeparamref name="T"/>.
    /// </summary>
    public sealed class BloomFilter<T> : BloomFilter
    {
        private readonly Func<T, MurmurHash3> m_hasher;

        /// <summary>
        /// Creates an empty filter with the given parameters.
        /// The <see cref="BloomFilter.Parameters.NumberOfBits"/> specifies the final size of the filter.
        /// The <paramref name="hasher"/> function is used to derive the 'k' hashes for each entry of type <typeparamref name="T"/>.
        /// </summary>
        public BloomFilter(Parameters parameters, Func<T, MurmurHash3> hasher)
            : base(parameters)
        {
            Contract.Requires(hasher != null);
            Contract.Requires(parameters != null);

            m_hasher = hasher;
        }

        /// <summary>
        /// Indicates if a given item has possibly been added (false positives may occur).
        /// </summary>
        public bool PossiblyContains(T item)
        {
            return PossiblyContains(m_hasher(item));
        }

        /// <summary>
        /// Adds an item. Subsequently, <see cref="PossiblyContains"/> for this item
        /// is guaranteed to return true.
        /// </summary>
        public void Add(T item)
        {
            Add(m_hasher(item));
        }
    }
}
