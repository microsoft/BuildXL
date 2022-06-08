// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;

// AsyncMutexClient.exe - Executable that can acquire/release an AsyncMutex for testing purposes

namespace Test.BuildXL.Executables.AsyncMutexClient
{
    /// <nodoc/>
    public class Program
    {
        /// <nodoc/>
        public enum AsyncMutexClientAction
        {
            /// <nodoc/>
            Acquire = 0,
            /// <nodoc/>
            Release = 1,
            /// <nodoc/>
            AcquireAndRelease = 2,
        }

        /// <nodoc/>
        public static int Main(string[] args)
        {
            string mutexName = args[0];
            AsyncMutexClientAction action = (AsyncMutexClientAction) Enum.Parse(typeof(AsyncMutexClientAction),args[1]);

            return PerformActionAsync(mutexName, action).GetAwaiter().GetResult();
        }

        private static async Task<int> PerformActionAsync(string mutexName, AsyncMutexClientAction action)
        {
            using (var mutex = new AsyncMutex(mutexName))
            {
                try
                {
                    switch (action)
                    {
                        case AsyncMutexClientAction.Acquire:
                            await mutex.WaitOneAsync(CancellationToken.None);
                            break;
                        case AsyncMutexClientAction.Release:
                            mutex.ReleaseMutex();
                            break;
                        case AsyncMutexClientAction.AcquireAndRelease:
                            await mutex.WaitOneAsync(CancellationToken.None);
                            mutex.ReleaseMutex();
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(action));
                    }
                }
                catch (ApplicationException)
                {
                    return 1;
                }
            }

            return 0;
        }
    }
}
