// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// A static class with helper functions and utilities used throughout the native I/O implementations
    /// </summary>
    public static class Helpers
    {
        /// <summary>
        /// Default total number of attempts to complete when retrying an action that may fail.
        /// </summary>
        public const int DefaultNumberOfAttempts = 3;

        /// <summary>
        /// Default initial timeout in milliseconds.
        /// </summary>
        private const int DefaultInitialTimeoutMs = 100;

        /// <summary>
        /// Default post timeout multiplier.
        /// </summary>
        private const int DefaultPostTimeoutMultiplier = 10;

        /// <summary>
        /// Helper function to retry file operations with transient error behaviors.
        /// </summary>
        /// <param name="work">
        /// The <code>work</code> function is invoked a few times; its parameter indicates if the invocation is the last one before
        /// finally giving up; its return value indicates if all work has finished successfully and no further attempt is needed.
        /// </param>
        /// <param name="logExceptions">
        /// Whether exceptions unhandled by the caller should be logged. By default, exceptions will be logged.
        /// </param>
        /// <param name="rethrowExceptions">
        /// Whether exceptions unhandled by the caller should be re-thrown, breaking the retry loop. By default, exceptions will be ignored and work() will be retried.
        /// </param>
        /// <param name="numberOfAttempts">
        /// Total number of attempts to complete when trying an action that may fail.
        /// </param>
        /// <param name="initialTimeoutMs">
        /// Initial timeout in millisecond.
        /// </param>
        /// <param name="postTimeoutMultiplier">
        /// Post timeout multiplier, i.e., after i-th attempt, the timeout is initialTimeoutMs * (postTimeoutMultiplier)^{i-1}.
        /// </param>
        /// <remarks>
        /// If rethrowExceptions and logExceptions are both false, unhandled exceptions will be lost.
        /// </remarks>
        public static bool RetryOnFailure(
            Func<bool, bool> work, 
            bool logExceptions = true, 
            bool rethrowExceptions = false,
            int numberOfAttempts = DefaultNumberOfAttempts,
            int initialTimeoutMs = DefaultInitialTimeoutMs,
            int postTimeoutMultiplier = DefaultPostTimeoutMultiplier)
        {
            Contract.Requires(work != null);
            Contract.Requires(logExceptions || rethrowExceptions);
            Contract.Requires(numberOfAttempts > 0);

            int timeoutMs = initialTimeoutMs;
            bool done = false;
            for (int attempt = 0; !done && attempt < numberOfAttempts; attempt++)
            {
                if (attempt != 0)
                {
                    Thread.Sleep(timeoutMs);
                    timeoutMs *= postTimeoutMultiplier;
                }

                try
                {
                    done = work(attempt + 1 >= numberOfAttempts);
                }
                catch (Exception e)
                {
                    // Give the caller opportunity to re-throw the exception to break the retry loop
                    if (rethrowExceptions)
                    {
                        throw;
                    }
                    
                    // Only log exceptions if they are not being re-thrown already
                    if (logExceptions)
                    {
                        Tracing.Logger.Log.RetryOnFailureException(Events.StaticContext, e.ToString());
                    }
                }
            }

            return done;
        }

        /// <summary>
        /// Async version of <see cref="RetryOnFailure"/>.
        /// </summary>
        public static async Task<bool> RetryOnFailureAsync(
            Func<bool, Task<bool>> work, 
            bool logExceptions = true, 
            bool rethrowExceptions = false,
            int numberOfAttempts = DefaultNumberOfAttempts,
            int initialTimeoutMs = DefaultInitialTimeoutMs,
            int postTimeoutMultiplier = DefaultPostTimeoutMultiplier)
        {
            Contract.Requires(work != null);
            Contract.Requires(logExceptions || rethrowExceptions);
            Contract.Requires(numberOfAttempts > 0);

            int timeoutMs = initialTimeoutMs;
            bool done = false;
            for (int attempt = 0; !done && attempt < numberOfAttempts; attempt++)
            {
                if (attempt != 0)
                {
                    await Task.Delay(timeoutMs);
                    timeoutMs *= postTimeoutMultiplier;
                }

                try
                {
                    done = await work(attempt + 1 >= numberOfAttempts);
                }
                catch (Exception e)
                {
                    // Give the caller opportunity to re-throw the exception to break the retry loop
                    if (rethrowExceptions)
                    {
                        throw;
                    }

                    // Only log exceptions if they are not being re-thrown already
                    if (logExceptions)
                    {
                        Tracing.Logger.Log.RetryOnFailureException(Events.StaticContext, e.ToString());
                    }
                }
            }

            return done;
        }

        /// <summary>
        /// A generic version of <see cref="RetryOnFailure"/>.
        /// </summary>
        public static Possible<TResult, Failure> RetryOnFailure<TResult>(
          Func<bool, Possible<TResult, Failure>> work,
          bool logExceptions = true,
          bool rethrowExceptions = false,
          int numberOfAttempts = DefaultNumberOfAttempts,
          int initialTimeoutMs = DefaultInitialTimeoutMs,
          int postTimeoutMultiplier = DefaultPostTimeoutMultiplier)
        {
            Contract.Requires(work != null);
            Contract.Requires(logExceptions || rethrowExceptions);
            Contract.Requires(numberOfAttempts > 0);

            Possible<TResult, Failure>? possiblyResult = null;

            bool success = RetryOnFailure(
                isLastRetry =>
                {
                    possiblyResult = work(isLastRetry);
                    return possiblyResult.Value.Succeeded;
                },
                logExceptions, rethrowExceptions, numberOfAttempts, initialTimeoutMs, postTimeoutMultiplier);

            // Since work returns Possible<TResult, Failure>, we expect it to gracefully handle any exception
            // (i.e., return a failure instead of throwing an exception). However, there is no guarantee that
            // it would behave this way. 
            if (!possiblyResult.HasValue)
            {
                Contract.Assert(false, $"An operation resulted in an unhandled exception in all of the '{numberOfAttempts}' retry attempts");
            }

            return possiblyResult.Value;
        }

        /// <summary>
        /// Async version of <see cref="RetryOnFailure"/>.
        /// </summary>
        public static async Task<Possible<TResult, Failure>> RetryOnFailureAsync<TResult>(
            Func<bool, Task<Possible<TResult, Failure>>> work,
            bool logExceptions = true,
            bool rethrowExceptions = false,
            int numberOfAttempts = DefaultNumberOfAttempts,
            int initialTimeoutMs = DefaultInitialTimeoutMs,
            int postTimeoutMultiplier = DefaultPostTimeoutMultiplier)
        {
            Contract.Requires(work != null);
            Contract.Requires(logExceptions || rethrowExceptions);
            Contract.Requires(numberOfAttempts > 0);

            Possible<TResult>? possiblyResult = null;

            bool success = await RetryOnFailureAsync(
                async isLastRetry =>
                {
                    possiblyResult = await work(isLastRetry);
                    return possiblyResult.Value.Succeeded;
                },
                logExceptions, rethrowExceptions, numberOfAttempts, initialTimeoutMs, postTimeoutMultiplier);

            // Since work returns Possible<TResult, Failure>, we expect it to gracefully handle any exception
            // (i.e., return a failure instead of throwing an exception). However, there is no guarantee that
            // it would behave this way. 
            if (!possiblyResult.HasValue)
            {
                Contract.Assert(false, $"An operation resulted in an unhandled exception in all of the '{numberOfAttempts}' retry attempts");
            }

            return possiblyResult.Value;
        }
    }
}
