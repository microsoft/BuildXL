// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        /// <summary>
        ///     This is a variant of Task.WhenAll which ensures that all exceptions thrown by the tasks are
        ///     propagated back through a single AggregateException. This is necessary because the default awaiter
        ///     (as used by 'await') only takes the *first* exception inside of a task's aggregate exception.
        ///     All code should use this method instead of the standard WhenAll.
        /// </summary>
        public static async Task WhenAll(IEnumerable<Task> tasks)
        {
            Contract.Requires(tasks != null);

            var whenAllTask = Task.WhenAll(tasks);
            try
            {
                await whenAllTask;
            }
            catch
            {
                if (whenAllTask.Exception != null)
                {
                    ExceptionDispatchInfo.Capture(whenAllTask.Exception).Throw();
                }

                throw;
            }
        }

        /// <summary>
        ///     This is a variant of Task.WhenAll which ensures that all exceptions thrown by the tasks are
        ///     propagated back through a single AggregateException. This is necessary because the default awaiter
        ///     (as used by 'await') only takes the *first* exception inside of a task's aggregate exception.
        ///     All code should use this method instead of the standard WhenAll.
        /// </summary>
        public static Task<TResult[]> WhenAll<TResult>(params Task<TResult>[] tasks)
        {
            Contract.Requires(tasks != null);

            return WhenAll((IEnumerable<Task<TResult>>)tasks);
        }

        /// <summary>
        ///     This is a variant of Task.WhenAll which ensures that all exceptions thrown by the tasks are
        ///     propagated back through a single AggregateException. This is necessary because the default awaiter
        ///     (as used by 'await') only takes the *first* exception inside of a task's aggregate exception.
        ///     All code should use this method instead of the standard WhenAll.
        /// </summary>
        public static async Task<TResult[]> WhenAll<TResult>(IEnumerable<Task<TResult>> tasks)
        {
            Contract.Requires(tasks != null);

            var whenAllTask = Task.WhenAll(tasks);
            try
            {
                return await whenAllTask;
            }
            catch
            {
                if (whenAllTask.Exception != null)
                {
                    // Rethrowing the error preserving the stack trace.
                    ExceptionDispatchInfo.Capture(whenAllTask.Exception).Throw();
                }

                throw;
            }
        }
    }
}
