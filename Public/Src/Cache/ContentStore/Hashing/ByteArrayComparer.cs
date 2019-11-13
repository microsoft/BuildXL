// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

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
        public int Compare(byte[] x, byte[] y)
        {
            return CompareArrays(x, y);
        }

        /// <summary>
        /// IEqualityComparer.Equal.
        /// </summary>
        public bool Equals(byte[] x, byte[] y)
        {
            return ArraysEqual(x, y);
        }

        /// <summary>
        ///     IEqualityComparer.GetHashCode.
        ///     Must return the same hash codes for equal byte arrays.
        ///     From the web - Bob Jenkins' Hash algorithm
        /// </summary>
        public int GetHashCode(byte[] obj)
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
        private static int CompareArrays(byte[] x, byte[] y)
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
#if NET_COREAPP
        public static bool ArraysEqual(byte[] x, byte[] y)
        {
            return x.AsSpan().SequenceEqual(y.AsSpan());
        }
#else
        public static unsafe bool ArraysEqual(byte[] x, byte[] y)
        {
            // Adapted from: https://gist.github.com/airbreather/90c5fd3ba9d77fcd7c106db3beeb569b
            // (https://stackoverflow.com/questions/43289/comparing-two-byte-arrays-in-net, Joe Amenta's answer).
            
            // Copyright (c) 2008-2013 Hafthor Stefansson
            // Distributed under the MIT/X11 software license
            // Ref: http://www.opensource.org/licenses/mit-license.php.
            if (x == null)
            {
                return (y == null);
            }
            if (y == null || x.Length != y.Length)
            {
                return false;
            }

            fixed (byte* p1 = x, p2 = y)
            {
                byte* x1 = p1, x2 = p2;
                int l = x.Length;
                for (int i = 0; i < l / 8; i++, x1 += 8, x2 += 8)
                {
                    if (*((long*)x1) != *((long*)x2))
                    {
                        return false;
                    }
                }

                if ((l & 4) != 0)
                {
                    if (*((int*)x1) != *((int*)x2))
                    {
                        return false;
                    }

                    x1 += 4; x2 += 4;
                }

                if ((l & 2) != 0)
                {
                    if (*((short*)x1) != *((short*)x2))
                    {
                        return false;
                    }

                    x1 += 2; x2 += 2;
                }

                if ((l & 1) != 0)
                {
                    if (*x1 != *x2)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
#endif
    }
}
