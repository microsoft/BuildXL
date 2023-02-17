// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Engine.Cache.Plugin.CacheCore
{
    /// <summary>
    /// Utilities
    /// </summary>
    public static class Utilities
    {
        /// <summary>
        /// A wrapper for performing a cache operation with the given timeout duration
        /// </summary>
        public static async Task<Possible<TResult, Failure>> PerformCacheOperationAsync<TResult>(Func<Task<Possible<TResult, Failure>>> func, string operationName, TimeSpan timeout)
        {
            try
            {
                return await func().WithTimeoutAsync(timeout);
            }
            catch (TimeoutException)
            {
                return new CacheTimeoutFailure(operationName, (int)timeout.TotalMinutes);
            }
        }
    }
}
