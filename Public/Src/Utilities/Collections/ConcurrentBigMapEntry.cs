// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// This is a key value pair for the concurrent big map.
    /// </summary>
    /// <remarks>
    /// We can't use the built-in KeyValuePair because it does not implement GetHashCode and Equals.
    /// Using KeyValue would result in a lot more memory traffic when using the ConcurrentBigMap due to
    /// allocations made by boxing key/values when they are added to some internal members of ConcurrentBigMap
    /// </remarks>
    public readonly struct ConcurrentBigMapEntry<TKey, TValue> : IEquatable<ConcurrentBigMapEntry<TKey, TValue>>
    {
        /// <nodoc />
        public ConcurrentBigMapEntry(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }

        /// <nodoc />
        public TKey Key { get; }

        /// <nodoc />
        public TValue Value { get; }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(ConcurrentBigMapEntry<TKey, TValue> other)
        {
            return EqualityComparer<TKey>.Default.Equals(Key, other.Key) &&
                EqualityComparer<TValue>.Default.Equals(Value, other.Value);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Key, Value).GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            // Use the original KeyValuePair pair tostring for convenience.
            return new KeyValuePair<TKey, TValue>(Key, Value).ToString();
        }
    }
}
