// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class CounterCollectionTests
    {
        [Fact]
        public void SingleCounter()
        {
            var collection = new CounterCollection(1);
            XAssert.AreEqual(0, collection.GetCounterValueInternal(0));
            collection.AddToCounterInternal(0, 123);
            collection.AddToCounterInternal(0, 1);
            XAssert.AreEqual(124, collection.GetCounterValueInternal(0));
        }

        [Fact]
        public void SingleCounterOverflow()
        {
            var collection = new CounterCollection(1);
            XAssert.AreEqual(0, collection.GetCounterValueInternal(0));
            collection.AddToCounterInternal(0, long.MaxValue - 3);
            collection.AddToCounterInternal(0, 3);

            ExpectOverflow(
                () =>
                {
                    // Since the counter is sharded, overflow may manifest in Add (single shard overflow) or Get (overflow of shards only in aggregate).
                    collection.AddToCounterInternal(0, 1);
                    collection.GetCounterValueInternal(0);
                });
        }

        [Fact]
        public void SingleCounterUnderflow()
        {
            var collection = new CounterCollection(1);
            XAssert.AreEqual(0, collection.GetCounterValueInternal(0));
            collection.AddToCounterInternal(0, long.MinValue + 3);
            collection.AddToCounterInternal(0, -3);

            ExpectOverflow(
                () =>
                {
                    // Since the counter is sharded, overflow may manifest in Add (single shard overflow) or Get (overflow of shards only in aggregate).
                    collection.AddToCounterInternal(0, -1);
                    collection.GetCounterValueInternal(0);
                });
        }

        private void ExpectOverflow(Action action)
        {
            try
            {
                action();
            }
            catch (OverflowException)
            {
                return;
            }

            XAssert.Fail("Expected counter overflow / underflow.");
        }

        [Fact]
        public void MultipleCounters()
        {
            const ushort NumCounters = 65; // Not a multiple of 64
            var collection = new CounterCollection(NumCounters);

            for (ushort i = 0; i < NumCounters; i++)
            {
                XAssert.AreEqual(0, collection.GetCounterValueInternal(i));
                collection.AddToCounterInternal(i, i);
                XAssert.AreEqual(i, collection.GetCounterValueInternal(i));
            }
        }

        private enum TestCounters
        {
            [CounterType(CounterType.Stopwatch)]
            SomeTime = 3,
            SomeCount = 5,
        }

        [Fact]
        public void EnumCounters()
        {
            var collection = new CounterCollection<TestCounters>();
            XAssert.IsTrue(CounterCollection<TestCounters>.IsStopwatch(TestCounters.SomeTime));
            XAssert.IsFalse(CounterCollection<TestCounters>.IsStopwatch(TestCounters.SomeCount));

            // Increase the time
            using (collection.StartStopwatch(TestCounters.SomeTime))
            {
                Thread.Sleep(1);
            }

            TimeSpan elapsed = collection.GetElapsedTime(TestCounters.SomeTime);
            XAssert.IsTrue(elapsed.Ticks > 0);
            XAssert.AreEqual<long>(0, collection.GetCounterValue(TestCounters.SomeCount));

            collection.AddToCounter(TestCounters.SomeCount, 2);
            XAssert.AreEqual(elapsed, collection.GetElapsedTime(TestCounters.SomeTime));
            XAssert.AreEqual<long>(2, collection.GetCounterValue(TestCounters.SomeCount));

            TimeSpan delta = new TimeSpan(100);
            collection.AddToCounter(TestCounters.SomeTime, delta);
            TimeSpan newElapsed = collection.GetElapsedTime(TestCounters.SomeTime);
            // note: must not check if `newElapsed == elapsed.Add(delta)` because
            //       `AddToCounter(); GetElapsedTime()` is lossy due to float-point arithmetic
            XAssert.IsTrue(newElapsed > elapsed);
        }

        [Fact]
        public void Temporal()
        {
            var collection = new CounterCollection<TestCounters>();

            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 300)
            {
                using (collection.StartStopwatch(TestCounters.SomeTime))
                {
                    Thread.Sleep(30);
                }
            }

            sw.Stop();

            // These times won't be exactly the same since the counter stopwatch is only measuring the sleep, not the loop and counter manipulation etc.
            // In any of those steps we can get unlucky and be descheduled. But a large discrepancy is in high likelihood some serious fault.
            TimeSpan delta = sw.Elapsed - collection.GetElapsedTime(TestCounters.SomeTime);
            XAssert.IsTrue(
                delta < 100.MillisecondsToTimeSpan(),
                "Difference between System.Diagnostics.Stopwatch and the Counter stopwatch was very large");
        }

        [Fact]
        public void SingleCounterWithContention()
        {
            var collection = new CounterCollection(1);
            XAssert.AreEqual(0, collection.GetCounterValueInternal(0));

            var threads = new Thread[Environment.ProcessorCount];
            long expected = 0;
            bool[] allStarted = new[] { false };
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(
                    () =>
                    {
                        long thisThreadAdded = 0;
                        while (!Volatile.Read(ref allStarted[0]))
                        {
                            collection.AddToCounterInternal(0, 1);
                            thisThreadAdded++;
                        }

                        for (int j = 0; j < 30; j++)
                        {
                            collection.AddToCounterInternal(0, 1);
                            thisThreadAdded++;
                        }

                        Interlocked.Add(ref expected, thisThreadAdded);
                    });
                threads[i].Start();
            }

            Volatile.Write(ref allStarted[0], true);

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i].Join();
            }

            XAssert.AreEqual(expected, collection.GetCounterValueInternal(0));
        }
    }
}
