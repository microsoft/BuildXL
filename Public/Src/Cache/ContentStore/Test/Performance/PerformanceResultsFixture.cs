// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using ContentStoreTest.Test;
using Xunit.Abstractions;

namespace ContentStoreTest.Performance
{
    public sealed class PerformanceResultsFixture : IDisposable
    {
        private readonly int _previousMinWorkerThreads;
        private readonly int _previousMaxWorkerThreads;
        private readonly int _previousMinCompletionPortThreads;
        private readonly int _previousMaxCompletionPortThreads;

        public PerformanceResults Results { get; } = new PerformanceResults();

        public PerformanceResultsFixture()
        {
            ThreadPool.GetMinThreads(out _previousMinWorkerThreads, out _previousMinCompletionPortThreads);
            int workerThreads = Math.Max(_previousMinWorkerThreads, Environment.ProcessorCount * 16);
            ThreadPool.SetMinThreads(workerThreads, _previousMinCompletionPortThreads);

            ThreadPool.GetMaxThreads(out _previousMaxWorkerThreads, out _previousMaxCompletionPortThreads);
            workerThreads = Math.Max(_previousMaxWorkerThreads, Environment.ProcessorCount * 16);
            ThreadPool.SetMaxThreads(workerThreads, _previousMaxCompletionPortThreads);
        }

        public void AddResults(ITestOutputHelper output, string name, long value, string units, long count = 0)
        {
            Results.Add(name, value, units, count);
            output?.WriteLine($"{name} = {value} {units}");
        }

        public void Dispose()
        {
            Results.Report();
            TestGlobal.Logger.Flush();

            ThreadPool.SetMinThreads(_previousMinWorkerThreads, _previousMinCompletionPortThreads);
            ThreadPool.SetMaxThreads(_previousMaxWorkerThreads, _previousMaxCompletionPortThreads);
        }
    }
}
