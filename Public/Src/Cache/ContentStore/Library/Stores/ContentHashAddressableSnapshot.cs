// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// This class is specifically for usage within <see cref="ContentHashAddressableSnapshot{T}"/>. It is a reference type on purpose, in order to avoid issues
    /// with the maximum object size limit.
    /// </summary>
    /// <typeparam name="T">Type tagged with a hash</typeparam>
    public class PayloadFromDisk<T> : IComparable<PayloadFromDisk<T>>
    {
        /// <summary>
        /// Hash for the <see cref="Payload"/>
        /// </summary>
        public readonly ContentHash Hash;

        /// <summary>
        /// Information for which <see cref="Hash"/> applies
        /// </summary>
        public readonly T Payload;

        /// <nodoc />
        public PayloadFromDisk(ContentHash hash, T payload)
        {
            Hash = hash;
            Payload = payload;
        }

        /// <nodoc />
        public int CompareTo(PayloadFromDisk<T> other)
        {
            return Hash.CompareTo(other.Hash);
        }
    }

    /// <summary>
    /// This class represents an immutable snapshot of a collection of items at an undetermined point in time, used for enumerations of the file
    /// system. It is made specifically to avoid issues with enumerations of large amounts of files which go over the maximum object size
    /// restriction.
    /// </summary>
    /// <typeparam name="T">Type held inside the snapshot</typeparam>
    public class ContentHashAddressableSnapshot<T> : IEnumerable<PayloadFromDisk<T>>
    {
        private List<PayloadFromDisk<T>>[] _snapshot;

        /// <nodoc />
        public readonly long Count = 0;

        /// <nodoc />
        public ContentHashAddressableSnapshot()
        {
            InitializeSnapshot();
        }

        /// <nodoc />
        public ContentHashAddressableSnapshot(IEnumerable<PayloadFromDisk<T>> snapshot)
        {
            InitializeSnapshot();

            foreach (var payload in snapshot)
            {
                var identifier = payload.Hash[0];
                _snapshot[identifier].Add(payload);
                Count++;
            }
        }

        /// <nodoc />
        private void InitializeSnapshot()
        {
            _snapshot = new List<PayloadFromDisk<T>>[256];
            for (var i = 0; i < _snapshot.Length; i++)
            {
                _snapshot[i] = new List<PayloadFromDisk<T>>();
            }
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

        /// <nodoc />
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

        /// <nodoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
