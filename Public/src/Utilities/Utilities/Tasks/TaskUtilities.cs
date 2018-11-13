// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Utilities.Tasks
{
    /// <summary>
    /// Static utilities related to <see cref="Task" />.
    /// </summary>
    public static class TaskUtilities
    {
        /// <summary>
        /// Returns a faulted task containing the given exception.
        /// This is the failure complement of <see cref="Task.FromResult{TResult}" />.
        /// </summary>
        [ContractOption("runtime", "checking", false)]
        public static Task<T> FromException<T>(Exception ex)
        {
            Contract.Requires(ex != null);
            Contract.Ensures(Contract.Result<Task<T>>() != null);

            var failureSource = TaskSourceSlim.Create<T>();
            failureSource.SetException(ex);
            return failureSource.Task;
        }

        /// <summary>
        /// This is a variant of Task.WhenAll which ensures that all exceptions thrown by the tasks are
        /// propagated back through a single AggregateException. This is necessary because the default awaiter
        /// (as used by 'await') only takes the *first* exception inside of a task's aggregate exception.
        /// All BuildXL code should use this method instead of the standard WhenAll.
        /// </summary>
        public static async Task SafeWhenAll(IEnumerable<Task> tasks)
        {
            Contract.Requires(tasks != null);

            var whenAllTask = Task.WhenAll(tasks);
            try
            {
                await whenAllTask;
            }
            catch
            {
                Contract.Assume(whenAllTask.Exception != null);
                throw whenAllTask.Exception;
            }
        }

        /// <summary>
        /// This is a variant of Task.WhenAll which ensures that all exceptions thrown by the tasks are
        /// propagated back through a single AggregateException. This is necessary because the default awaiter
        /// (as used by 'await') only takes the *first* exception inside of a task's aggregate exception.
        /// All BuildXL code should use this method instead of the standard WhenAll.
        /// </summary>
        public static async Task<TResult[]> SafeWhenAll<TResult>(IEnumerable<Task<TResult>> tasks)
        {
            Contract.Requires(tasks != null);

            var whenAllTask = Task.WhenAll(tasks);
            try
            {
                return await whenAllTask;
            }
            catch
            {
                Contract.Assume(whenAllTask.Exception != null);
                throw whenAllTask.Exception;
            }
        }

        /// <summary>
        /// Provides await functionality for ordinary <see cref="WaitHandle"/>s.
        /// </summary>
        /// <param name="handle">The handle to wait on.</param>
        /// <returns>The awaiter.</returns>
        public static TaskAwaiter GetAwaiter(this WaitHandle handle)
        {
            Contract.Requires(handle != null);

            return handle.ToTask().GetAwaiter();
        }

        /// <summary>
        /// Provides await functionality for an array of ordinary <see cref="WaitHandle"/>s.
        /// </summary>
        /// <param name="handles">The handles to wait on.</param>
        /// <returns>The awaiter.</returns>
        public static TaskAwaiter<int> GetAwaiter(this WaitHandle[] handles)
        {
            Contract.Requires(handles != null);
            Contract.RequiresForAll(handles, handle => handles != null);

            return handles.ToTask().GetAwaiter();
        }

        /// <summary>
        /// Creates a TPL Task that is marked as completed when a <see cref="WaitHandle"/> is signaled.
        /// </summary>
        /// <param name="handle">The handle whose signal triggers the task to be completed.  Do not use a <see cref="Mutex"/> here.</param>
        /// <param name="timeout">The timeout (in milliseconds) after which the task will fault with a <see cref="TimeoutException"/> if the handle is not signaled by that time.</param>
        /// <returns>A Task that is completed after the handle is signaled.</returns>
        /// <remarks>
        /// There is a (brief) time delay between when the handle is signaled and when the task is marked as completed.
        /// </remarks>
        public static Task ToTask(this WaitHandle handle, int timeout = Timeout.Infinite)
        {
            Contract.Requires(handle != null);

            return ToTask(new WaitHandle[1] { handle }, timeout);
        }

        /// <summary>
        /// Creates a TPL Task that is marked as completed when any <see cref="WaitHandle"/> in the array is signaled.
        /// </summary>
        /// <param name="handles">The handles whose signals triggers the task to be completed.  Do not use a <see cref="Mutex"/> here.</param>
        /// <param name="timeout">The timeout (in milliseconds) after which the task will return a value of WaitTimeout.</param>
        /// <returns>A Task that is completed after any handle is signaled.</returns>
        /// <remarks>
        /// There is a (brief) time delay between when the handles are signaled and when the task is marked as completed.
        /// </remarks>
        public static Task<int> ToTask(this WaitHandle[] handles, int timeout = Timeout.Infinite)
        {
            Contract.Requires(handles != null);
            Contract.RequiresForAll(handles, handle => handles != null);

            var tcs = TaskSourceSlim.Create<int>();
            int signalledHandle = WaitHandle.WaitAny(handles, 0);
            if (signalledHandle != WaitHandle.WaitTimeout)
            {
                // An optimization for if the handle is already signaled
                // to return a completed task.
                tcs.SetResult(signalledHandle);
            }
            else
            {
                var localVariableInitLock = new object();
                lock (localVariableInitLock)
                {
                    RegisteredWaitHandle[] callbackHandles = new RegisteredWaitHandle[handles.Length];
                    for (int i = 0; i < handles.Length; i++)
                    {
                        callbackHandles[i] = ThreadPool.RegisterWaitForSingleObject(
                            handles[i],
                            (state, timedOut) =>
                            {
                                int handleIndex = (int)state;
                                if (timedOut)
                                {
                                    tcs.TrySetResult(WaitHandle.WaitTimeout);
                                }
                                else
                                {
                                    tcs.TrySetResult(handleIndex);
                                }

                                // We take a lock here to make sure the outer method has completed setting the local variable callbackHandles contents.
                                lock (localVariableInitLock)
                                {
                                    foreach (var handle in callbackHandles)
                                    {
                                        handle.Unregister(null);
                                    }
                                }
                            },
                            state: i,
                            millisecondsTimeOutInterval: timeout,
                            executeOnlyOnce: true);
                    }
                }
            }

            return tcs.Task;
        }

        /// <summary>
        /// Creates a new <see cref="SemaphoreSlim"/> representing a mutex which can only be entered once.
        /// </summary>
        /// <returns>the semaphore</returns>
        public static SemaphoreSlim CreateMutex(bool taken = false)
        {
            return new SemaphoreSlim(initialCount: taken ? 0 : 1, maxCount: 1);
        }

        /// <summary>
        /// Asynchronously acquire a semaphore
        /// </summary>
        /// <param name="semaphore">The semaphore to acquire</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>A disposable which will release the semaphore when it is disposed.</returns>
        public static async Task<SemaphoreReleaser> AcquireAsync(this SemaphoreSlim semaphore, CancellationToken cancellationToken = default(CancellationToken))
        {
            Contract.Requires(semaphore != null);
            await semaphore.WaitAsync(cancellationToken);
            return new SemaphoreReleaser(semaphore);
        }

        /// <summary>
        /// Synchronously acquire a semaphore
        /// </summary>
        /// <param name="semaphore">The semaphore to acquire</param>
        public static SemaphoreReleaser AcquireSemaphore(this SemaphoreSlim semaphore)
        {
            Contract.Requires(semaphore != null);
            semaphore.Wait();
            return new SemaphoreReleaser(semaphore);
        }

        /// <summary>
        /// Consumes a task and doesn't do anything with it.  Useful for fire-and-forget calls to async methods within async methods.
        /// </summary>
        /// <param name="task">The task whose result is to be ignored.</param>
        /// <param name="unobservedExceptionHandler">Optional handler for the task's unobserved exception (if any).</param>
        public static void Forget(this Task task, Action<Exception> unobservedExceptionHandler = null)
        {
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var e = (t.Exception as AggregateException)?.InnerException ?? t.Exception;
                    unobservedExceptionHandler?.Invoke(e);
                }
            });
        }

        /// <summary>
        /// Waits for the given task to complete within the given timeout, throwing a <see cref="TimeoutException"/> if the timeout expires before the task completes
        /// </summary>
        public static async Task WithTimeoutAsync(this Task task, TimeSpan timeout)
        {
            await Task.WhenAny(task, Task.Delay(timeout));

            if (!task.IsCompleted)
            {
                throw new TimeoutException();
            }
        }

        /// <summary>
        /// Gets a task for the completion source which execute continuations for TaskCompletionSource.SetResult asynchronously.
        /// </summary>
        public static Task<T> GetAsyncCompletion<T>(this TaskSourceSlim<T> completion)
        {
            if (completion.Task.IsCompleted)
            {
                return completion.Task;
            }

            return GetTaskWithAsyncContinuation(completion);
        }

        private static async Task<T> GetTaskWithAsyncContinuation<T>(this TaskSourceSlim<T> completion)
        {
            var result = await completion.Task;

            // Yield to not block the thread which sets the result of the completion
            await Task.Yield();

            return result;
        }

        /// <summary>
        /// Allows an IDisposable-conforming release of an acquired semaphore
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public readonly struct SemaphoreReleaser : IDisposable
        {
            private readonly SemaphoreSlim m_semaphore;

            /// <summary>
            /// Creates a new releaser.
            /// </summary>
            /// <param name="semaphore">The semaphore to release when Dispose is invoked.</param>
            /// <remarks>
            /// Assumes the semaphore is already acquired.
            /// </remarks>
            internal SemaphoreReleaser(SemaphoreSlim semaphore)
            {
                Contract.Requires(semaphore != null);
                m_semaphore = semaphore;
            }

            /// <summary>
            /// IDispoaable.Dispose()
            /// </summary>
            public void Dispose()
            {
                m_semaphore.Release();
            }

            /// <summary>
            /// Whether this semaphore releaser is valid (and not the default value)
            /// </summary>
            public bool IsValid => m_semaphore != null;

            /// <summary>
            /// Gets the number of threads that will be allowed to enter the semaphore.
            /// </summary>
            public int CurrentCount => m_semaphore.CurrentCount;
        }

        /// <summary>
        /// Awaits given tasks, periodically calling <paramref name="action"/>.
        /// </summary>
        /// <typeparam name="TItem">Type of the collection to iterate over.</typeparam>
        /// <typeparam name="TResult">Type of the tasks' result.</typeparam>
        /// <param name="collection">Collection to iterate over.</param>
        /// <param name="taskSelector">Function to use to select a task for a given item from the given collection.</param>
        /// <param name="action">
        /// Action to call periodically (as specified by <paramref name="period"/>).
        /// The action receives
        ///   (1) total elapsed time,
        ///   (2) all original items, and
        ///   (3) a collection of non-finished items
        /// </param>
        /// <param name="period">Period at which to call <paramref name="action"/>.</param>
        /// <returns>The results of inidvidual tasks.</returns>
        public static async Task<TResult[]> AwaitWithProgressReporting<TItem, TResult>(
            IReadOnlyCollection<TItem> collection,
            Func<TItem, Task<TResult>> taskSelector,
            Action<TimeSpan, IReadOnlyCollection<TItem>, IReadOnlyCollection<TItem>> action,
            TimeSpan period)
        {
            var startTime = DateTime.UtcNow;
            var timer = new StoppableTimer(
                () =>
                {
                    var elapsed = DateTime.UtcNow.Subtract(startTime);
                    var remainingItems = collection
                        .Where(item => !taskSelector(item).IsCompleted)
                        .ToList();
                    action(elapsed, collection, remainingItems);
                },
                dueTime: 0,
                period: (int)period.TotalMilliseconds);

            using (timer)
            {
                var result = await Task.WhenAll(collection.Select(item => taskSelector(item)));
                await timer.StopAsync();

                // report once at the end
                action(DateTime.UtcNow.Subtract(startTime), collection, CollectionUtilities.EmptyArray<TItem>());
                return result;
            }
        }

        /// <summary>
        /// Awaits for a given task while periodically calling <paramref name="action"/>.
        /// </summary>
        /// <typeparam name="TResult">Return type of the task</typeparam>
        /// <param name="task">The task to await</param>
        /// <param name="period">Period at which to call <paramref name="action"/></param>
        /// <param name="action">Action to periodically call.  The action receives elapsed time since this method was called.</param>
        /// <param name="reportImmediately">Whether <paramref name="action"/> should be called immediately.</param>
        /// <param name="reportAtEnd">Whether <paramref name="action"/> should be called at when </param>
        /// <returns>The result of the task.</returns>
        public static async Task<TResult> AwaitWithProgressReporting<TResult>(
            Task<TResult> task,
            TimeSpan period,
            Action<TimeSpan> action,
            bool reportImmediately = true,
            bool reportAtEnd = true)
        {
            var startTime = DateTime.UtcNow;
            var timer = new StoppableTimer(
                () =>
                {
                    action(DateTime.UtcNow.Subtract(startTime));
                },
                dueTime: reportImmediately ? 0 : (int)period.TotalMilliseconds,
                period: (int)period.TotalMilliseconds);

            using (timer)
            {
                await task.ContinueWith(t =>
                {
                    return timer.StopAsync();
                }).Unwrap();

                // report once at the end
                if (reportAtEnd)
                {
                    action(DateTime.UtcNow.Subtract(startTime));
                }

                return await task;
            }
        }

        /// <summary>
        /// Evaluate Tasks and return <paramref name="errorValue"/> if evaluation was cancelled.
        /// </summary>
        public static async Task<T> WithCancellationHandlingAsync<T>(LoggingContext loggingContext, Task<T> evaluationTask, Action<LoggingContext> errorLogEvent, T errorValue, CancellationToken cancellationToken)
        {
            var result = default(T);

            try
            {
                result = await evaluationTask;

                if (result.Equals(errorValue))
                {
                    return errorValue;
                }

                // Check for cancellation one last time.
                //
                // This makes sure that we log an error and return false if cancellation is requested.
                // If we don't check for cancellation at this point, it can happen that 'result' is
                // false (because the intepreter caught OperationCanceledException and returned ErrorResult)
                // but we haven't logged an error.
                cancellationToken.ThrowIfCancellationRequested();

                return result;
            }
            catch (OperationCanceledException)
            {
                errorLogEvent(loggingContext);
                return errorValue;
            }
        }
    }
}
