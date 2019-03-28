// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Tracks a number of pins in a thread-safe manner.
    /// </summary>
    public class Pin
    {
        /// <summary>
        ///     Gets while this is greater than zero, the corresponding content should not be purged.
        /// </summary>
        public int Count => _count;

        private int _count;

        /// <summary>
        ///     Increment the pin count.
        /// </summary>
        public void Increment()
        {
            Interlocked.Increment(ref _count);
        }

        /// <summary>
        ///     Increase the pin count by a given amount.
        /// </summary>
        /// <remarks>Use negative value for subtraction.</remarks>
        public void Add(int value)
        {
            Interlocked.Add(ref _count, value);
        }

        /// <summary>
        ///     Decrement the pin count.
        /// </summary>
        public void Decrement()
        {
            Interlocked.Decrement(ref _count);
        }
    }
}
