// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Extension methods for Result classes.
    /// </summary>
    public static class ResultsExtensions
    {
        /// <summary>
        /// Waits for the given task to complete within the given timeout, returns unsuccessful <see cref="BoolResult"/> if the timeout expires before the task completes.
        /// </summary>
        public static async Task<BoolResult> WithTimeoutAsync(this Task<BoolResult> task, string operationName, TimeSpan timeout)
        {
            try
            {
                return await task.WithTimeoutAsync(timeout);
            }
            catch (TimeoutException)
            {
                string error = $"{operationName} didn't finished after '{timeout}'.";
                return new BoolResult(error);
            }
        }

        /// <summary>
        /// Converts <see cref="Possible{TResult}"/> to <see cref="Result{T}"/>
        /// </summary>
        public static Result<T> ToResult<T>(this Possible<T> possible, bool isNullAllowed = false)
        {
            if (possible.Succeeded)
            {
                return new Result<T>(possible.Result, isNullAllowed);
            }
            else
            {
                return new Result<T>(possible.Failure.DescribeIncludingInnerFailures());
            }
        }

        /// <summary>
        /// Ensures result exceptions are demystified.
        /// </summary>
        internal static void InitializeResultExceptionPreprocessing()
        {
            // Ensure exception strings have demystified stack tracks
            Error.ExceptionToTextConverter = static ex => ex.ToStringDemystified();
        }

        /// <nodoc />
        internal static TResult WithLockAcquisitionDuration<TResult, TLockKey>(this TResult result, in LockSet<TLockKey>.LockHandle handle)
            where TResult : ResultBase
            where TLockKey : IEquatable<TLockKey>
        {
            Contract.Requires(!result.Succeeded || result.Diagnostics == null, "Diagnostics property can be set only once.");

            if (result.Succeeded && handle.LockAcquisitionDuration != null)
            {
                result.SetDiagnosticsForSuccess($"LockWait={(long)handle.LockAcquisitionDuration.Value.TotalMilliseconds}ms");
            }

            return result;
        }
    }
}
