// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Utils;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Utils
{
    public class ResultNagleQueueTests
    {
        [Fact]
        public async Task TestBasicUsage()
        {
            using var queue = new ResultNagleQueue<int, int>(
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMilliseconds(1),
                batchSize: 10);

            queue.Start(async values =>
            {
                await Task.Yield();
                return values.Select(i => i + 1).ToList();
            });

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
