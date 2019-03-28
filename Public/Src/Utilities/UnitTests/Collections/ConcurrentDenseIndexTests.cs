// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class ConcurrentDenseIndexTests
    {
        [Fact]
        public void Linear()
        {
            var c = new ConcurrentDenseIndex<int>(true);
            for (int i = 0; i < 160000; i++)
            {
                c[(uint)i] = i;
            }

            for (int i = 0; i < 160000; i++)
            {
                XAssert.AreEqual(i, c[(uint)i]);
            }

            XAssert.AreEqual(
                (160000 + ConcurrentDenseIndex<int>.DefaultBuffersCount - 1) / ConcurrentDenseIndex<int>.DefaultBuffersCount,
                c.BuffersCount);
        }

        [Fact]
        public void RandomAccess()
        {
            var c = new ConcurrentDenseIndex<int>(true);
            var r = new Random(0);
            for (int i = 0; i < 100; i++)
            {
                var index = unchecked((uint)r.Next(int.MinValue, int.MaxValue));
                c[index] = i;
            }

            r = new Random(0);
            for (int i = 0; i < 100; i++)
            {
                var index = unchecked((uint)r.Next(int.MinValue, int.MaxValue));
                XAssert.AreEqual(i, c[index]);
            }
        }

        [Fact]
        public async Task RandomAccessMultithreadedAsync()
        {
            var c = new ConcurrentDenseIndex<int>(true);
            var r = new Random(0);
            var tasks = new Task[100];
            var mre = new ManualResetEvent(false);
            for (int i = 0; i < tasks.Length; i++)
            {
                var index = unchecked((uint)r.Next(int.MinValue, int.MaxValue));
                int j = i;
                tasks[j] = Task.Run(
                    () =>
                    {
                        mre.WaitOne();
                        c[index] = j;
                    });
            }

            mre.Set();
            foreach (Task t in tasks)
            {
                await t;
            }

            r = new Random(0);
            for (int i = 0; i < tasks.Length; i++)
            {
                var index = unchecked((uint)r.Next(int.MinValue, int.MaxValue));
                int j = i;
                tasks[j] = Task.Run(
                    () =>
                    {
                        mre.WaitOne();
                        XAssert.AreEqual(c[index], j);
                    });
            }

            foreach (Task t in tasks)
            {
                await t;
            }
        }
    }
}
