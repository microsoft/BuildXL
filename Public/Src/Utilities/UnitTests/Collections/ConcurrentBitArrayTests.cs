// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests for <see cref="BitSet" />
    /// </summary>
    public class ConcurrentBitArrayTests
    {
        [Fact]
        public void TestBitArrayOperations()
        {
            TestOperationsHelper(parallel: false);
        }

        [Fact]
        public void TestConcurrentBitArrayOperations()
        {
            TestOperationsHelper(parallel: true);
        }

        private static void TestOperationsHelper(bool parallel)
        {
            var length = 256;
            var mod5Array = new ConcurrentBitArray(12);
            var mod8Array = new ConcurrentBitArray(length);

            mod5Array.UnsafeSetLength(length);

            XAssert.AreEqual(length, mod5Array.Length);

            // Verify that all bits start off with the default value (false in this case)
            For(length, i =>
            {
                XAssert.IsFalse(mod5Array[i]);
            }, parallel);

            // Verify setting bits
            For(length, i =>
            {
                mod5Array[i] = i % 5 == 0;
                XAssert.AreEqual(mod5Array[i], i % 5 == 0);
            }, parallel);

            For(length, i =>
                {
                    mod8Array[i] = i % 8 == 0;
                    XAssert.AreEqual(mod8Array[i], i % 8 == 0);
                }, parallel);

            var orArray = new ConcurrentBitArray(length);
            orArray.Or(mod5Array);
            orArray.Or(mod8Array);

            For(length, i => XAssert.AreEqual(orArray[i], i % 5 == 0 || i % 8 == 0), parallel);

            var andArray = new ConcurrentBitArray(length);
            andArray.Or(mod5Array);
            andArray.And(mod8Array);

            For(length, i => XAssert.AreEqual(andArray[i], i % 5 == 0 && i % 8 == 0), parallel);
        }

        private static void For(int count, Action<int> action, bool parallel)
        {
            if (parallel)
            {
                Parallel.For(0, count, action);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    action(i);
                }
            }
        }
    }
}
