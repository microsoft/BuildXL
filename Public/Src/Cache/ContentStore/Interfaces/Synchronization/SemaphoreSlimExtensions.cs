// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Interfaces.Synchronization.Internal
{
    /// <summary>
    ///     Static class that provides a disposable token for guaranteed release via a using() statement
    /// </summary>
    public static class SemaphoreSlimExtensions
    {
        /// <summary>
        ///     Get a disposable token for guaranteed release via a using() statement.
        /// </summary>
        public static Task<SemaphoreSlimToken> WaitToken(this SemaphoreSlim semaphore)
        {
            return SemaphoreSlimToken.WaitAsync(semaphore);
        }
    }
}
