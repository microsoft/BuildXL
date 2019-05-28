// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// This class represents an immutable snapshot of a collection of items at an undetermined point in time, used for enumerations of the file
    /// system. It is made specifically to avoid issues with enumerations of large amounts of files which go over the maximum object size
    /// restriction.
    /// </summary>
    /// <typeparam name="T">Type held inside the snapshot</typeparam>
    public class ContentDirectorySnapshot<T> : IEnumerable<PayloadFromDisk<T>>
    {
        private readonly List<PayloadFromDisk<T>>[] _snapshot;
        private readonly BitArray _sorted;

        /// <nodoc />
        public long Count { get; private set; }

        /// <nodoc />
        public ContentDirectorySnapshot()
        {
            _snapshot = InitializeSnapshot();

            // Empty list is always sorted, so we initialize everything to true
            _sorted = new BitArray(_snapshot.Length, true);
        }
        
        private List<PayloadFromDisk<T>>[] InitializeSnapshot()
        {
            var snapshot = new List<PayloadFromDisk<T>>[byte.MaxValue + 1];
            for (var i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = new List<PayloadFromDisk<T>>();
            }

            return snapshot;
        }

        /// <nodoc />
        public void Add(IEnumerable<PayloadFromDisk<T>> snapshot)
        {
            foreach (var payload in snapshot)
            {
                Add(payload);
            }
        }

        /// <nodoc />
        public void Add(PayloadFromDisk<T> payload)
        {
            // We are using the first byte of the hash to round-robin entries across multiple lists
            byte identifier = payload.Hash[0];
            _snapshot[identifier].Add(payload);
            _sorted.Set(identifier, false);
            Count++;
        }

        /// <nodoc />
        public List<PayloadFromDisk<T>> ListOrderedByHash()
        {
            var comparer = new ByHashPayloadFromDiskComparer<T>();
            Parallel.For(0, _snapshot.Length, i =>
            {
                if (!_sorted.Get(i))
                {
                    _snapshot[i].Sort(comparer);
                }
            });

            var result = new List<PayloadFromDisk<T>>();
            foreach (var list in _snapshot)
            {
                // Each list has hashes whose most significant byte is exactly the bucket selector, so we can just append
                result.AddRange(list);
            }

            return result;
        }

        /// <summary>
        /// Generates a similar result to grouping the <see cref="PayloadFromDisk{T}"/> by their hashes. The order of the returned
        /// groups may not be the same as in GroupBy. Moreover, the order across subsequent calls may not be preserved if the call
        /// is interleaved with a call to <see cref="ListOrderedByHash"/>.
        /// </summary>
        public IEnumerable<IGrouping<ContentHash, PayloadFromDisk<T>>> GroupByHash()
        {
            foreach (var bucket in _snapshot)
            {
                foreach (var group in bucket.GroupBy(p => p.Hash))
                {
                    yield return group;
                }
            }
        }

        /// <inheritdoc />
        public IEnumerator<PayloadFromDisk<T>> GetEnumerator()
        {
            foreach (var bucket in _snapshot)
            {
                foreach (var payload in bucket)
                {
                    yield return payload;
                }
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
