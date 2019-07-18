// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Interfaces.Synchronization.Internal
{
    /// <summary>
    ///     Disposable token for guarenteed release via a using() statement
    /// </summary>
    public struct SemaphoreSlimToken : IDisposable
    {
        private SemaphoreSlim _semaphore;

        private SemaphoreSlimToken(SemaphoreSlim semaphore)
            : this()
        {
            _semaphore = semaphore;
        }

        /// <summary>
        ///     Wait on a SemaphoreSlim and return a token that, when disposed, calls Release() on the SemaphoreSlim
        /// </summary>
        /// <param name="semaphore">The semaphore to wait on</param>
        /// <returns>A token that, when disposed, calls Release() on the SemaphoreSlim</returns>
        public static async Task<SemaphoreSlimToken> WaitAsync(SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            return new SemaphoreSlimToken(semaphore);
        }

        /// <summary>
        ///     Wait on a SemaphoreSlim and return a token that, when disposed, calls Release() on the SemaphoreSlim
        /// </summary>
        /// <param name="semaphore">The semaphore to wait on</param>
        /// <returns>A token that, when disposed, calls Release() on the SemaphoreSlim</returns>
        public static SemaphoreSlimToken Wait(SemaphoreSlim semaphore)
        {
            semaphore.Wait();
            return new SemaphoreSlimToken(semaphore);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_semaphore != null)
            {
                _semaphore.Release();
                _semaphore = null;
            }
        }

        // ReSharper disable UnusedParameter.Global

        /// <summary>
        ///     Equality operator.
        /// </summary>
        public static bool operator ==(SemaphoreSlimToken left, SemaphoreSlimToken right)
        {
            throw new InvalidOperationException();
        }

        /// <summary>
        ///     Inequality operator.
        /// </summary>
        public static bool operator !=(SemaphoreSlimToken left, SemaphoreSlimToken right)
        {
            throw new InvalidOperationException();
        }

        // ReSharper restore UnusedParameter.Global

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            throw new InvalidOperationException();
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            throw new InvalidOperationException();
        }
    }
}
