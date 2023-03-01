// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities.Core.Tasks
{
    /// <summary>
    /// Static utilities related to <see cref="Task" />.
    /// </summary>
    public static class TaskUtilitiesExtension
    {
        /// <nodoc />
        public static async Task<Optional<T>> WaitAsync<T>(this Task<T> task, TimeSpan? timeout = null)
        {
            timeout ??= Timeout.InfiniteTimeSpan;

            if (timeout == Timeout.InfiniteTimeSpan)
            {
                return await task;
            }
            else if (timeout == TimeSpan.Zero)
            {
                if (task.IsCompleted)
                {
                    return await task;
                }
                else
                {
                    return Optional<T>.Empty;
                }
            }

            var completedTask = await Task.WhenAny(task, Task.Delay(timeout.Value));
            completedTask.Forget();

            if (task.IsCompleted)
            {
                return await task;
            }
            else
            {
                return Optional<T>.Empty;
            }
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
        /// <param name="reportImmediately">Whether <paramref name="action"/> should be called immediately.</param>
        /// <returns>The results of individual tasks.</returns>
        public static async Task<TResult[]> AwaitWithProgressReporting<TItem, TResult>(
            IReadOnlyCollection<TItem> collection,
            Func<TItem, Task<TResult>> taskSelector,
            Action<TimeSpan, IReadOnlyCollection<TItem>, IReadOnlyCollection<TItem>> action,
            TimeSpan period,
            bool reportImmediately = true)
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
                dueTime: reportImmediately ? 0 : (int)period.TotalMilliseconds,
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
        /// <typeparam name="T">Return type of the task</typeparam>
        /// <param name="task">The task to await</param>
        /// <param name="period">Period at which to call <paramref name="action"/></param>
        /// <param name="action">Action to periodically call.  The action receives elapsed time since this method was called.</param>
        /// <param name="reportImmediately">Whether <paramref name="action"/> should be called immediately.</param>
        /// <param name="reportAtEnd">Whether <paramref name="action"/> should be called at when </param>
        /// <returns>The result of the task.</returns>
        public static async Task<T> AwaitWithProgressReportingAsync<T>(
            Task<T> task,
            TimeSpan period,
            Action<TimeSpan> action,
            bool reportImmediately = true,
            bool reportAtEnd = true)
        {
            var startTime = DateTime.UtcNow;
            using var timer = new StoppableTimer(
                () => action(DateTime.UtcNow.Subtract(startTime)),
                dueTime: reportImmediately ? 0 : (int)period.TotalMilliseconds,
                period: (int)period.TotalMilliseconds);

            await task.ContinueWith(_ => timer.StopAsync()).Unwrap();

            // report once at the end
            if (reportAtEnd)
            {
                action(DateTime.UtcNow.Subtract(startTime));
            }

            return await task;
        }

        /// <summary>
        /// Evaluate Tasks and return <paramref name="errorValue"/> if evaluation was cancelled.
        /// </summary>
        public static async Task<T> WithCancellationHandlingAsync<T>(Task<T> evaluationTask, Action onCancellationException, T errorValue, CancellationToken cancellationToken)
        {
            try
            {
                var result = await evaluationTask;
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
                onCancellationException();
                return errorValue;
            }
        }
    }
}
