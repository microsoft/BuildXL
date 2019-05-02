using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Synchronization;

namespace BuildXL.Cache.ContentStore.Utils
{
    internal static class ResultsExtensions
    {
        /// <nodoc />
        public static TResult WithLockAcquisitionDuration<TResult, TLockKey>(this TResult result, in LockSet<TLockKey>.LockHandle handle)
            where TResult : ResultBase
            where TLockKey : IEquatable<TLockKey>
        {
            Contract.Requires(!result.Succeeded || result.Diagnostics == null, $"Diagnostics property can be set only once.");

            if (result.Succeeded && handle.LockAcquisitionDuration != null)
            {
                var message = $", LockWait={(long)handle.LockAcquisitionDuration.Value.TotalMilliseconds}ms";
                result.Diagnostics = message;
            }

            return result;
        }
    }
}
