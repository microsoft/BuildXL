// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// A static class with helper functions and utilities used throughout the native I/O implementations
    /// </summary>
    public static class Helpers
    {
        /// <summary>
        /// Total number of attempts to complete when retrying an action that may fail
        /// </summary>
        public const int NumberOfAttempts = 3;

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
        /// <remarks>
        /// If rethrowExceptions and logExceptions are both false, unhandled exceptions will be lost.
        /// </remarks>
        public static bool RetryOnFailure(Func<bool, bool> work, bool logExceptions = true, bool rethrowExceptions = false)
        {
            Contract.Requires(work != null);

            const int PostAttemptSleepMsMultiplier = 10; // 10ms ^ 0, 10ms ^ 1, etc.

            int sleepMs = 100; // multiplier ^ 0
            bool done = false;
            for (int attempt = 0; !done && attempt < NumberOfAttempts; attempt++)
            {
                if (attempt != 0)
                {
                    Thread.Sleep(sleepMs);
                    sleepMs *= PostAttemptSleepMsMultiplier;
                }

                try
                {
                    done = work(attempt + 1 >= NumberOfAttempts);
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
                        Tracing.Logger.Log.RetryOnFailureException(Events.StaticContext, e.Message);
                    }
                }
            }

            return done;
        }
    }
}
