using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Extension methods for Result classes.
    /// </summary>
    public static class ResultsExtensions
    {
        /// <summary>
        /// Converts <see cref="Possible{TResult}"/> to <see cref="Result{T}"/>
        /// </summary>
        public static Result<T> ToResult<T>(this Possible<T> possible)
        {
            if (possible.Succeeded)
            {
                return new Result<T>(possible.Result);
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
            if (ResultBase.ResultExceptionTextProcessor == null)
            {
                // Ensure exception strings have demystified stack tracks
                ResultBase.ResultExceptionTextProcessor = ex => ex.ToStringDemystified();
            }
        }

        /// <nodoc />
        internal static TResult WithLockAcquisitionDuration<TResult, TLockKey>(this TResult result, in LockSet<TLockKey>.LockHandle handle)
            where TResult : ResultBase
            where TLockKey : IEquatable<TLockKey>
        {
            Contract.Requires(!result.Succeeded || result.Diagnostics == null, "Diagnostics property can be set only once.");

            if (result.Succeeded && handle.LockAcquisitionDuration != null)
            {
                result.Diagnostics = $"LockWait={(long)handle.LockAcquisitionDuration.Value.TotalMilliseconds}ms";
            }

            return result;
        }
    }
}
