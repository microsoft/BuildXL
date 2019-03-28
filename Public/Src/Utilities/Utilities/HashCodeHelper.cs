// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities
{
    /// <summary>
    /// This class provides some utility function to compute stable strong hash codes.
    /// </summary>
    public static class HashCodeHelper
    {
#pragma warning disable SA1139 // Use literal suffix notation instead of casting
        // Magic numbers known to provide good hash distributions.
        // See here: http://www.isthe.com/chongo/tech/comp/fnv/
        private const int Fnv1Prime32 = 16777619;
        private const int Fnv1Basis32 = unchecked((int)2166136261);
        private const long Fnv1Prime64 = 1099511628211;
        private const long Fnv1Basis64 = unchecked((long)14695981039346656037);
#pragma warning restore SA1139 // Use literal suffix notation instead of casting

        /// <summary>
        /// Creates a strong hash code of a string
        /// </summary>
        public static int GetOrdinalHashCode(string value)
        {
            if (value == null)
            {
                return 0;
            }

            int hash = Fnv1Basis32;
            foreach (char c in value)
            {
                hash = Fold(hash, (short)c);
            }

            return hash;
        }

        /// <summary>
        /// Creates a strong hash code of a string
        /// </summary>
        public static long GetOrdinalHashCode64(string value)
        {
            if (value == null)
            {
                return 0;
            }

            long hash = Fnv1Basis64;
            foreach (char c in value)
            {
                hash = Fold(hash, (short)c);
            }

            return hash;
        }
        
        /// <summary>
        /// Creates a strong hash code of a character array.
        /// </summary>
        public static long GetOrdinalHashCode64(char[] value)
        {
            if (value == null)
            {
                return 0;
            }

            long hash = Fnv1Basis64;
            foreach (char c in value)
            {
                hash = Fold(hash, (short)c);
            }

            return hash;
        }

        /// <summary>
        /// Creates a case-invariant stable strong hash code of string
        /// </summary>
        public static int GetOrdinalIgnoreCaseHashCode(string value)
        {
            if (value == null)
            {
                return 0;
            }

            int hash = Fnv1Basis32;
            foreach (char c in value)
            {
                hash = Fold(hash, (short)c.ToUpperInvariantFast());
            }

            return hash;
        }

        /// <summary>
        /// Creates a case-invariant stable strong hash code of string
        /// </summary>
        public static long GetOrdinalIgnoreCaseHashCode64(string value)
        {
            if (value == null)
            {
                return 0;
            }

            long hash = Fnv1Basis64;
            foreach (char c in value)
            {
                hash = Fold(hash, (short)c.ToUpperInvariantFast());
            }

            return hash;
        }

        /// <summary>
        /// Creates a stable strong hash code
        /// </summary>
        public static int GetHashCode(long value)
        {
            unchecked
            {
                return Combine((int)value, (int)(((ulong)value) >> 32));
            }
        }

        private static int Fold(int hash, byte value)
        {
            return unchecked((hash * Fnv1Prime32) ^ (int)value);
        }

        private static int Fold(int hash, short value)
        {
            unchecked
            {
                return Fold(
                    Fold(
                        hash,
                        (byte)value),
                    (byte)(((uint)value) >> 8));
            }
        }

        private static int Fold(int hash, int value)
        {
            unchecked
            {
                return Fold(
                    Fold(
                        Fold(
                            Fold(
                                hash,
                                (byte)value),
                            (byte)(((uint)value) >> 8)),
                        (byte)(((uint)value) >> 16)),
                    (byte)(((uint)value) >> 24));
            }
        }

        /// <summary>
        /// Combines two hash codes in a stable strong way.
        /// </summary>
        public static int Combine(int value0, int value1)
        {
            return Fold(Fold(Fnv1Basis32, value0), value1);
        }

        /// <summary>
        /// Combines three hash codes in a stable strong way.
        /// </summary>
        public static int Combine(int value0, int value1, int value2)
        {
            return Fold(Fold(Fold(Fnv1Basis32, value0), value1), value2);
        }

        /// <summary>
        /// Combines four hash codes in a stable strong way.
        /// </summary>
        public static int Combine(int value0, int value1, int value2, int value3)
        {
            return Fold(Fold(Fold(Fold(Fnv1Basis32, value0), value1), value2), value3);
        }

        /// <summary>
        /// Combines five hash codes in a stable strong way.
        /// </summary>
        public static int Combine(int value0, int value1, int value2, int value3, int value4)
        {
            return Fold(Fold(Fold(Fold(Fold(Fnv1Basis32, value0), value1), value2), value3), value4);
        }

        /// <summary>
        /// Combines six hash codes in a stable strong way.
        /// </summary>
        public static int Combine(int value0, int value1, int value2, int value3, int value4, int value5)
        {
            return Fold(Fold(Fold(Fold(Fold(Fold(Fnv1Basis32, value0), value1), value2), value3), value4), value5);
        }

        /// <summary>
        /// Combines seven hash codes in a stable strong way.
        /// </summary>
        public static int Combine(int value0, int value1, int value2, int value3, int value4, int value5, int value6)
        {
            return Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fnv1Basis32, value0), value1), value2), value3), value4), value5), value6);
        }

        /// <summary>
        /// Combines eight hash codes in a stable strong way.
        /// </summary>
        public static int Combine(int value0, int value1, int value2, int value3, int value4, int value5, int value6, int value7)
        {
            return Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fnv1Basis32, value0), value1), value2), value3), value4), value5), value6), value7);
        }

        /// <summary>
        /// Combines nine hash codes in a stable strong way.
        /// </summary>
        public static int Combine(int value0, int value1, int value2, int value3, int value4, int value5, int value6, int value7, int value8)
        {
            return Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fnv1Basis32, value0), value1), value2), value3), value4), value5), value6), value7), value8);
        }

        /// <summary>
        /// Combines ten hash codes in a stable strong way.
        /// </summary>
        public static int Combine(int value0, int value1, int value2, int value3, int value4, int value5, int value6, int value7, int value8, int value9)
        {
            return Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fnv1Basis32, value0), value1), value2), value3), value4), value5), value6), value7), value8), value9);
        }

        /// <summary>
        /// Combines eleven hash codes in a stable strong way.
        /// </summary>
        public static int Combine(int value0, int value1, int value2, int value3, int value4, int value5, int value6, int value7, int value8, int value9, int value10)
        {
            return Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fold(Fnv1Basis32, value0), value1), value2), value3), value4), value5), value6), value7), value8), value9), value10);
        }

        /// <summary>
        /// Combines the specified values.
        /// </summary>
        public static int Combine(int[] values)
        {
            if (values == null)
            {
                return 0;
            }

            int hash = Fnv1Basis32;
            foreach (int value in values)
            {
                hash = Fold(hash, value);
            }

            return hash;
        }

        /// <summary>
        /// Combines the specified values.
        /// </summary>
        public static int Combine(byte[] values)
        {
            if (values == null)
            {
                return 0;
            }

            int hash = Fnv1Basis32;
            foreach (byte value in values)
            {
                hash = Fold(hash, value);
            }

            return hash;
        }

        /// <summary>
        /// Combines the specified values.
        /// </summary>
        public static int Combine(ArrayView<byte> values)
        {
            int hash = Fnv1Basis32;
            foreach (byte value in values)
            {
                hash = Fold(hash, value);
            }

            return hash;
        }

        /// <summary>
        /// Combines the specified array.
        /// </summary>
        public static int Combine<T>(T[] values, Func<T, int> converter)
        {
            Contract.Requires(converter != null);
            if (values == null)
            {
                return 0;
            }

            int hash = Fnv1Basis32;
            foreach (T value in values)
            {
                hash = Fold(hash, converter(value));
            }

            return hash;
        }

        private static long Fold(long hash, byte value)
        {
            unchecked
            {
                return (hash * Fnv1Prime64) ^ value;
            }
        }

        private static long Fold(long hash, short value)
        {
            unchecked
            {
                return Fold(
                    Fold(
                        hash,
                        (byte)value),
                    (byte)(((uint)value) >> 8));
            }
        }

        private static long Fold(long hash, long value)
        {
            unchecked
            {
                return Fold(
                    Fold(
                        Fold(
                            Fold(
                                Fold(
                                    Fold(
                                        Fold(
                                            Fold(
                                                hash,
                                                (byte)value),
                                            (byte)(((uint)value) >> 8)),
                                        (byte)(((uint)value) >> 16)),
                                    (byte)(((uint)value) >> 24)),
                                (byte)(((ulong)value) >> 32)),
                            (byte)(((ulong)value) >> 40)),
                        (byte)(((ulong)value) >> 48)),
                    (byte)(((ulong)value) >> 56));
            }
        }

        /// <summary>
        /// Combines two hash codes in a stable strong way.
        /// </summary>
        public static long Combine(long value0, long value1)
        {
            return Fold(Fold(Fnv1Basis64, value0), value1);
        }

        /// <summary>
        /// Combines three hash codes in a stable strong way.
        /// </summary>
        public static long Combine(long value0, long value1, long value2)
        {
            return Fold(Fold(Fold(Fnv1Basis64, value0), value1), value2);
        }

        /// <summary>
        /// Combines four hash codes in a stable strong way.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1025:ReplaceRepetitiveArgumentsWithParamsArray")]
        public static long Combine(long value0, long value1, long value2, long value3)
        {
            return Fold(Fold(Fold(Fold(Fnv1Basis64, value0), value1), value2), value3);
        }

        /// <summary>
        /// Combines the specified enumerable values.
        /// </summary>
        public static long Combine<T>(IEnumerable<T> values, Func<T, long> converter)
        {
            Contract.Requires(converter != null);

            if (values == null)
            {
                return 0;
            }

            long hash = Fnv1Basis64;
            foreach (T value in values)
            {
                hash = Fold(hash, converter(value));
            }

            return hash;
        }

        /// <summary>An array containing useful prime numbers in the positive integer range.</summary>
        private static readonly int[] s_primes =
        {
            3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353,
            431, 521, 631, 761, 919, 1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103,
            12143, 14591, 17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
            187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263, 1674319,
            2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369, 8639249, 10367101, 12440537,
            14928671, 17914409, 21497293, 25796759, 30956117, 37147349, 44576837, 53492207, 64190669, 77028803, 92434613,
            110921543, 133105859, 159727031, 191672443, 230006941, 276008387, 331210079, 397452101, 476942527,
            572331049, 686797261, 824156741, 988988137, 1186785773, 1424142949, 1708971541, 2050765853,
        };

        /// <summary>Returns a prime number which is >= to the input.</summary>
        /// <remarks>
        /// The set of supported is primes is by no means exhaustive and represents a subset useful for use with
        /// hash tables of variable sizes.
        /// </remarks>
        public static int GetGreaterOrEqualPrime(int minValue)
        {
            Contract.Requires(minValue >= 0);
            Contract.Ensures(Contract.Result<int>() > 0);

            for (int i = 0; i < s_primes.Length; i++)
            {
                int prime = s_primes[i];
                Contract.Assume(prime > 0);
                if (prime >= minValue)
                {
                    return prime;
                }
            }

            // 2**31 - 1, which is prime
            return 2147483647;
        }
    }
}
