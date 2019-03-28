// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class ConcurrentPriorityQueueTest : XunitBuildXLTest
    {
        public ConcurrentPriorityQueueTest(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public async Task ConcurrentEnqueueSequentialDequeue()
        {
            var q = new ConcurrentPriorityQueue<int>();
            var values = new int[10000];
            var r = new Random(0);
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = 0x7FFFFFFF & r.Next();
            }

            await Task.WhenAll(values.Select(value => Task.Run(() => q.Enqueue(value, value))));
            int dequeuedPriority, dequeuedValue;
            var dequeuedValues = new List<int>();
            while (q.TryDequeue(out dequeuedPriority, out dequeuedValue))
            {
                dequeuedValues.Add(dequeuedValue);
            }

            Array.Sort(values);
            dequeuedValues.Sort();
            XAssert.IsTrue(values.SequenceEqual(dequeuedValues));
        }

        [Fact]
        public void Priorities()
        {
            var q = new ConcurrentPriorityQueue<int>();
            var values = new int[10000];
            var r = new Random(0);
            for (int i = 0; i < values.Length; i++)
            {
                int value;
                switch (r.Next(3))
                {
                    case 0:
                        value = 0;
                        break;
                    case 1:
                        value = int.MaxValue;
                        break;
                    default:
                        value = 0x7FFFFFFF & r.Next();
                        break;
                }

                values[i] = value;
                q.Enqueue(value, value);
            }

            Array.Sort(values);
            Array.Reverse(values);
            int dequeuedPriority0, dequeuedValue0, dequeuedPriority1, dequeuedValue1;
            foreach (int t in values)
            {
                XAssert.IsTrue(q.TryPeek(out dequeuedPriority0, out dequeuedValue0));
                XAssert.IsTrue(q.TryDequeue(out dequeuedPriority1, out dequeuedValue1));
                XAssert.AreEqual(dequeuedPriority0, dequeuedPriority0);
                XAssert.AreEqual(dequeuedPriority1, dequeuedPriority1);
                XAssert.AreEqual(t, dequeuedValue1);
            }

            XAssert.IsFalse(q.TryPeek(out dequeuedPriority0, out dequeuedValue0));
            XAssert.IsFalse(q.TryDequeue(out dequeuedPriority1, out dequeuedValue1));
        }
    }
}
