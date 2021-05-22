// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Synchronization
{
    /// <summary>
    ///     Task helpers
    /// </summary>
    public static class TaskSafetyHelpers
    {
        /// <summary>
        ///     Run a delegate on the thread pool to prevent deadlocks.
        /// </summary>
        /// <remarks>
        /// https://blogs.msdn.microsoft.com/pfxteam/2012/04/13/should-i-expose-synchronous-wrappers-for-asynchronous-methods/
        /// </remarks>
        public static T SyncResultOnThreadPool<T>(Func<Task<T>> taskFunc)
        {
            // http://stackoverflow.com/questions/36426937/what-is-the-difference-between-wait-vs-getawaiter-getresult
            return Task.Run(taskFunc).GetAwaiter().GetResult();
        }
    }
}
