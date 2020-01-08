// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Interfaces.Extensions
{
    /// <summary>
    /// Extension methods for Tasks.
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// When you want to call an <c>async</c> method but don't want to <c>await</c> it.
        /// </summary>
        /// <remarks>
        /// If the task is given up on, this prevents any unhandled exception from bringing down
        /// the process (if ThrowOnUnobservedTaskException is set) by observing any exception.
        /// </remarks>
        public static void FireAndForget(this Task task, Context context, [CallerMemberName]string operation = null, Severity failureSeverity = Severity.Warning, bool traceFailures = true)
        {
            // Since we're no longer going to wait for this task, we need to handle the case where it later throws
            // an exception as ThrowOnUnobservedTaskException is usually set in Q services.
            // See: http://blogs.msdn.com/b/pfxteam/archive/2011/09/28/task-exception-handling-in-net-4-5.aspx
            task.ContinueWith(
                t =>
                {
                    if (traceFailures)
                    {
                        context.TraceMessage(
                            failureSeverity,
                            $"Unhandled exception in fire and forget task for operation '{operation}': {t.Exception?.Message}. FullException={t.Exception?.ToString()}");
                    }
                },
            TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// When you want to call an <c>async</c> method which returns a <see cref="BoolResult"/> but only want to log error rather than propagating.
        /// </summary>
        /// <remarks>
        /// If the task is given up on, this prevents any unhandled exception from bringing down
        /// the process (if ThrowOnUnobservedTaskException is set) by observing any exception.
        /// </remarks>
        public static void FireAndForget(this Task<BoolResult> task, Context context, [CallerMemberName]string operation = null, Severity severityOnException = Severity.Warning)
        {
            task.ContinueWith(
                t =>
                {
                    if (t.IsCanceled)
                    {
                        context.Info(
                            $"Fire and forget task is canceled for operation '{operation}'. FullException={t.Exception?.ToString()}");
                    }
                    else if (t.Exception != null)
                    {
                        context.TraceMessage(severityOnException,
                            $"Unhandled exception in fire and forget task for operation '{operation}': {t.Exception?.Message}. FullException={t.Exception?.ToString()}");
                    }
                    else if (!t.Result.Succeeded)
                    {
                        context.Warning($"Unhandled error in fire and forget task for operation '{operation}': {t.Result.ToString()}");
                    }
                });
        }

        /// <summary>
        /// When you want to call an <c>async</c> method which returns a <see cref="BoolResult"/> but only want to log error rather than propagating.
        /// </summary>
        /// <remarks>
        /// If the task is given up on, this prevents any unhandled exception from bringing down
        /// the process (if ThrowOnUnobservedTaskException is set) by observing any exception.
        /// </remarks>
        public static void TraceIfFailure<TResult>(this Task<TResult> task, Context context, Severity failureSeverity, bool traceTaskExceptionsOnly = false, [CallerMemberName]string operation = null) where TResult : ResultBase
        {
            task.ContinueWith(
                t =>
                {
                    // Ignoring cancelled tasks.
                    if (t.Exception != null)
                    {
                        context.TraceMessage(
                            failureSeverity,
                            $"'{operation}' has failed: {t.Exception?.Message}. FullException={t.Exception?.ToString()}");
                    }
                    else if (!t.Result.Succeeded && !traceTaskExceptionsOnly)
                    {
                        context.TraceMessage(
                            failureSeverity,
                            $"'{operation}' has failed: {t.Result.ToString()}");
                    }
                });
        }

        /// <summary>
        /// Allows awaiting the completion of a task while ignoring errors.
        /// </summary>
        public static Task FireAndForgetErrorsAsync(this Task task, Context context, [CallerMemberName]string operation = null)
        {
            task.FireAndForget(context, operation);
            return task.IgnoreErrors();
        }

        /// <nodoc />
        public static Task<TResult> TraceResult<TResult>(this Task<TResult> task, Context context, string operation, string message)
            where TResult : ResultBase
        {
            task.ContinueWith(
                t =>
                {
                    if (t.IsCanceled)
                    {
                        context.Info(
                            $"Operation '{operation}' is canceled.{message}");
                    }
                    else if (t.Exception != null)
                    {
                        context.Warning(
                            $"Unhandled exception in fire and forget task for operation '{operation}': {t.Exception?.Message}. FullException={t.Exception?.ToString()}");
                    }
                    else if (!t.Result.Succeeded)
                    {
                        context.Warning($"Unhandled error in fire and forget task for operation '{operation}': {t.Result.ToString()}");
                    }
                });

            return task;
        }

        /// <summary>
        /// When you want to call an <c>async</c> method but don't want to <c>await</c> it.
        /// </summary>
        /// <remarks>
        /// If the task is given up on, this prevents any unhandled exception from bringing down
        /// the process (if ThrowOnUnobservedTaskException is set) by observing any exception.
        /// </remarks>
        public static Task<T> FireAndForgetAndReturnTask<T>(this Task<T> task, Context context, [CallerMemberName]string operation = null)
        {
            // Since we're no longer going to wait for this task, we need to handle the case where it later throws
            // an exception as ThrowOnUnobservedTaskException is usually set in Q services.
            // See: http://blogs.msdn.com/b/pfxteam/archive/2011/09/28/task-exception-handling-in-net-4-5.aspx
            task.ContinueWith(
                t =>
                {
                    context.Warning(
                        $"Unhandled exception in fire and forget task for operation '{operation}': {t.Exception?.Message}. FullException={t.Exception?.ToString()}");
                },
                TaskContinuationOptions.OnlyOnFaulted);

            return task;
        }

        /// <summary>
        ///     Task composition that allows for asynchronously executing the next operation.
        /// </summary>
        /// <typeparam name="T1">
        ///     Resulting type of the first task.
        /// </typeparam>
        /// <typeparam name="T2">
        ///     Resulting type of the next task.
        /// </typeparam>
        /// <param name="first">
        ///     First task.
        /// </param>
        /// <param name="next">
        ///     Function to run the next task.
        /// </param>
        /// <returns>
        ///     Next task.
        /// </returns>
        [Obsolete("Please use async/await for task composition.")]
        public static Task<T2> Then<T1, T2>(this Task<T1> first, Func<T1, Task<T2>> next)
        {
            Contract.Requires(first != null);
            Contract.Requires(next != null);

            var tcs = new TaskCompletionSource<T2>();
            first.ContinueWith(
                antecedent =>
                {
                    if (antecedent.IsFaulted)
                    {
                        Contract.Assume(antecedent.Exception != null);
                        tcs.TrySetException(antecedent.Exception.InnerExceptions);
                    }
                    else if (antecedent.IsCanceled)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        try
                        {
                            var t = next(antecedent.Result);

                            if (t == null)
                            {
                                tcs.TrySetCanceled();
                            }
                            else
                            {
                                t.ContinueWith(
                                    nextAntecedent =>
                                    {
                                        if (nextAntecedent.IsFaulted)
                                        {
                                            Contract.Assume(nextAntecedent.Exception != null);
                                            tcs.TrySetException(nextAntecedent.Exception.InnerExceptions);
                                        }
                                        else if (nextAntecedent.IsCanceled)
                                        {
                                            tcs.TrySetCanceled();
                                        }
                                        else
                                        {
                                            tcs.TrySetResult(nextAntecedent.Result);
                                        }
                                    },
                                    TaskContinuationOptions.ExecuteSynchronously);
                            }
                        }
                        catch (Exception exception)
                        {
                            tcs.TrySetException(exception);
                        }
                    }
                },
                TaskContinuationOptions.ExecuteSynchronously);

            return tcs.Task;
        }

        /// <summary>
        /// Assuming <paramref name="pendingTask"/> is a non-null Task, will either return the incomplete <paramref name="pendingTask"/> or start a new task constructed by <paramref name="runAsync"/>.
        /// <paramref name="newTaskWasCreated"/> returns whether the function was run or false if the previous task was returned.
        /// </summary>
        public static Task RunIfCompleted(ref Task pendingTask, object lockHandle, Func<Task> runAsync, out bool newTaskWasCreated)
        {
            newTaskWasCreated = false;

            if (pendingTask.IsCompleted)
            {
                lock (lockHandle)
                {
                    if (pendingTask.IsCompleted)
                    {
                        newTaskWasCreated = true;
                        pendingTask = runAsync();
                    }
                }
            }

            return pendingTask;
        }
    }
}
