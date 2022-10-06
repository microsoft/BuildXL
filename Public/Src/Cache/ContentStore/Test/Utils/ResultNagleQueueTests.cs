// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Utils
{
    public class ResultNagleQueueTests
    {
        private readonly ITestOutputHelper _outputHelper;

        public ResultNagleQueueTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [Fact]
        public async Task AvoidUnobservedExceptions()
        {
            // This test makes sure that when an error is happening in the callback provided to ResultNagleQueue
            // the errors won't became unhandled and the operation won't stuck.

            Exception unobservedException = null;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (sender, args) =>
            {
                unobservedException = args.Exception;
            };

            try
            {
                TaskScheduler.UnobservedTaskException += handler;

                var task = createAndUseResultNagleQueue();
                _outputHelper.WriteLine($"Got t. Scheduling ContinueWith. Status={task.Status}");

                var continuationTask = task.ContinueWith(
                    t =>
                    {
                        _outputHelper.WriteLine("Continue with");
                        var e = t.Exception?.InnerExceptions.FirstOrDefault();

                        _outputHelper.WriteLine($"Continuation: {t.Status}, E: {e?.GetHashCode()}");
                    });

                _outputHelper.WriteLine("Before delay");
                await Task.Delay(1000);
                _outputHelper.WriteLine("Forcing full GC");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                unobservedException.Should().BeNull();

                _outputHelper.WriteLine($"Awaiting continuation. task.Status={task.Status}, continuationTask.Status={continuationTask.Status}");
                await continuationTask;

            }
            finally
            {
                _outputHelper.WriteLine("In finally");
                TaskScheduler.UnobservedTaskException -= handler;
            }

            async Task createAndUseResultNagleQueue()
            {
                using var queue = new ResultNagleQueue<int, int>(
                    async list =>
                    {
                        await Task.Yield();
                        _outputHelper.WriteLine("Inside Start's callback.");
                        var e = new Exception(string.Join(", ", list.Select(n => n.ToString())));
                        throw e;
                    },
                    maxDegreeOfParallelism: 1,
                    interval: TimeSpan.FromMilliseconds(1),
                    batchSize: 2);

                queue.Start();

                // Enqueue more item then the batch size.
                // It is very important to enqueue more item then the batch size
                // to make sure the callback is called more than once.
                // If the callback will never be called the second time
                // the third EnqueueAsync will never be finished causing two issues:
                // this method will never be finished and TaskUnobservedException will be raised
                // when queue instance will go out of scope.
                _outputHelper.WriteLine("Before EnqueueAsync");
                var t1 = queue.EnqueueAsync(1);
                var t2 = queue.EnqueueAsync(2);
                var t3 = queue.EnqueueAsync(3);
                _outputHelper.WriteLine("Before Task.WhenAll");
                await Task.WhenAll(t1, t2, t3);

                _outputHelper.WriteLine("After EnqueueAsync");
            }
        }

        [Fact]
        public async Task TestBasicUsage()
        {
            using var queue = new ResultNagleQueue<int, int>(
                async values =>
                {
                    await Task.Yield();
                    return values.Select(i => i + 1).ToList();
                },
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMilliseconds(1),
                batchSize: 10);

            queue.Start();

            var values = new[] { 1, 20, 50, 121 };

            var resultTasks = new List<Task<int>>();

            foreach (var value in values)
            {
                resultTasks.Add(queue.EnqueueAsync(value));
            }

            var results = await Task.WhenAll(resultTasks);
            for (int i = 0; i < values.Length; i++)
            {
                results[i].Should().Be(values[i] + 1);
            }
        }
    }
}
