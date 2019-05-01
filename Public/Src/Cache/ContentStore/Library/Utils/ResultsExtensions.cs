using System;
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
            if (handle.LockAcquisitionDuration != null)
            {
                var message = $", lock acquired by {(long)handle.LockAcquisitionDuration.Value.TotalMilliseconds}ms, ";
                result.ExtraDiagnosticsMessage = message;
            }

            return result;
        }

        /// <nodoc />
        public static string GetExtraDiagnosticsMessageForTracing(this ResultBase result)
        {
            if (!string.IsNullOrEmpty(result.ExtraDiagnosticsMessage))
            {
                return " " + result.ExtraDiagnosticsMessage;
            }

            return string.Empty;
        }
    }
}
