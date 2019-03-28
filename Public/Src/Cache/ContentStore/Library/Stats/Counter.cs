// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;

namespace BuildXL.Cache.ContentStore.Stats
{
    /// <summary>
    ///     A numeric counter.
    /// </summary>
    public sealed class Counter
    {
        private long _value;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Counter"/> class.
        /// </summary>
        public Counter(string counterName)
        {
            Name = counterName;
        }

        /// <summary>
        ///     Gets the counter name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Gets the current counter value.
        /// </summary>
        public long Value => _value;

        /// <summary>
        ///     Increment the counter value.
        /// </summary>
        public void Add(long increment)
        {
            Interlocked.Add(ref _value, increment);
        }

        /// <summary>
        /// Increment the counter value by 1.
        /// </summary>
        public void Increment()
        {
            Interlocked.Increment(ref _value);
        }
    }
}
