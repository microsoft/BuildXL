// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Scheduler.WorkDispatcher;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    public class PriorityQueueTest
    {
        [Fact]
        public async Task ConcurrentEnqueueSequentialDequeue()
        {
            var q = new PriorityQueue<int>();
            var values = new int[10000];
            var r = new Random(0);
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = 0x7FFFFFFF & r.Next();
            }

            await Task.WhenAll(values.Select(value => Task.Run(() => q.Enqueue(value, value))));
            var dequeuedValues = new List<int>();

            q.ProcessItems((int priority, int item, out bool stopProcessing) =>
            {
                stopProcessing = false;
                dequeuedValues.Add(item);
                return true;
            });

            Array.Sort(values);
            Array.Reverse(values);
            XAssert.IsTrue(values.SequenceEqual(dequeuedValues));
        }

        [Fact]
        public void Priorities()
        {
            var q = new PriorityQueue<int>();
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

            XAssert.AreEqual(values.Length, q.Count);

            Array.Sort(values);
            Array.Reverse(values);
            int idx = 0;

            q.ProcessItems((int priority, int item, out bool stopProcessing) =>
            {
                stopProcessing = false;
                XAssert.IsTrue(idx < values.Length);
                XAssert.IsTrue(values[idx] == item);
                ++idx;
                return true;
            });

            XAssert.AreEqual(values.Length, idx);
            XAssert.AreEqual(0, q.Count);
        }

        [Fact]
        public void ProcessItems()
        {
            var q = new PriorityQueue<int>();
            q.Enqueue(3, 30);
            q.Enqueue(1, 10);
            q.Enqueue(2, 20);
            q.Enqueue(2, 21);
            XAssert.AreEqual(4, q.Count);

            // The processing delegate will process 30 and 21. It skips 20 and stops processing after 21.
            q.ProcessItems((int priority, int item, out bool stopProcessing) =>
            {
                stopProcessing = false;
                switch (item)
                {
                    case 10:
                        break;
                    case 20:
                        return false;
                    case 21:
                        stopProcessing = true;
                        return true;
                    case 30:
                        return true;
                    default:
                        break;
                }

                XAssert.Fail("Unexpected item: {0}", item);
                return false;
            });

            XAssert.AreEqual(2, q.Count);

            q.ProcessItems((int priority, int item, out bool stopProcessing) =>
            {
                stopProcessing = false;
                XAssert.IsTrue(item == 10 || item == 20);
                return false;
            });

            XAssert.AreEqual(2, q.Count);
        }

        [Fact]
        public void AddAfterProcess()
        {
            var q = new PriorityQueue<int>();
            q.Enqueue(3, 3);
            q.Enqueue(1, 1);
            q.Enqueue(4, 4);
            q.Enqueue(2, 2);
            q.Enqueue(4, 4);
            XAssert.AreEqual(5, q.Count);

            // Process both 4s and 2 but skip 3. Stop after 2.
            q.ProcessItems((int priority, int item, out bool stopProcessing) =>
            {
                stopProcessing = false;
                switch (item)
                {
                    case 4:
                        return true;
                    case 3:
                        return false;
                    case 2:
                        stopProcessing = true;
                        return true;
                    default:
                        break;
                }

                XAssert.Fail("Unexpected item: {0}", item);
                return false;
            });

            XAssert.AreEqual(2, q.Count);

            // Insert new items at both ends
            q.Enqueue(5, 5);
            q.Enqueue(0, 0);
            q.Enqueue(4, 4);

            int[] values = { 5, 4, 3, 1, 0 };
            int idx = 0;

            q.ProcessItems((int priority, int item, out bool stopProcessing) =>
            {
                stopProcessing = false;
                XAssert.IsTrue(idx < values.Length);
                XAssert.IsTrue(values[idx] == item);
                ++idx;
                return true;
            });

            XAssert.AreEqual(values.Length, idx);
            XAssert.AreEqual(0, q.Count);
        }

        [Fact]
        public void ProcessItemsLock()
        {
            var q = new PriorityQueue<int>();
            q.Enqueue(1, 10);

            q.ProcessItems((int priority, int item, out bool stopProcessing) =>
            {
                VerifyExceptionThrown(() => q.Enqueue(2, 20));
                VerifyExceptionThrown(() => q.ProcessItems((int pr, int it, out bool stopProc) => { stopProc = true;
                    return false; }));
                stopProcessing = true;
                return false;
            });
        }

        private static void VerifyExceptionThrown(Action action)
        {
            try
            {
                action();
                XAssert.Fail("Expected exception");
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
