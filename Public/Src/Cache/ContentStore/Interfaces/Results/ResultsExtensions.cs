// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Set of extension methods for <see cref="ResultBase"/> and it's derived types.
    /// </summary>
    public static class ResultsExtensions
    {
        private static readonly string Component = nameof(ResultsExtensions);

        /// <nodoc />
        public static string GetDiagnosticsMessageForTracing(this ResultBase result, string prefix = " ")
        {
            if (string.IsNullOrEmpty(result.Diagnostics))
            {
                return string.Empty;
            }

            return $"{prefix}{result.Diagnostics}";
        }

        /// <summary>
        /// Gets tracing status from result instance
        /// </summary>
        public static OperationStatus GetStatus(this ResultBase resultBase)
        {
            if (resultBase.IsCriticalFailure)
            {
                return OperationStatus.CriticalFailure;
            }
            else if (resultBase.IsCancelled)
            {
                return OperationStatus.Cancelled;
            }
            else if (!resultBase.Succeeded)
            {
                return OperationStatus.Failure;
            }
            else
            {
                return OperationStatus.Success;
            }
        }

        /// <summary>
        /// Awaits for the <paramref name="task"/> to finish and logs the error if the result is not successful.
        /// </summary>
        public static async Task<TResult> TraceIfFailure<TResult>(this Task<TResult> task, Context context, [CallerMemberName]string? operationName = null) where TResult : ResultBase
        {
            var result = await task;
            result.TraceIfFailure(context, operationName);

            return result;
        }

        /// <summary>
        /// Traces the <paramref name="result"/> if the result is not successful.
        /// </summary>
        public static TResult TraceIfFailure<TResult>(this TResult result, Context context, [CallerMemberName] string? operationName = null)
            where TResult : ResultBase
        {
            if (!result.Succeeded)
            {
                context.Warning($"Operation '{operationName}' failed with an error={result}", Component, operation: operationName);
            }

            return result;
        }

        /// <summary>
        /// Method that ignores the result.
        /// </summary>
        public static void IgnoreTaskResult(this Task<BoolResult> task)
        {
            
        }

        /// <nodoc />
        public static BoolResult And(this IEnumerable<BoolResult> results)
        {
            var result = BoolResult.Success;

            foreach (var r in results)
            {
                result &= r;
            }

            return result;
        }

        /// <summary>
        /// Helper for transforming Task{T} to Task{Result{T}}
        /// </summary>
        public static async Task<Result<T>> AsSuccessAsync<T>(this Task<T> task)
        {
            var result = await task;
            return Result.Success(result);
        }

        /// <summary>
        /// Maps result into different result type or propagates error to result type
        /// </summary>
        public static TResult Then<T, TResult>(this Result<T> result, Func<T, TResult> selector)
            where TResult : ResultBase
        {
            if (result.Succeeded)
            {
                return selector(result.Value);
            }

            return new ErrorResult(result).AsResult<TResult>();
        }

        /// <summary>
        /// Maps result into different result type or propagates error to result type
        /// </summary>
        public static TResult Then<T, TResult>(this Result<T> result, Func<T, TimeSpan, TResult> selector)
            where TResult : ResultBase
        {
            if (result.Succeeded)
            {
                return selector(result.Value, result.Duration);
            }

            return new ErrorResult(result).AsResult<TResult>();
        }

        /// <summary>
        /// Transforms the result of a task
        /// </summary>
        public static async Task<TResult> ThenAsync<T, TResult>(this Task<T> first, Func<T, TResult> next)
            where T : ResultBase
            where TResult : ResultBase
        {
            var result = await first;
            if (result.Succeeded)
            {
                return next(result);
            }

            return new ErrorResult(result).AsResult<TResult>();
        }

        /// <summary>
        /// Transforms the result of a task
        /// </summary>
        public static async Task<TResult> ThenAsync<T, TResult>(this Task<T> first, Func<T, Task<TResult>> next)
            where T : ResultBase
            where TResult : ResultBase
        {
            var result = await first;
            if (result.Succeeded)
            {
                return await next(result);
            }

            return new ErrorResult(result).AsResult<TResult>();
        }

        /// <nodoc />
        public static void Match<T>(this Result<T> result, Action<T> successAction, Action<Result<T>> failureAction)
        {
            if (result.Succeeded)
            {
                successAction(result.Value);
            }
            else
            {
                failureAction(result);
            }
        }

        /// <summary>
        /// Maps result into different result type or propagates error to result type
        /// </summary>
        public static async Task<Result<TResult>> SelectAsync<TResultBase, TResult>(this Task<TResultBase> resultTask, Func<TResultBase, TResult> selector)
            where  TResultBase : ResultBase
        {
            var result = await resultTask;
            if (result.Succeeded)
            {
                return Result.Success(selector(result));
            }
            else
            {
                return new Result<TResult>(result);
            }
        }

        /// <summary>
        /// Maps result into different result type or propagates error to result type
        /// </summary>
        public static async Task<Result<TResult>> AsAsync<T, TResult>(this Task<Result<T>> resultTask, Func<T, TResult> selector)
        {
            var result = await resultTask;
            if (result.Succeeded)
            {
                return Result.Success(selector(result.Value));
            }
            else
            {
                return new Result<TResult>(result);
            }
        }

        /// <summary>
        /// Maps result into different result type or propagates error to result type
        /// </summary>
        public static Result<TResult> Select<TResult>(this BoolResult result, Func<TResult> selector)
        {
            if (result.Succeeded)
            {
                return Result.Success(selector());
            }
            else
            {
                return new Result<TResult>(result);
            }
        }

        /// <summary>
        /// Maps result into different result type or propagates error to result type
        /// </summary>
        public static Result<TResult> Select<T, TResult>(this Result<T> result, Func<T, TResult> selector, bool isNullAllowed = false)
        {
            if (result.Succeeded)
            {
                return Result.Success(selector(result.Value), isNullAllowed: isNullAllowed);
            }
            else
            {
                return new Result<TResult>(result);
            }
        }

        /// <summary>
        /// Throws <see cref="ResultPropagationException"/> if <paramref name="result"/> is not successful.
        /// </summary>
        [StackTraceHidden]
        public static T ThrowIfFailure<T>(this Result<T> result)
        {
            return ThrowIfFailure(result, unwrap: false);
        }

        /// <summary>
        /// Throws <see cref="ResultPropagationException"/> or original exception (unwrap is true) if <paramref name="result"/> is not successful.
        /// </summary>
        [StackTraceHidden]
        public static T ThrowIfFailure<T>(this Result<T> result, bool unwrap)
        {
            if (!result.Succeeded)
            {
                if (unwrap)
                {
                    result.RethrowIfFailure();
                }

                throw new ResultPropagationException(result);
            }

            return result.Value;
        }

        /// <summary>
        /// Throws <see cref="ResultPropagationException"/> if result is not successful.
        /// </summary>
        [StackTraceHidden]
        public static Task<T> ThrowIfFailureAsync<T>(this Task<Result<T>> task)
        {
            return ThrowIfFailureAsync(task, unwrap: false);
        }

        /// <summary>
        /// Throws <see cref="ResultPropagationException"/> if result is not successful.
        /// </summary>
        [StackTraceHidden]
        public static async Task<T> ThrowIfFailureAsync<T>(this Task<Result<T>> task, bool unwrap)
        {
            var result = await task;
            return result.ThrowIfFailure(unwrap);
        }

        /// <summary>
        /// Throws <see cref="ResultPropagationException"/> if <paramref name="result"/> is not successful.
        /// </summary>
        [StackTraceHidden]
        public static TResult ThrowIfFailure<TResult>(this TResult result) where TResult : ResultBase
        {
            if (!result.Succeeded)
            {
                throw new ResultPropagationException(result);
            }

            return result;
        }

        /// <summary>
        /// Throws <see cref="ResultPropagationException"/> if <paramref name="result"/> is not successful.
        /// </summary>
        [StackTraceHidden]
        public static TResult RethrowIfFailure<TResult>(this TResult result) where TResult : ResultBase
        {
            if (!result.Succeeded)
            {
                if (result.Exception is not null)
                {
                    ExceptionDispatchInfo.Capture(result.Exception).Throw();
                }

                throw new ResultPropagationException(result);
            }

            return result;
        }

        /// <summary>
        /// Awaits the task and throws <see cref="ResultPropagationException"/> if the result is not successful.
        /// </summary>
        [StackTraceHidden]
        public static async Task<T> ThrowIfFailure<T>(this Task<T> task)
            where T : ResultBase
        {
            var result = await task;
            result.ThrowIfFailure();
            return result;
        }

        /// <summary>
        /// Awaits the task and throws the original exception that caused the original error.
        /// If the result is unsuccessful not because of an exception then <see cref="ResultPropagationException"/> is thrown.
        /// </summary>
        [StackTraceHidden]
        public static async Task<T> RethrowIfFailure<T>(this Task<T> task)
            where T : ResultBase
        {
            var result = await task;
            result.RethrowIfFailure();
            return result;
        }

        /// <summary>
        /// Awaits the task and throws <see cref="ResultPropagationException"/> if the result is not successful but only when <paramref name="throwOnFailure"/> is true.
        /// </summary>
        [StackTraceHidden]
        public static Task<T> ThrowIfNeededAsync<T>(this Task<T> task, bool throwOnFailure)
            where T : ResultBase
        {
            return throwOnFailure ? task.ThrowIfFailure() : task;
        }

        /// <summary>
        /// Throws <see cref="ResultPropagationException"/> if result is not successful.
        /// </summary>
        [StackTraceHidden]
        public static void ThrowIfFailure<TResult>(this IEnumerable<TResult> results) where TResult : ResultBase
        {
            foreach (var result in results)
            {
                result.ThrowIfFailure();
            }
        }

        /// <summary>
        /// Throws <see cref="ResultPropagationException"/> if result is not successful.
        /// </summary>
        [StackTraceHidden]
        public static async Task<TResult> ThrowIfFailureAsync<TResult>(this Task<TResult> task) where TResult : ResultBase
        {
            var result = await task;
            result.ThrowIfFailure();

            return result;
        }

        /// <summary>
        /// Throws <see cref="ResultPropagationException"/> if result is not successful.
        /// </summary>
        [StackTraceHidden]
        public static async Task ThrowIfFailureAsync<TResult>(this Task<TResult[]> task) where TResult : ResultBase
        {
            var result = await task;
            result.ThrowIfFailure();
        }

        /// <summary>
        /// Awaits the task and throws <see cref="ResultPropagationException"/> if the result is not successful.
        /// </summary>
        [StackTraceHidden]
        public static async Task<TResult> ThrowIfFailureAsync<T, TResult>(this Task<T> task, Func<T, TResult> resultSelector)
            where T : ResultBase
        {
            var result = await task;
            result.ThrowIfFailure();
            return resultSelector(result);
        }

        /// <summary>
        /// Special method to ignore potentially not successful result of an operation explicitly.
        /// </summary>
        public static void IgnoreFailure(this ResultBase? result)
        {
        }

        /// <summary>
        /// Special method to ignore potentially not successful result of an operation explicitly.
        /// </summary>
        public static Task IgnoreFailure<T>(this Task<T> task) where T : ResultBase
        {
            return task;
        }

        /// <summary>
        /// Gets the value from <paramref name="result"/> if operation succeeded or default value if it did not succeed.
        /// </summary>
        public static T? GetValueOrDefault<T>(this Result<T> result, T? defaultValue = default!)
        {
            return result.Succeeded ? result.Value : defaultValue;
        }

        /// <summary>
        /// Gets the value from <paramref name="result"/> if operation succeeded or default value if it did not succeed.
        /// </summary>
        public static T GetValueOr<T>(this Result<T> result, T defaultValue)
        {
            return result.Succeeded ? result.Value : defaultValue;
        }

        /// <summary>
        /// Gets the value from <paramref name="result"/> if operation succeeded or throws <see cref="ResultPropagationException"/> otherwise.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Result{T}.Value"/>, this method will not throw contract violation if the result is not successful.
        /// </remarks>
        [StackTraceHidden]
        public static T GetValueOrThrow<T>(this Result<T> result)
        {
            if (!result.Succeeded)
            {
                throw new ResultPropagationException(result);
            }

            return result.Value;
        }

        /// <summary>
        /// Tries to get the value from the result.
        /// </summary>
        /// <returns>Whether the value was gotten successfully.</returns>
        public static bool TryGetValue<T>(this Result<T> result, [NotNullWhen(true)]out T? value)
        {
            value = result.Succeeded ? result.Value : default;
            return value is not null;
        }

        /// <summary>
        /// Returns a string representation of the result if succeeded.
        /// </summary>
        public static string? ToStringWithValue<T>(this Result<T> result)
        {
            if (!result.Succeeded)
            {
                return result.ToString();
            }

            return result.Value.ToString();
        }

        /// <summary>
        /// Returns a result of <paramref name="toStringProjection"/> call if the <paramref name="result"/> succeeded, otherwise returns an error representation of the result.
        /// </summary>
        public static string ToStringSelect<T>(this Result<T> result, Func<T, string> toStringProjection)
        {
            if (!result.Succeeded)
            {
                return result.ToString();
            }

            return toStringProjection(result.Value);
        }

        /// <summary>
        /// Returns a result of <paramref name="toStringProjection"/> call if the <paramref name="result"/> succeeded, otherwise default string value
        /// </summary>
        public static string ToStringSelectOrDefault<T>(this Result<T> result, Func<T, string> toStringProjection, string defaultValue = "")
        {
            if (!result.Succeeded)
            {
                return defaultValue;
            }

            return toStringProjection(result.Value);
        }

        /// <summary>
        /// Returns a string representation of the result if succeeded.
        /// </summary>
        public static string ToStringOr<T>(this Result<T> result, string defaultValue)
        {
            if (!result.Succeeded)
            {
                return defaultValue;
            }

            return result.Value.ToString()!;
        }
    }
}
