// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        /// <summary>
        /// Awaits the task obtained via <paramref name="taskProvider"/> and returns a failure if the task fails.
        /// </summary>
        public static async Task<ObjectResult<TResult>> ToResult<TResult>(this Task<TResult> taskProvider) where TResult: class
        {
            try
            {
                var result = await taskProvider;
                return new ObjectResult<TResult>(result);
            }
            catch (Exception e)
            {
                return new ObjectResult<TResult>(e);
            }
        }

        /// <summary>
        /// Awaits the task obtained via <paramref name="taskProvider"/> and returns a failure if the task fails.
        /// </summary>
        public static async Task<StructResult<TResult>> FromStructAsync<TResult>(Task<TResult> taskProvider) where TResult : struct
        {
            try
            {
                var result = await taskProvider;
                return new StructResult<TResult>(result);
            }
            catch (Exception e)
            {
                return new StructResult<TResult>(e);
            }
        }
    }
}
