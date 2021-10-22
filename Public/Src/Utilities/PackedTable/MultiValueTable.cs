// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Represents a table with a sequence of values per ID.
    /// </summary>
    /// <remarks>
    /// The MultiValueTable's state is three lists: 
    /// - Values: the per-id count of how many relationships each TFromId has.
    /// - Offsets: the per-id index into m_relations for each TFromId; calculated by a scan over Values.
    /// - MultiValues: the collection of all TValues.
    /// 
    /// For example, if we have ID 1 with values 10 and 11, ID 2 with no values, and ID 3 with values 12 and 13:
    /// SingleValues: [2, 0, 2]
    /// Offsets: [0, 2, 2]
    /// MultiValues: [10, 11, 12, 13]
    /// 
    /// Note that there are three ways to construct a MultiValueTable:
    /// 
    /// 1. The Add method can be called on an empty table (with zero Count). This lets a table be constructed
    ///    in ID order, such that it can be saved to disk..
    ///    
    /// 2. The AddUnordered method can be called on a filled table (one which has had FillToBaseTableCount()
    ///    called on it). This supports adding data out of strict ID order, but results in a table that can't
    ///    be saved directly to disk.
    ///    
    /// 3. The Builder class allows data to be accumulated in any order, and then added all at once in the
    ///    Complete() method; however, the data can't be queried before Complete() is called.
    /// </remarks>
    public class MultiValueTable<TId, TValue> : Table<TId, int>, IMultiValueTable<TId, TValue>
        where TId : unmanaged, Id<TId>
        where TValue : unmanaged
    {
        /// <summary>
        /// List of offsets per ID.
        /// </summary>
        /// <remarks>
        /// In the common (ordered) case, this is computed from a scan over SingleValues, which is the
        /// per-ID count of MultiValues per element. When building incrementally with Add, this list grows
        /// progressively; if this list has fewer elements than Count, it means only a prefix of all IDs
        /// have had their relations added yet.
        /// </remarks>
        protected readonly SpannableList<int> Offsets;

        /// <summary>
        /// List of all per-ID values.
        /// </summary>
        /// <remarks>
        /// Stored in order of ID in the default case; if AddUnordered has been called, these may be in any order.
        /// </remarks>
        protected readonly SpannableList<TValue> MultiValues;

        /// <summary>
        /// Set to true if data has been appended to this MultiValueTable in a non-ordered way
        /// (e.g. using AddUnordered).
        /// </summary>
        public bool MayBeUnordered { get; private set; }

        /// <summary>
        /// Construct a MultiValueTable.
        /// </summary>
        /// <remarks>
        /// This must be called after baseTable has been fully populated,
        /// or this table will not be able to preallocate its capacity.
        /// </remarks>
        public MultiValueTable(ITable<TId> baseTable) : base(baseTable)
        {
            Offsets = new SpannableList<int>(baseTable.Count == 0 ? DefaultCapacity : baseTable.Count);
            MultiValues = new SpannableList<TValue>();
        }

        /// <summary>
        /// Construct a MultiValueTable.
        /// </summary>
        /// <remarks>
        /// This must be called after baseTable has been fully populated,
        /// or this table will not be able to preallocate its capacity.
        /// </remarks>
        public MultiValueTable(int capacity = DefaultCapacity) : base(capacity)
        {
            Offsets = new SpannableList<int>(capacity);
            MultiValues = new SpannableList<TValue>();
        }

        /// <summary>
        /// Preallocate capacity for a known total number of multi-values (across all IDs in this table).
        /// </summary>
        public void SetMultiValueCapacity(int multiValueCapacity)
        {
            MultiValues.Capacity = multiValueCapacity;
        }

        /// <summary>
        /// Get the total number of multi-values (summed over all IDs).
        /// </summary>
        public int MultiValueCount => MultiValues.Count;

        /// <summary>
        /// Save to file.
        /// </summary>
        public override void SaveToFile(string directory, string name)
        {
            // Only save it if it's known to be ordered
            if (MayBeUnordered)
            {
                // TODO: make a way to reorder it!
                throw new Exception("Can't save MultiValueTable that may contain unordered data");
            }

            base.SaveToFile(directory, name);
            // we don't need to save Offsets since it is calculated from the counts in SingleValues
            FileSpanUtilities.SaveToFile(directory, InsertSuffix(name, "MultiValue"), MultiValues);
        }

        /// <summary>
        /// Load from file.
        /// </summary>
        public override void LoadFromFile(string directory, string name)
        {
            Offsets.Clear();
            MultiValues.Clear();

            base.LoadFromFile(directory, name);

            FileSpanUtilities.LoadFromFile(directory, InsertSuffix(name, "MultiValue"), MultiValues);

            Offsets.Fill(Count, default);
            CalculateOffsets();
        }

        /// <summary>Get read-only access to all the values.</summary>
        public ReadOnlySpan<TValue> MultiValueSpan => MultiValues.AsSpan();

        /// <summary>
        /// Calculate all the offsets for all IDs based on their counts.
        /// </summary>
        protected void CalculateOffsets()
        {
            for (int i = 1; i < Count; i++)
            {
                Offsets[i] = Offsets[i - 1] + SingleValues[i - 1];
            }
        }

        /// <summary>
        /// Get a span of values at the given ID.
        /// </summary>
        private Span<TValue> GetSpan(TId id)
        {
            int offset = Offsets[id.Value - 1];
            int count = SingleValues[id.Value - 1];
            return MultiValues.AsSpan().Slice(offset, count);
        }

        /// <summary>
        /// Get or set the values for the given ID.
        /// </summary>
        /// <remarks>
        /// If setting, the number of values provided must match the number of values currently associated with the ID.
        /// </remarks>
        public virtual ReadOnlySpan<TValue> this[TId id]
        {
            get
            {
                CheckValid(id);
                return GetSpan(id);
            }
            set
            {
                CheckValid(id);
                Span<TValue> contents = GetSpan(id);
                value.CopyTo(contents);
            }
        }

        /// <summary>
        /// Add the next set of values; return the ID that was allocated.
        /// </summary>
        /// <remarks>
        /// This only supports appending all the sets of values ID by ID in order.
        /// TODO: allow more flexible building.
        /// </remarks>
        public virtual TId Add(ReadOnlySpan<TValue> multiValues)
        {
            if (BaseTableOpt != null && SingleValues.Count == BaseTableOpt.Count)
            {
                throw new Exception("Can't add to end of a table that has already been filled");
            }

            if (SingleValues.Count > 0)
            {
                Offsets.Add(Offsets[Count - 1] + SingleValues[Count - 1]);
            }
            else
            {
                Offsets.Add(0);
            }

            SingleValues.Add(multiValues.Length);

            MultiValues.AddRange(multiValues);

            return default(TId).CreateFrom(Count);
        }

        /// <summary>
        /// Add a set of values for an ID that has no values yet, regardless of whether the ID is next in order
        /// to be added.
        /// </summary>
        /// <remarks>
        /// This results in out-of-order data, e.g. MayBeOutOfOrder is set to true when this method is called.
        /// TODO: create a way to re-order a MultiValueTable.
        /// 
        /// Note that this also breaks other assumptions of the Add method above. Specifically, this method
        /// requires that FillToBaseTableCount() has been called on this table already, whereas the normal
        /// Add method requires the opposite. In fact, this method requires having a BaseTable.
        /// </remarks>
        public virtual void AddUnordered(TId id, ReadOnlySpan<TValue> multiValues)
        {
            MayBeUnordered = true;

            if (BaseTableOpt == null)
            {
                // There must be a base table or we don't know how big this table should be, and hence can't safely
                // add random IDs to it.
                throw new InvalidOperationException("Can only call AddUnordered on a table with a base table");
            }

            if (SingleValues.Count < BaseTableOpt.Count)
            {
                // We must have already filled to the base table count.
                throw new Exception("MultiValueTable must be filled to base table count before calling AddUnordered");
            }

            int index = id.Value - 1;
            if (SingleValues[index] > 0)
            {
                // Can only add actual data for a given index once.
                throw new Exception($"MultiValueTable.AddUnordered can't add data twice for the same ID {id}");
            }

            Offsets[id.Value - 1] = MultiValueCount;
            SingleValues[id.Value - 1] = multiValues.Length;
            MultiValues.AddRange(multiValues);
        }

        /// <summary>
        /// Fill to the same count as the base table, to enable safely setting any ID's values.
        /// </summary>
        public override void FillToBaseTableCount()
        {
            base.FillToBaseTableCount();

            if (BaseTableOpt.Count > Offsets.Count)
            {
                int lastOffsetValue = 0;
                if (Offsets.Count > 0)
                {
                    lastOffsetValue = Offsets[^1];
                }
                Offsets.Fill(BaseTableOpt.Count - Offsets.Count, lastOffsetValue);
            }
        }

        /// <summary>
        /// Return a string showing the full contents of the table.
        /// </summary>
        /// <remarks>
        /// FOR UNIT TESTING ONLY! Will OOM your machine if you call this on a really large table.
        /// </remarks>
        public string ToFullString() => $"SingleValues {SingleValues.ToFullString()}, m_offsets {Offsets.ToFullString()}, m_multiValues {MultiValues.ToFullString()}";

        /// <summary>
        /// Enumerate all values at the given ID; very useful for LINQ.
        /// </summary>
        public IEnumerable<TValue> Enumerate(TId id)
        {
            int index = id.Value - 1;
            return MultiValues.Enumerate(Offsets[index], SingleValues[index]);
        }
        /// <summary>
        /// Enumerate all values at the given ID, returning tuples including the ID; very useful for LINQ.
        /// </summary>
        public IEnumerable<(TId, TValue)> EnumerateWithId(TId id)
            => Enumerate(id).Select(value => (id, value));

        /// <summary>
        /// Enumerate the whole relation as tuples of (id, value).
        /// </summary>
        public IEnumerable<(TId, TValue)> EnumerateWithIds()
            => Ids.SelectMany(id => EnumerateWithId(id));

        /// <summary>Build a MultiValueTable by adding unordered tuples and finally completing, which sorts by ID and builds the final table.</summary>
        public class Builder<TTable>
            where TTable : MultiValueTable<TId, TValue>
        {
            /// <summary>The table being built.</summary>
            public readonly TTable Table;

            private readonly SpannableList<(TId id, TValue value)> m_list;

            /// <summary>Construct a Builder.</summary>
            public Builder(TTable table, int capacity = DefaultCapacity)
            {
                Table = table ?? throw new ArgumentException("Table argument must not be null");
                m_list = new SpannableList<(TId, TValue)>(capacity);
            }

            /// <summary>Add this datum.</summary>
            public void Add(TId id, TValue value)
            {
                m_list.Add((id, value));
            }

            /// <summary>Compare TValues if they need to be sorted.</summary>
            /// <remarks>
            /// This is never the case for an ordinary MultiValueTable.Builder, but the subtype RelationTable.Builder
            /// does sort its values.
            /// </remarks>
            public virtual int Compare(TValue value1, TValue value2) => 0;

            /// <summary>Are these two values distinct?</summary>
            /// <remarks>
            /// If so, then the second value will be added. This allows subclasses of this builder to deduplicate;
            /// the default is not to deduplicate (e.g. consider all values distinct, without checking).
            /// </remarks>
            public virtual bool IsConsideredDistinct(TValue value1, TValue value2) => true;

            /// <summary>Static comparer instance to avoid possible repeated allocation.</summary>
            private static readonly IEqualityComparer<TId> s_idComparer = default(TId).Comparer;

            /// <summary>
            /// All relationships have been added; sort them all and build the final relation table.
            /// </summary>
            public void Complete()
            {
                Comparison<(TId id, TValue value)> tupleComparison =
                    (tuple1, tuple2) =>
                    {
                        int fromIdCompare = tuple1.id.Value.CompareTo(tuple2.id.Value);
                        if (fromIdCompare != 0)
                        {
                            return fromIdCompare;
                        }
                        return Compare(tuple1.value, tuple2.value);
                    };

                m_list.AsSpan().Sort(tupleComparison);

                // and bin them by groups
                int listIndex = 0;
                SpannableList<TValue> buffer = new SpannableList<TValue>();
                int listCount = m_list.Count;
                Table.SetMultiValueCapacity(listCount);

                foreach (TId baseId in Table.BaseTableOpt.Ids)
                {
                    // Count up how many are for id.
                    int count = 0;
                    buffer.Clear();

                    while (listIndex + count < m_list.Count)
                    {
                        var (id, value) = m_list[listIndex + count];
                        if (s_idComparer.Equals(baseId, id))
                        {
                            if (buffer.Count == 0 || IsConsideredDistinct(buffer[buffer.Count - 1], value))
                            {
                                buffer.Add(value);
                            }
                            count++;
                        }
                        else
                        {
                            // ok we're done with this baseId, let's move to the next one.
                            break;
                        }
                    }

                    Table.Add(buffer.AsSpan());
                    listIndex += count;
                }

                // and finish by filling out to the full number of (blank) entries.
                Table.FillToBaseTableCount();
            }
        }
    }
}
