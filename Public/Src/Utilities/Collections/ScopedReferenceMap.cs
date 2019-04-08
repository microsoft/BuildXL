// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Allows creating reference scopes to key value pairs where value is created when the first reference
    /// to a key is opened and released when the last reference scope is closed.
    /// </summary>
    /// <typeparam name="TKey">the key type</typeparam>
    /// <typeparam name="TValue">the value type</typeparam>
    public abstract class ScopedReferenceMap<TKey, TValue>
    {
        /// <summary>
        /// The underlying set for the map
        /// </summary>
        private readonly ConcurrentBigSet<ScopedReferenceEntry> m_backingSet;

        /// <summary>
        /// The comparer used to compare key-value pairs
        /// </summary>
        private readonly IEqualityComparer<TKey> m_keyComparer;

        /// <summary>
        /// The access verification to ensure scopes can only be created by this object.
        /// </summary>
        private readonly object m_accessVerifier = new object();

        /// <summary>
        /// Returns the count of open scopes
        /// </summary>
        public int OpenScopeCount => m_backingSet.Count;

        /// <summary>
        /// Class constructor
        /// </summary>
        protected ScopedReferenceMap()
            : this(EqualityComparer<TKey>.Default)
        {
        }

        /// <summary>
        /// Class constructor
        /// </summary>
        protected ScopedReferenceMap(IEqualityComparer<TKey> keyComparer = null)
        {
            m_keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            m_backingSet = new ConcurrentBigSet<ScopedReferenceEntry>();
        }

        /// <summary>
        /// Creates a new value for the key
        /// </summary>
        /// <remarks>
        /// This method is called under the lock so should not do re-entrant calls into map.
        /// </remarks>
        protected abstract TValue CreateValue(TKey key);

        /// <summary>
        /// Releases a the value for the key when all references are released.
        /// </summary>
        /// <remarks>
        /// This method is called under the lock so should not do re-entrant calls into map.
        /// </remarks>
        protected abstract void ReleaseValue(TKey key, TValue value);

        /// <summary>
        /// Opens a scope which should be disposed when value is no longer in use.
        /// </summary>
        public Scope OpenScope(TKey key)
        {
            var scopedReferenceEntry = m_backingSet.UpdateItem(new ScopePendingItem(this, key, dereference: false), allowAdd: true).Item;
            return new Scope(this, key, scopedReferenceEntry.Value, m_accessVerifier);
        }

        /// <summary>
        /// Removes a reference to the key.
        /// </summary>
        private void CloseScope(TKey key)
        {
            m_backingSet.UpdateItem(new ScopePendingItem(this, key, dereference: true));
        }

        /// <summary>
        /// Represents an entry in a scoped reference map
        /// </summary>
        private readonly struct ScopedReferenceEntry
        {
            /// <summary>
            /// The key
            /// </summary>
            public readonly TKey Key;

            /// <summary>
            /// The value
            /// </summary>
            public readonly TValue Value;

            /// <summary>
            /// The number of open scopes for this entry
            /// </summary>
            public readonly int ReferenceCount;

            /// <summary>
            /// Constructor.
            /// </summary>
            public ScopedReferenceEntry(TKey key, TValue value, int referenceCount)
            {
                Key = key;
                Value = value;
                ReferenceCount = referenceCount;
            }
        }

        /// <summary>
        /// Represents an open reference to an entry in a scoped reference map.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public struct Scope : IDisposable
        {
            private ScopedReferenceMap<TKey, TValue> m_map;

            /// <summary>
            /// The key for the scope
            /// </summary>
            public readonly TKey Key;

            /// <summary>
            /// The created value for the scope
            /// </summary>
            public readonly TValue Value;

            /// <summary>
            /// Constructor. Internal use only.
            /// </summary>
            internal Scope(ScopedReferenceMap<TKey, TValue> map, TKey key, TValue value, object accessVerifier)
            {
                Contract.Assert(map.m_accessVerifier == accessVerifier, "Scopes can only be created by a parent ScopedReferenceMap");

                m_map = map;
                Key = key;
                Value = value;
            }

            /// <summary>
            /// Closes the scope, releasing the reference.
            /// </summary>
            public void Dispose()
            {
                if (m_map != null)
                {
                    m_map.CloseScope(Key);
                    m_map = null;
                }
            }
        }

        /// <summary>
        /// Handles update semantics for scoped reference entries in the backing set.
        /// </summary>
        private readonly struct ScopePendingItem : IPendingSetItem<ScopedReferenceEntry>
        {
            private readonly ScopedReferenceMap<TKey, TValue> m_map;
            private readonly bool m_dereference;
            public readonly TKey Key;

            public ScopePendingItem(ScopedReferenceMap<TKey, TValue> map, TKey key, bool dereference)
            {
                m_map = map;
                Key = key;
                m_dereference = dereference;
            }

            public int HashCode => m_map.m_keyComparer.GetHashCode(Key);

            public bool Equals(ScopedReferenceEntry other)
            {
                return m_map.m_keyComparer.Equals(Key, other.Key);
            }

            /// <summary>
            /// This handles creating the entry on first reference and removing the entry (and releasing on the value)
            /// for the scope on last dereference.
            /// </summary>
            public ScopedReferenceEntry CreateOrUpdateItem(ScopedReferenceEntry oldItem, bool hasOldItem, out bool remove)
            {
                TValue value;
                if (m_dereference)
                {
                    Contract.Assert(hasOldItem, "Extraneous dereference detected. Ensure that scope is only disposed once");

                    // Check if dereferencing the last reference. If so, remove the item.
                    remove = oldItem.ReferenceCount == 1;
                    if (remove)
                    {
                        m_map.ReleaseValue(Key, oldItem.Value);
                        return default(ScopedReferenceEntry);
                    }

                    value = oldItem.Value;
                    return new ScopedReferenceEntry(Key, value, oldItem.ReferenceCount - 1);
                }
                else
                {
                    remove = false;
                    value = hasOldItem ? oldItem.Value : m_map.CreateValue(Key);

                    return new ScopedReferenceEntry(Key, value, oldItem.ReferenceCount + 1);
                }
            }
        }
    }
}
