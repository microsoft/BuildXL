// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;


namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Provides a mechanism for retrying asynchronous operations that may fail due to transient conditions.
    /// </summary>
    public class AdoBuildRunnerRetryHandler
    {
        private readonly int m_maxAttempts;

        private readonly TimeSpan m_retryDelay = TimeSpan.FromMilliseconds(1000);

        /// <nodoc />
        public AdoBuildRunnerRetryHandler(int maxApiAttempts)
        {
            m_maxAttempts = maxApiAttempts;
        }

        /// <summary>
        /// Executes an asynchronous operation with retry logic, returning the result of the operation when successful.
        /// </summary>
        public Task<T> ExecuteAsync<T>(Func<Task<T>> action, string apiMethodName, ILogger? logger)
        {
            return ExecuteWithRetry(action, apiMethodName, logger);
        }

        /// <summary>
        /// Executes a non-returning asynchronous operation with retry logic.
        /// </summary>
        /// <remarks>
        /// This method returns a Task with a result of type Boolean to fit into the generic retry framework.
        /// The return value `true` is a placeholder, because the operation itself does not produce a result.
        /// </remarks>
        public Task ExecuteAsync(Func<Task> action, string apiMethodName, ILogger logger)
        {
            return ExecuteWithRetry(async () => { await action(); return true; }, apiMethodName, logger);
        }

        /// <summary>
        /// Attempts to execute the provided asynchronous API method multiple times until it succeeds or the maximum retry limit is reached.
        /// </summary>
        private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> apiMethod, string apiMethodName, ILogger? logger)
        {
            var retryAttempt = 1;
            for (; retryAttempt <= m_maxAttempts; retryAttempt++)
            {
                try
                {
                    return await apiMethod();
                }
                catch (Exception ex)
                {
                    if (retryAttempt == m_maxAttempts)
                    {
                        throw new Exception($"Failed to execute {apiMethodName} after {retryAttempt} attempts", ex);
                    }
                }

                logger?.Info($"Retrying {apiMethodName} at attempt {retryAttempt}");
                await Task.Delay(m_retryDelay * retryAttempt);
            }

            throw new Exception($"Failed to execute {apiMethodName} after {retryAttempt} attempts");
        }
    }
}