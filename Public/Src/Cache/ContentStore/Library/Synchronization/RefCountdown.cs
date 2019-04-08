// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;

namespace BuildXL.Cache.ContentStore.Synchronization
{
    /// <summary>
    ///     Thread-safe reference counter which triggers a callback whenever the ref count is 0.
    /// </summary>
    public class RefCountdown
    {
        private int _count;

        /// <summary>
        ///     Gets the count of references.
        /// </summary>
        public int Count => _count;

        /// <summary>
        ///     Handler for events fired when count is decremented to 0.
        /// </summary>
        public event EventHandler DecrementedToZero;

        /// <summary>
        ///     Increment the reference count.
        /// </summary>
        public void Increment()
        {
            Interlocked.Increment(ref _count);
        }

        /// <summary>
        ///     Decrement the reference count, with a minimum of 0. Triggers the callback if reference count is 0.
        /// </summary>
        public void Decrement()
        {
            var current = Interlocked.Decrement(ref _count);
            if (current <= 0)
            {
                if (current < 0)
                {
                    Interlocked.Increment(ref _count);
                }

                DecrementedToZero?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
