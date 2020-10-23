// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;

#nullable enable
namespace BuildXL.Utilities.ParallelAlgorithms
{
    /// <nodoc />
    public static class ConcurrencyHelper
    {
        /// <summary>
        /// Runs a given <paramref name="func"/> once and returns the result provided by <paramref name="funcIsRunningResultProvider"/> if the func is already running.
        /// Note: <paramref name="syncRoot"/> must be initialized with 0 to avoid skipping the first call!
        /// </summary>
        public static T RunOnceIfNeeded<T>(ref int syncRoot, Func<T> func, Func<T> funcIsRunningResultProvider)
        {
            if (Interlocked.CompareExchange(ref syncRoot, value: 1, comparand: 0) == 0)
            {
                try
                {
                    return func();
                }
                finally
                {
                    Volatile.Write(ref syncRoot, 0);
                }
            }
            else
            {
                // Prevent re-entrancy so this method may be called during shutdown in addition
                // to being called in the timer
                return funcIsRunningResultProvider();
            }
        }

        /// <summary>
        /// Runs a given <paramref name="action"/> once.
        /// Note: <paramref name="syncRoot"/> must be initialized with 0 to avoid skipping the first call!
        /// </summary>
        public static void RunOnceIfNeeded(ref int syncRoot, Action action)
        {
            RunOnceIfNeeded<int>(ref syncRoot,
                () =>
                {
                    action();
                    return 1;
                },
                () => 1);
        }
    }
}