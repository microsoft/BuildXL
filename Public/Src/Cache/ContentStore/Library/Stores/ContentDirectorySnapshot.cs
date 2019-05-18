// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

        /// <nodoc />
        public long Count { get; } = 0;

        /// <nodoc />
        public ContentDirectorySnapshot()
        {
            _snapshot = InitializeSnapshot();
        }

        /// <nodoc />
        public ContentDirectorySnapshot(IEnumerable<PayloadFromDisk<T>> snapshot)
        {
            _snapshot = InitializeSnapshot();

            foreach (var payload in snapshot)
            {
                var identifier = payload.Hash[0];
                _snapshot[identifier].Add(payload);
                Count++;
            }
        }
        
        private List<PayloadFromDisk<T>>[] InitializeSnapshot()
        {
            var snapshot = new List<PayloadFromDisk<T>>[byte.MaxValue];
            for (var i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = new List<PayloadFromDisk<T>>();
            }

            return snapshot;
        }

        /// <nodoc />
        public List<PayloadFromDisk<T>> ListOrderedByHash()
        {
            var result = this.ToList();
            result.Sort();
            return result;
        }

        /// <summary>
        /// Generates a similar result to grouping the <see cref="PayloadFromDisk{T}"/> by their hashes. The order of the returned
        /// groups may not be the same as in GroupBy.
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
