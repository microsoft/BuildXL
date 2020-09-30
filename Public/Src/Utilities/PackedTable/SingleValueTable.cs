// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Defines a new ID space and set of base values for each ID.
    /// </summary>
    /// <remarks>
    /// This serves as the "core data" of the entity with the given ID.
    /// Other derived tables and relations can be built sharing the same base table.
    /// 
    /// ID 0 is always preallocated and always has the default TValue; this lets us
    /// distinguish uninitialized IDs from allocated IDs, and gives us a default
    /// sentinel value in every table.
    /// </remarks>
    public class SingleValueTable<TId, TValue> : Table<TId, TValue>, ISingleValueTable<TId, TValue>
        where TId : unmanaged, Id<TId>
        where TValue : unmanaged
    {
        /// <summary>
        /// Construct a SingleValueTable.
        /// </summary>
        public SingleValueTable(int capacity = DefaultCapacity) : base(capacity)
        { }

        /// <summary>
        /// Construct a derived SingleValueTable with the same IDs as the given base table.
        /// </summary>
        /// <param name="baseTable"></param>
        public SingleValueTable(ITable<TId> baseTable) : base(baseTable)
        { }

        /// <summary>
        /// Get a value from the table.
        /// </summary>
        public TValue this[TId id]
        {
            get
            {
                CheckValid(id);
                return SingleValues[id.FromId() - 1];
            }
            set
            {
                CheckValid(id);
                SingleValues[id.FromId() - 1] = value;
            }
        }

        /// <summary>
        /// Add the next value in the table.
        /// </summary>
        public TId Add(TValue value)
        {
            SingleValues.Add(value);
            return default(TId).ToId(Count);
        }

        /// <summary>
        /// Build a SingleValueTable which caches items by hash value, adding any item only once.
        /// </summary>
        public class CachingBuilder<TValueComparer>
            where TValueComparer : IEqualityComparer<TValue>, new()
        {
            /// <summary>
            /// Efficient lookup by hash value.
            /// </summary>
            /// <remarks>
            /// This is really only necessary when building the table, and should probably be split out into a builder type.
            /// </remarks>
            protected readonly Dictionary<TValue, TId> Entries = new Dictionary<TValue, TId>(new TValueComparer());

            /// <summary>
            /// The table being constructed.
            /// </summary>
            protected readonly SingleValueTable<TId, TValue> ValueTable;

            /// <summary>
            /// Construct a CachingBuilder.
            /// </summary>
            protected CachingBuilder(SingleValueTable<TId, TValue> valueTable)
            {
                ValueTable = valueTable;
                // Prepopulate the dictionary that does the caching
                for (int i = 0; i < ValueTable.Count; i++)
                {
                    TId id = default(TId).ToId(i + 1);
                    Entries.Add(ValueTable[id], id);
                }
            }

            /// <summary>
            /// Get or add the given value.
            /// </summary>
            public virtual TId GetOrAdd(TValue value)
            {
                if (Entries.TryGetValue(value, out TId id))
                {
                    return id;
                }
                else
                {
                    id = ValueTable.Add(value);
                    Entries.Add(value, id);
                    return id;
                }
            }

            /// <summary>
            /// Update the given value with the new value; if optCombiner is provided, use it to determine the new value.
            /// </summary>
            /// <param name="value">The updated value to use now.</param>
            /// <param name="optCombiner">Function to combine old and new values to determine final updated value.</param>
            /// <returns></returns>
            public virtual TId UpdateOrAdd(TValue value, Func<TValue, TValue, TValue> optCombiner = null)
            {
                if (Entries.TryGetValue(value, out TId id))
                {
                    // prefer new value if no combiner
                    TValue updated = optCombiner == null ? value : optCombiner(ValueTable[id], value);
                    ValueTable[id] = updated;
                    return id;
                }
                else
                {
                    id = ValueTable.Add(value);
                    Entries.Add(value, id);
                    return id;
                }
            }
        }
    }
}
