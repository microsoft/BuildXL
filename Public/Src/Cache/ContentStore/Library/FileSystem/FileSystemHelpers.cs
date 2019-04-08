// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.FileSystem
{
    /// <summary>
    ///     Helper methods for file system operations
    /// </summary>
    public static class FileSystemHelpers
    {
        /// <summary>
        ///     Retries an operation that may fail due to an IOException
        /// </summary>
        /// <param name="attemptCount">Number of times to catch an IOException before failing</param>
        /// <param name="initialRetryDelay">After the first exception, how long to delay before retrying.</param>
        /// <param name="retryDelayIncrease">After each successive exception, how much longer to delay each time.</param>
        /// <param name="exceptionAction">Action (e.g. logging) to call when an exception occurs.</param>
        /// <param name="throwOnLastException">
        ///     If true, an exception is thrown, and no retries are remaining, then re-throw the
        ///     exception after the callback to exceptionAction.
        /// </param>
        /// <param name="operation">The operation that may throw an IOException.</param>
        /// <returns>True if the operation completed at least once without throwing an exception, otherwise false.</returns>
        public static async Task<bool> RetryOnIOException(
            int attemptCount,
            TimeSpan initialRetryDelay,
            TimeSpan retryDelayIncrease,
            Action<IOException> exceptionAction,
            bool throwOnLastException,
            Func<Task> operation)
        {
            Contract.Requires(attemptCount > 0);
            Contract.Requires(exceptionAction != null);
            Contract.Requires(operation != null);

            TimeSpan delay = initialRetryDelay;

            for (int attempts = 0; attempts <= attemptCount; attempts++)
            {
                try
                {
                    await operation();

                    return true;
                }
                catch (IOException ioException)
                {
                    exceptionAction(ioException);
                    if (attempts + 1 == attemptCount && throwOnLastException)
                    {
                        throw;
                    }
                }

                await Task.Delay(delay);
                delay += retryDelayIncrease;
            }

            return false;
        }
    }
}
