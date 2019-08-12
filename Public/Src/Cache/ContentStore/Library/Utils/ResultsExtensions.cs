using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Utils
{
    internal static class ResultsExtensions
    {
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
        public static TResult WithLockAcquisitionDuration<TResult, TLockKey>(this TResult result, in LockSet<TLockKey>.LockHandle handle)
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
