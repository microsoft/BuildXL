// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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
        public static void FireAndForget(this Task task, Context context, [CallerMemberName] string? operation = null, Severity failureSeverity = Severity.Warning, bool traceFailures = true, string? extraMessage = null, bool failFast = false)
        {
            // Since we're no longer going to wait for this task, we need to handle the case where it later throws
            // an exception as ThrowOnUnobservedTaskException is usually set in Q services.
            // See: http://blogs.msdn.com/b/pfxteam/archive/2011/09/28/task-exception-handling-in-net-4-5.aspx
            task.ContinueWith(
                t =>
                {
                    if (traceFailures)
                    {
                        string extraMessageText = string.IsNullOrEmpty(extraMessage) ? string.Empty : " " + extraMessage;
                        context.TraceMessage(
                            failureSeverity,
                            $"Unhandled exception in fire and forget task for operation '{operation}'{extraMessageText}: {t.Exception?.Message}. FullException={t.Exception?.ToString()}");
                    }

                    if (failFast)
                    {
                        string extraMessageText = string.IsNullOrEmpty(extraMessage) ? string.Empty : " " + extraMessage;
                        Environment.FailFast($"Unhandled exception in fire and forget task for operation '{operation}'{extraMessageText}: {t.Exception?.Message}. FullException={t.Exception?.ToString()}");
                    }
                },
            TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// When you want to call an <c>async</c> method but don't want to <c>await</c> it based on a flag. In the case where the task is fire
        /// and forget, a successful result is always returned.
        /// </summary>
        public static async Task<BoolResult> FireAndForgetOrInlineAsync<T>(this Task<T> task, Context context, bool inline, [CallerMemberName]string? operation = null, Severity severityOnException = Severity.Warning)
            where T : BoolResult
        {
            if (inline)
            {
                return await task;
            }
            else
            {
                task.FireAndForget(context, operation, severityOnException);
                return BoolResult.Success;
            }
        }

        /// <summary>
        /// Returns a task representing the result of the given task which will run continuations asynchronously.
        /// </summary>
        public static Task<T> RunContinuationsAsync<T>(this Task<T> task)
        {
            var taskSource = TaskSourceSlim.Create<T>();
            taskSource.LinkToTask(task);
            return taskSource.Task;
        }

        /// <summary>
        /// When you want to call an <c>async</c> method which returns a <see cref="BoolResult"/> but only want to log error rather than propagating.
        /// </summary>
        /// <remarks>
        /// If the task is given up on, this prevents any unhandled exception from bringing down
        /// the process (if ThrowOnUnobservedTaskException is set) by observing any exception.
        /// </remarks>
        public static void FireAndForget(this Task<BoolResult> task, Context context, [CallerMemberName]string? operation = null, Severity severityOnException = Severity.Warning)
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
        public static void TraceIfFailure<TResult>(this Task<TResult> task, Context context, Severity failureSeverity, bool traceTaskExceptionsOnly = false, [CallerMemberName]string? operation = null) where TResult : ResultBase
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
        public static Task FireAndForgetErrorsAsync(this Task task, Context context, [CallerMemberName]string? operation = null)
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
        public static Task<T> FireAndForgetAndReturnTask<T>(this Task<T> task, Context context, [CallerMemberName]string? operation = null)
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
        /// Transforms the result of a task
        /// </summary>
        public static async Task<T2> ThenAsync<T1, T2>(this Task<T1> first, Func<T1, T2> next)
        {
            var result = await first;
            return next(result);
        }

        /// <summary>
        /// Transforms the result of a task
        /// </summary>
        public static async Task<T2> ThenAsync<T1, T2>(this Task<T1> first, Func<T1, Task<T2>> next)
        {
            var result = await first;
            return await next(result);
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
