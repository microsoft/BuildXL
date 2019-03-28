// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Helper methods for configuring the CLR thread pool.
    /// </summary>
    public static class ThreadPoolHelper
    {
        /// <summary>
        /// Configures the worker thread pools
        /// </summary>
        public static void ConfigureWorkerThreadPools(int maxProcesses, int multiplier = 10)
        {
            int workerThreads;
            int completionPortThreads;

            ThreadPool.GetMaxThreads(out workerThreads, out completionPortThreads);
            workerThreads = Math.Max(workerThreads, maxProcesses * multiplier);
            ThreadPool.SetMaxThreads(workerThreads, completionPortThreads);

            // In this experiment we want to see if the gaps in the execution phase are caused by thread pool
            // exhaustion. The magic number 3 is from the following argument. Each pip execution, when creating process, requires
            // two tasks, one for running the process and one for the completion callback. Each pip execution
            // may update cache in async way, and this requires another task. With this change, in short experiments
            // (20 trials) with large benchmarks from the nightly perf. tests, the gaps in the execution phase do not occur.
            ThreadPool.GetMinThreads(out workerThreads, out completionPortThreads);
            workerThreads = Math.Max(workerThreads, maxProcesses * multiplier);
            ThreadPool.SetMinThreads(workerThreads, completionPortThreads);
        }
    }
}
