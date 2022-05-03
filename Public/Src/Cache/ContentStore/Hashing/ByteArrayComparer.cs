// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Utils
{
    /// <summary>
    ///     Helper class for comparing multiple byte arrays.
    /// </summary>
    public sealed class ByteArrayComparer : IComparer<byte[]>, IEqualityComparer<byte[]>
    {
        /// <summary>
        ///     A convenient ready-made instance.
        /// </summary>
        public static readonly ByteArrayComparer Instance = new ByteArrayComparer();

        /// <summary>
        ///     Compare two byte arrays.
        /// </summary>
        public int Compare(byte[]? x, byte[]? y)
        {
            return CompareArrays(x, y);
        }

        /// <summary>
        /// IEqualityComparer.Equal.
        /// </summary>
        public bool Equals(byte[]? x, byte[]? y)
        {
            return ArraysEqual(x, y);
        }

        /// <summary>
        /// IEqualityComparer.GetHashCode.
        /// Must return the same hash codes for equal byte arrays.
        /// NET6+ version uses HashCode.AddBytes and for the older runtimes - Bob Jenkins' Hash algorithm
        /// </summary>
#if NET6_0_OR_GREATER
        public int GetHashCode(byte[]? obj)
        {
            if (obj == null)
            {
                return 0;
            }

            // Using a new API available only in .net 6 for performance purposes.
            // Unlike the custom implementation AddBytes uses vectorized instructions that gives 2-4x performance improvements
            // without sacrificing the distribution of the hash codes.

            var hashCode = new HashCode();
            hashCode.AddBytes(obj.AsSpan());
            return hashCode.ToHashCode();
        }
#else
        public int GetHashCode(byte[]? obj)
        {
            if (obj == null)
            {
                return 0;
            }

            unchecked {
                int len = obj.Length;
                uint b = 0x9e3779b9;
                uint a = b;
                uint c = 0;
                int i = 0;
                while (i + 12 <= len)
                {
                    a += obj[i++] |
                        ((uint)obj[i++] << 8) |
                        ((uint)obj[i++] << 16) |
                        ((uint)obj[i++] << 24);
                    b += obj[i++] |
                        ((uint)obj[i++] << 8) |
                        ((uint)obj[i++] << 16) |
                        ((uint)obj[i++] << 24);
                    c += obj[i++] |
                        ((uint)obj[i++] << 8) |
                        ((uint)obj[i++] << 16) |
                        ((uint)obj[i++] << 24);
                    Mix(ref a, ref b, ref c);
                }

                c += (uint)len;
                if (i < len)
                {
                    a += obj[i++];
                }

                if (i < len)
                {
                    a += (uint)obj[i++] << 8;
                }

                if (i < len)
                {
                    a += (uint)obj[i++] << 16;
                }

                if (i < len)
                {
                    a += (uint)obj[i++] << 24;
                }

                if (i < len)
                {
                    b += obj[i++];
                }

                if (i < len)
                {
                    b += (uint)obj[i++] << 8;
                }

                if (i < len)
                {
                    b += (uint)obj[i++] << 16;
                }

                if (i < len)
                {
                    b += (uint)obj[i++] << 24;
                }

                if (i < len)
                {
                    c += (uint)obj[i++] << 8;
                }

                if (i < len)
                {
                    c += (uint)obj[i++] << 16;
                }

                if (i < len)
                {
                    c += (uint)obj[i] << 24;
                }

                Mix(ref a, ref b, ref c);
                return (int)c;
            }
        }
#endif

        // Bob Jenkins Hash algorithm - worker method.
        private static void Mix(ref uint a, ref uint b, ref uint c)
        {
            unchecked {
                a -= b;
                a -= c;
                a ^= c >> 13;
                b -= c;
                b -= a;
                b ^= a << 8;
                c -= a;
                c -= b;
                c ^= b >> 13;
                a -= b;
                a -= c;
                a ^= c >> 12;
                b -= c;
                b -= a;
                b ^= a << 16;
                c -= a;
                c -= b;
                c ^= b >> 5;
                a -= b;
                a -= c;
                a ^= c >> 3;
                b -= c;
                b -= a;
                b ^= a << 10;
                c -= a;
                c -= b;
                c ^= b >> 15;
            }
        }

        /// <summary>
        ///     Static comparison method for when the caller does not want to create an instance
        ///     of this class.
        /// </summary>
        private static int CompareArrays(byte[]? x, byte[]? y)
        {
            // Non-null is greater than null; both null is equal.
            if (x == null)
            {
                if (y == null)
                {
                    return 0;
                }

                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            int comp = x.Length.CompareTo(y.Length);
            if (comp == 0)
            {
                for (int i = 0; i < x.Length; i++)
                {
                    comp = x[i].CompareTo(y[i]);
                    if (comp != 0)
                    {
                        break;
                    }
                }
            }

            return comp;
        }

        /// <summary>
        ///     Static comparison method for when the caller does not want to create an instance
        ///     of this class.
        /// </summary>
        public static bool ArraysEqual(byte[]? x, byte[]? y)
        {
            if (x is null)
            {
                return y is null;
            }

            if (y is null)
            {
                return x is null;
            }

            return x.AsSpan().SequenceEqual(y.AsSpan());
        }
    }
}
