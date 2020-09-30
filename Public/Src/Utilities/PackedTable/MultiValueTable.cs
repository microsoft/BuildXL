// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Represents a table with a sequence of values per ID.
    /// </summary>
    /// <remarks>
    /// The MultiValueTable's state is three lists: 
    /// - Values: the per-id count of how many relationships each TFromId has.
    /// - m_offsets: the per-id index into m_relations for each TFromId; calculated by a scan over Values.
    /// - m_relations: the collection of all TToIds for all relationships; sorted by TFromId then TToId.
    /// 
    /// For example, if we have ID 1 with values 10 and 11, ID 2 with no values, and ID 3 with values 12 and 13:
    /// SingleValues: [2, 0, 2]
    /// m_offsets: [0, 2, 2]
    /// m_multiValues: [10, 11, 12, 13]
    /// 
    /// Note that SingleValues is used just as implementation and isn't exposed publicly at all.
    /// </remarks>
    public class MultiValueTable<TId, TValue> : Table<TId, int>, IMultiValueTable<TId, TValue>
        where TId : unmanaged, Id<TId>
        where TValue : unmanaged
    {
        /// <summary>
        /// List of offsets per ID.
        /// </summary>
        /// <remarks>
        /// Computed from a scan over SingleValues, which is the per-ID count of MultiValues per element.
        /// When building incrementally, this list grows progressively; if this list has fewer elements
        /// than Count, it means only a prefix of all IDs have had their relations added yet.
        /// </remarks>
        protected readonly SpannableList<int> Offsets;

        /// <summary>
        /// List of all per-ID values.
        /// </summary>
        /// <remarks>
        /// Stored in order of ID..
        /// </remarks>
        protected readonly SpannableList<TValue> MultiValues;

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
            base.SaveToFile(directory, name);
            // we don't need to save m_offsets since it is calculated from the counts in SingleValues
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
        protected Span<TValue> GetSpan(TId id)
        {
            int offset = Offsets[id.FromId() - 1];
            int count = SingleValues[id.FromId() - 1];
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

            return default(TId).ToId(Count);
        }

        /// <summary>
        /// Fill to the same count as the base table, to enable safely setting any ID's values.
        /// </summary>
        public override void FillToBaseTableCount()
        {
            base.FillToBaseTableCount();

            if (BaseTableOpt.Count > Count)
            {
                int lastOffsetValue = 0;
                if (Offsets.Count > 0)
                {
                    lastOffsetValue = Offsets[^1];
                }
                Offsets.Fill(BaseTableOpt.Count - Count, lastOffsetValue);
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
            int index = id.FromId() - 1;
            return MultiValues.Enumerate(Offsets[index], SingleValues[index]);
        }
    }
}
