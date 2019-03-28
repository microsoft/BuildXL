// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class ConcurrentDrainingPriorityQueueTest : XunitBuildXLTest
    {
        public ConcurrentDrainingPriorityQueueTest(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public async Task Empty()
        {
            Func<int, Task> taskCreator = _ => Task.Run(() => { });
            using (var q = new ConcurrentDrainingPriorityQueue<int, Task>(taskCreator))
            {
                XAssert.AreEqual(-1, q.MaxDegreeOfParallelism);
                await q.WhenDone();
                XAssert.AreEqual(0, q.PriorityQueued);
                XAssert.AreEqual(0, q.SemaphoreQueued);
                XAssert.AreEqual(0, q.Running);
                XAssert.AreEqual(0, q.MaxRunning);
            }
        }

        public async Task FaultyTasks()
        {
            Func<int, Task> taskCreator = _ => Task.Run(() => { throw new Exception(); });
            using (var q = new ConcurrentDrainingPriorityQueue<int, Task>(taskCreator))
            {
                const int Expected = 42;
                int actual = 0;
                q.ItemCompleted += (sender, e) =>
                                   {
                                       e.IsExceptionHandled = true;
                                       if (e.Task.IsFaulted)
                                       {
                                           Interlocked.Increment(ref actual);
                                       }
                                   };
                for (int i = 0; i < Expected; i++)
                {
                    q.Enqueue(int.MaxValue, i);
                }

                await q.WhenDone();
                XAssert.AreEqual(Expected, actual);
            }
        }

        [Fact]
        public async Task Basic()
        {
            await Task.WhenAll(
                new[] { 1, 2, Environment.ProcessorCount, Environment.ProcessorCount * 2, -1 }.Select(
                    async maxDegreeOfParallelism =>
                          {
                              const int n = 1 << 11;
                              int count = 0;
                              Func<int, Task> taskCreator =
                                  _ => Task.Run(() => { Interlocked.Increment(ref count); });
                              using (var q = new ConcurrentDrainingPriorityQueue<int, Task>(taskCreator, maxDegreeOfParallelism))
                              {
                                  for (int i = 0; i < n; i++)
                                  {
                                      q.Enqueue(i, i);
                                  }

                                  await q.WhenDone();
                                  XAssert.AreEqual(0, q.PriorityQueued);
                                  XAssert.AreEqual(0, q.SemaphoreQueued);
                                  XAssert.AreEqual(0, q.Running);
                                  XAssert.AreEqual(n, count);
                                  XAssert.IsTrue(
                                      maxDegreeOfParallelism == -1 ||
                                      q.MaxRunning <= maxDegreeOfParallelism);
                              }
                          }));
        }

        public async Task MaxRunning()
        {
            const int N = 42;
            var e = new TaskCompletionSource<int>();
            Func<int, Task> taskCreator = _ => e.Task;
            using (var q = new ConcurrentDrainingPriorityQueue<int, Task>(taskCreator, N))
            {
                for (int i = 0; i < N; i++)
                {
                    q.Enqueue(i, i);
                }

                await q.WhenQueueDrained();
                e.SetCanceled();
                await q.WhenDone();
                XAssert.AreEqual(N, q.MaxRunning);
            }
        }

        public async Task EnqueueDuringRun()
        {
            await Task.WhenAll(
                new[] { 1, 2, Environment.ProcessorCount, Environment.ProcessorCount * 2, -1 }.Select(
                    async maxDegreeOfParallelism =>
                          {
                              const int n = 11;
                              ConcurrentDrainingPriorityQueue<int, Task> q = null;
                              int count = 0;
                              Func<int, Task> taskCreator = m => Task.Run(
                                  () =>
                                  {
                                      Interlocked.Increment(ref count);
                                      while (--m >= 0)
                                      {
                                          q.Enqueue(m, m);
                                      }
                                  });
                              using (q = new ConcurrentDrainingPriorityQueue<int, Task>(taskCreator, maxDegreeOfParallelism))
                              {
                                  for (int i = 0; i < n; i++)
                                  {
                                      q.Enqueue(i, i);
                                  }

                                  await q.WhenDone();
                                  XAssert.AreEqual(0, q.PriorityQueued);
                                  XAssert.AreEqual(0, q.SemaphoreQueued);
                                  XAssert.AreEqual(0, q.Running);
                                  XAssert.AreEqual((1 << n) - 1, count);
                              }
                          }));
        }

        public async Task Priorities()
        {
            var dequeuedValues = new List<int>();
            Func<int, Task> taskCreator = dequeuedValue => Task.Run(() => dequeuedValues.Add(dequeuedValue));

            // start queue in "paused" state with 0 parallelism
            using (var q = new ConcurrentDrainingPriorityQueue<int, Task>(taskCreator, 0))
            {
                var values = new int[1 << 11];
                var r = new Random(0);
                for (int i = 0; i < values.Length; i++)
                {
                    int value = 0x7FFFFFFF & r.Next();
                    values[i] = value;
                    q.Enqueue(value, value);
                }

                // set queue to "sequential" mode with 1 degree of parallelism
                q.MaxDegreeOfParallelism = 1;
                await q.WhenDone();
                Array.Sort(values);
                Array.Reverse(values);
                XAssert.AreEqual(values.Length, dequeuedValues.Count);
                for (int i = 0; i < values.Length; i++)
                {
                    XAssert.AreEqual(values[i], dequeuedValues[i]);
                }

                XAssert.AreEqual(1, q.MaxRunning);
            }
        }

        public async Task DisposeIsHarmless()
        {
            Func<int, Task> taskCreator = m => Task.Run(() => XAssert.Fail());

            // start queue in "paused" state with 0 parallelism
            var q = new ConcurrentDrainingPriorityQueue<int, Task>(taskCreator, 0);
            q.Enqueue(0, 0);
            q.Dispose();
            await Task.Yield();

            // even after calling Dispose, all public members are still callable

            // set queue to "sequential" mode with 1 degree of parallelism
            q.MaxDegreeOfParallelism = 1;
            q.Enqueue(0, 0);
            await Task.Yield();
            XAssert.AreEqual(0, q.MaxRunning);
            XAssert.AreEqual(1, q.MaxDegreeOfParallelism);
            XAssert.AreEqual(2, q.PriorityQueued);
            XAssert.AreEqual(0, q.SemaphoreQueued);
            XAssert.AreEqual(0, q.Running);

            // no tasks should ever have started running, and thus all running tasks have completed
            await q.WhenAllTasksCompleted();

            // however, some When...tasks will never complete since we had a pending task in the queue that got never scheduled
            XAssert.IsFalse(q.WhenDone().IsCompleted);
            XAssert.IsFalse(q.WhenQueueDrained().IsCompleted);
        }

        public async Task Semaphores()
        {
            var semaphoreLimits = new int[] { 1, 2, 3, 4 };
            SemaphoreSet<int> semaphores = new SemaphoreSet<int>();
            Func<int, ItemResources> itemResourceGetter =
                index =>
                {
                    int[] semaphoreIncrements;
                    switch (index)
                    {
                        case 2:
                        case 4:
                        case 6:
                            semaphoreIncrements = new[] { 0, 1 };
                            break;
                        default:
                            semaphoreIncrements = null;
                            break;
                    }

                    return ItemResources.Create(semaphoreIncrements);
                };

            var created = new TaskCompletionSource<int>[10];
            var completed = new TaskCompletionSource<int>[10];
            for (int i = 0; i < completed.Length; i++)
            {
                completed[i] = new TaskCompletionSource<int>();
                created[i] = new TaskCompletionSource<int>();
            }

            Func<int, Task> taskCreator = index =>
                                          {
                                              created[index].SetResult(index);
                                              return completed[index].Task;
                                          };
            using (
                var q = new ConcurrentDrainingPriorityQueue<int, Task>(
                    taskCreator,
                    maxDegreeOfParallelism: 0,
                    itemResourceGetter: itemResourceGetter,
                    semaphores: semaphores))
            using (var item6Queued = new ManualResetEvent(false))
            using (var item6Dequeued = new ManualResetEvent(false))
            {
                q.ItemSemaphoreQueued += (sender, e) =>
                                         {
                                             XAssert.AreEqual(e.Item, 6);
                                             item6Queued.Set();
                                         };

                q.ItemSemaphoreDequeued += (sender, e) =>
                                           {
                                               XAssert.AreEqual(e.Item, 6);
                                               item6Dequeued.Set();
                                           };

                for (int i = 0; i < 10; i++)
                {
                    q.Enqueue(10 - i, i);
                }

                for (int i = 0; i < semaphoreLimits.Length; i++)
                {
                    int index = semaphores.CreateSemaphore(i, semaphoreLimits[i]);
                    XAssert.AreEqual(index, i);
                }

                q.MaxDegreeOfParallelism = 3;
                completed[0].SetResult(0);
                completed[1].SetResult(1);

                // skip 2, building up resource use...
                completed[3].SetResult(3);

                // skip 4, building up resource use...
                completed[5].SetResult(5);

                for (int i = 0; i < 5; i++)
                {
                    await created[i].Task;
                }

                // item 6 cannot run, as 2 and 4 are using up the two semaphore slots
                XAssert.IsFalse(created[6].Task.IsCompleted);

                // Don't block, use await --- otherwise deadlock might arise as TPL may schedule a task continuation on the current thread.
                await item6Queued.ToTask();

                completed[2].SetResult(2);

                // now that item 2 has finished, item 6 can dequeue and run...

                // Don't block, use await --- otherwise deadlock might arise as TPL may schedule a task continuation on the current thread.
                await item6Dequeued.ToTask();

                completed[4].SetResult(4);
                completed[6].SetResult(6);
                for (int i = 7; i < 10; i++)
                {
                    completed[i].SetResult(i);
                }

                await q.WhenDone();
                for (int i = 0; i < 10; i++)
                {
                    XAssert.IsTrue(created[i].Task.IsCompleted);
                }

                XAssert.AreEqual(0, q.PriorityQueued);
                XAssert.AreEqual(0, q.SemaphoreQueued);
                XAssert.AreEqual(0, q.Running);
            }
        }

        public async Task TestDeadlock()
        {
            const int NumberOfIterations = 10000;
            for (int n = 0; n < NumberOfIterations; n++)
            {
                await Semaphores();
            }
        }
    }
}
