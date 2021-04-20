// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    internal static class ErrorHandlingUtilities
    {
        /// <summary>
        /// Awaits the <paramref name="task"/> and returns a failure if the task fails.
        /// </summary>
        public static async Task<BoolResult> FromAsync(this Task task)
        {
            try
            {
                await task;
                return BoolResult.Success;
            }
            catch(Exception e)
            {
                return new BoolResult(e);
            }
        }
    }
}
