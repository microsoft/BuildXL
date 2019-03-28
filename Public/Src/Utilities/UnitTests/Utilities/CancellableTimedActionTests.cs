// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests for <see cref="CancellableTimedAction"/>.
    /// </summary>
    public class CancellableTimedActionTests
    {
        [Fact]
        public void BasicFunctionalityTest()
        {
            var mre = new ManualResetEvent(false);
            int counter = 0;

            using (var timedAction = new CancellableTimedAction(() =>
            {
                // Signal that thread is started
                mre.Set();
                ++counter;
            }, 10, nameof(BasicFunctionalityTest)))
            {
                XAssert.IsTrue(timedAction.Start());

                // Wait for thread to start
                mre.WaitOne();

                Thread.Sleep(500);
                timedAction.Cancel();
                timedAction.Join();
                XAssert.IsTrue(counter > 2, "Value of counter is " + counter);
            }
        }

        [Fact]
        public void MultipleActionStartsTest()
        {
            var mre = new ManualResetEvent(false);
            int counter = 0;

            using (var timedAction = new CancellableTimedAction(() => 
            {
                // Signal that thread is started
                mre.Set();
                ++counter;
            }, 10, nameof(MultipleActionStartsTest)))
            {
                XAssert.IsTrue(timedAction.Start());
                XAssert.IsFalse(timedAction.Start());
                Thread.Sleep(500);
                XAssert.IsFalse(timedAction.Start());
                timedAction.Cancel();
                timedAction.Join();
                XAssert.IsTrue(counter > 2, "Value of counter is " + counter);
            }
        }

        [Fact]
        public void CancellingNotStartedActionTest()
        {
            int counter = 0;
            using (var timedAction = new CancellableTimedAction(() => ++counter, 10, nameof(CancellingNotStartedActionTest)))
            {
                Thread.Sleep(500);
                timedAction.Cancel();
                timedAction.Join();
                XAssert.AreEqual(0, counter);
            }
        }

        [Fact]
        public void LongIntervalTest()
        {
            int counter = 0;
            using (var timedAction = new CancellableTimedAction(() => Interlocked.Increment(ref counter), 1000, nameof(LongIntervalTest)))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                XAssert.IsTrue(timedAction.Start());
                Thread.Sleep(100);
                timedAction.Cancel();
                timedAction.Join();
                sw.Stop();
                XAssert.AreEqual(1, counter);
                XAssert.IsTrue(sw.ElapsedMilliseconds < 1000);
            }
        }
    }
}
