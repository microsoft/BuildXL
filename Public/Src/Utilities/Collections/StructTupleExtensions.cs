// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
{
    /// <nodoc />
    public static class StructTupleExtensions
    {
        /// <nodoc />
        public static void Deconstruct<T1, T2, T3, T4>(this StructTuple<T1, T2, T3, T4> value, out T1 item1, out T2 item2, out T3 item3, out T4 item4)
        {
            item1 = value.Item1;
            item2 = value.Item2;
            item3 = value.Item3;
            item4 = value.Item4;
        }

        /// <nodoc />
        public static void Deconstruct<T1, T2, T3>(this StructTuple<T1, T2, T3> value, out T1 item1, out T2 item2, out T3 item3)
        {
            item1 = value.Item1;
            item2 = value.Item2;
            item3 = value.Item3;
        }

        /// <nodoc />
        public static void Deconstruct<T1, T2>(this StructTuple<T1, T2> value, out T1 item1, out T2 item2)
        {
            item1 = value.Item1;
            item2 = value.Item2;
        }
    }
}
