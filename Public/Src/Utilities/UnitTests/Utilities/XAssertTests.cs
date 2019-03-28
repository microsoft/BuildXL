// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class XAssertTests
    {
        [Fact]
        public void TestArrayEqualSimple()
        {
            var len0Arr = new string[0];
            var len1Arr = new[] { "str1" };
            var len2Arr = new[] { "str1", "str2" };

            XAssert.ArrayEqual<object>(null, null);

            XAssert.ArrayNotEqual(len1Arr, null);
            XAssert.ArrayNotEqual(null, len1Arr);

            XAssert.ArrayEqual(len0Arr, len0Arr);
            XAssert.ArrayEqual(len1Arr, len1Arr);

            XAssert.ArrayNotEqual(len0Arr, len1Arr);
            XAssert.ArrayNotEqual(len1Arr, len0Arr);
            XAssert.ArrayNotEqual(new[] { "another string" }, len1Arr);
            XAssert.ArrayNotEqual(len2Arr, len1Arr);

            XAssert.ArrayNotEqual(new[] { new ObjectWithBogusToString(1) }, null);
            XAssert.ArrayNotEqual(new[] { new ObjectWithBogusToString(1) }, new[] { new ObjectWithBogusToString(2) });
            XAssert.ArrayEqual(new[] { new ObjectWithBogusToString(1) }, new[] { new ObjectWithBogusToString(1) });
        }

        [Theory]
        [InlineData(3)]
        public void TestCharArrayEqualExhaustive(int bitwidth)
        {
            int max = 1 << bitwidth;
            EnumerateArrayPairs(
                max,
                i => Convert.ToString(i, 2).ToCharArray(),
                (i, j, arrI, arrJ) =>
                {
                    XAssert.AreArraysEqual(arrI, arrJ, i == j);
                });
        }

        [Theory]
        [InlineData(3)]
        public void TestObjectArrayEqualExhaustive(int bitwidth)
        {
            int max = 1 << bitwidth;
            EnumerateArrayPairs(
                max,
                i => Convert.ToString(i, 2).ToCharArray().Select(c => c == '0' ? null : new ObjectWithBogusToString(c)),
                (i, j, arrI, arrJ) =>
                {
                    XAssert.AreArraysEqual(arrI, arrJ, i == j);
                });
        }

        private static void EnumerateArrayPairs<T>(int max, Func<int, IEnumerable<T>> constructor, Action<int, int, T[], T[]> action)
        {
            Func<int, Dictionary<int, T[]>> toArrayMapFunc = (n) => Enumerable
                .Range(0, n)
                .ToDictionary(i => i, i => constructor(i).ToArray());

            // create 2 different maps so that arrays passed to 'action' are not aliased
            var arrayMapI = toArrayMapFunc(max);
            var arrayMapJ = toArrayMapFunc(max);
            for (int i = 0; i < max; i++)
            {
                for (int j = 0; j < max; j++)
                {
                    action(i, j, arrayMapI[i], arrayMapI[j]);
                }
            }
        }
    }

    internal class ObjectWithBogusToString
    {
        private readonly int m_value;

        public ObjectWithBogusToString(int c)
        {
            m_value = c;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            return m_value == ((ObjectWithBogusToString)obj).m_value;
        }

        public override int GetHashCode()
        {
            return m_value.GetHashCode();
        }

        public override string ToString()
        {
            throw new Exception("Bogus");
        }
    }
}
