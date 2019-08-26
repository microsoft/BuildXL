// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Set of extension methods for <see cref="ResultBase"/> and it's derived types.
    /// </summary>
    public static class ResultsExtensions
    {
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
        /// Awaits for the <paramref name="task"/> to finish and logs the error if the result is not successful.
        /// </summary>
        public static async Task<ObjectResult<TResult>> TraceIfFailure<TResult>(this Task<ObjectResult<TResult>> task, Context context, [CallerMemberName]string operationName = null) where TResult : class
        {
            var result = await task;
            if (!result)
            {
                context.Warning($"Operation '{operationName}' failed with an error={result}");
            }

            return result;
        }

        /// <summary>
        /// Awaits for the <paramref name="task"/> to finish and logs the error if the result is not successful.
        /// </summary>
        public static async Task<TResult> TraceIfFailure<TResult>(this Task<TResult> task, Context context, [CallerMemberName]string operationName = null) where TResult : ResultBase
        {
            var result = await task;
            if (!result.Succeeded)
            {
                context.Warning($"Operation '{operationName}' failed with an error={result}");
            }

            return result;
        }

        /// <summary>
        /// Method that ignores the result.
        /// </summary>
        public static void IgnoreTaskResult(this Task<BoolResult> task)
        {
            
        }

        /// <summary>
        /// Throws <see cref="ResultPropagationException"/> if <paramref name="result"/> is not successful.
        /// </summary>
        public static T ThrowIfFailure<T>(this Result<T> result)
        {
            if (!result.Succeeded)
            {
                throw new ResultPropagationException(result);
            }

            return result.Value;
        }

        /// <summary>
        /// Throws <see cref="ResultPropagationException"/> if result is not successful.
        /// </summary>
        public static async Task<T> ThrowIfFailureAsync<T>(this Task<Result<T>> task)
        {
            var result = await task;
            return result.ThrowIfFailure();
        }

        /// <summary>
        /// Throws <see cref="ResultPropagationException"/> if <paramref name="result"/> is not successful.
        /// </summary>
        public static TResult ThrowIfFailure<TResult>(this TResult result) where TResult : ResultBase
        {
            if (!result.Succeeded)
            {
                throw new ResultPropagationException(result);
            }

            return result;
        }

        /// <summary>
        /// Awaits the task and throws <see cref="ResultPropagationException"/> if the result is not successful.
        /// </summary>
        public static async Task<T> ThrowIfFailure<T>(this Task<T> task)
            where T : ResultBase
        {
            var result = await task;
            result.ThrowIfFailure();
            return result;
        }

        /// <summary>
        /// Awaits the task and throws <see cref="ResultPropagationException"/> if the result is not successful.
        /// </summary>
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
        public static void IgnoreFailure(this ResultBase result)
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
        /// Gets the value from <paramref name="result"/> if operation succeeded or throws <see cref="ResultPropagationException"/> otherwise.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Result{T}.Value"/>, this method will not throw contract violation if the result is not successful.
        /// </remarks>
        public static T GetValueOrThrow<T>(this Result<T> result)
        {
            if (!result)
            {
                throw new ResultPropagationException(result);
            }

            return result.Value;
        }

        /// <summary>
        /// Gets the value from <paramref name="result"/> if operation succeeded or throws <see cref="ResultPropagationException"/> otherwise.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="StructResult{T}.Data"/>, this method will not throw contract violation if the result is not successful.
        /// </remarks>
        public static T GetValueOrThrow<T>(this StructResult<T> result) where T : struct
        {
            if (!result)
            {
                throw new ResultPropagationException(result);
            }

            return result.Data;
        }

        /// <summary>
        /// Gets the value from <paramref name="result"/> if operation succeeded or throws <see cref="ResultPropagationException"/> otherwise.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="ObjectResult{T}.Data"/>, this method will not throw contract violation if the result is not successful.
        /// </remarks>
        public static T GetValueOrThrow<T>(this ObjectResult<T> result) where T : class
        {
            if (!result)
            {
                throw new ResultPropagationException(result);
            }

            return result.Data;
        }
    }
}
