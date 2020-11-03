// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// A helper type for limiting the number of simultaneous operations.
    /// </summary>
    internal class ConcurrencyLimiter<T>
    {
        /// <summary>
        /// A set of hashes currently handled by the server.
        /// </summary>
        private readonly HashSet<T> _items = new HashSet<T>();

        private readonly object _syncRoot = new object();

        /// <summary>
        /// The max number of push handlers running at the same time.
        /// </summary>
        public int Limit { get; }

        /// <nodoc />
        public ConcurrencyLimiter(int limit) => Limit = limit;

        /// <nodoc />
        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _items.Count;
                }
            }
        }

        /// <nodoc />
        public (bool added, bool overTheLimit) TryAdd(T item, bool respectTheLimit)
        {
            lock (_syncRoot)
            {
                if (_items.Count < Limit || !respectTheLimit)
                {
                    return (added: _items.Add(item), overTheLimit: false);
                }
                else
                {
                    return (added: false, overTheLimit: true);
                }
            }
        }

        /// <nodoc />
        public bool Remove(T item)
        {
            lock (_syncRoot)
            {
                return _items.Remove(item);
            }
        }
    }
}
