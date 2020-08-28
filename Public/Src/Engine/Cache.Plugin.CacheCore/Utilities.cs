// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Utilities;
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
        public static async Task<Possible<TResult, Failure>> PerformCacheOperationAsync<TResult>(Func<Task<Possible<TResult, Failure>>> func, string operationName, int timeoutDurationMin)
        {
            try
            {
                return await func().WithTimeoutAsync(TimeSpan.FromMinutes(timeoutDurationMin));
            }
            catch (TimeoutException)
            {
                return new CacheTimeoutFailure(operationName, timeoutDurationMin);
            }
        }
    }
}
