// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Represents one-to-many relationship between two tables.
    /// </summary>
    /// <remarks>
    /// The RelationTable is a MultiValueTable which stores relations between the BaseTableOpt and the RelatedTable.
    /// </remarks>
    public class RelationTable<TFromId, TToId> : MultiValueTable<TFromId, TToId>
        where TFromId : unmanaged, Id<TFromId>
        where TToId : unmanaged, Id<TToId>
    {
        /// <summary>
        /// The "target" table, at the many end of the one-to-many associations tracked by this table.
        /// </summary>
        public readonly ITable<TToId> RelatedTable;

        /// <summary>
        /// Construct a RelationTable between baseTable and relatedTable.
        /// </summary>
        /// <remarks>
        /// This currently must be constructed after baseTable has been fully populated,
        /// or this table will not be able to preallocate its id table properly.
        /// TODO: remove this restriction.
        /// </remarks>
        public RelationTable(ITable<TFromId> baseTable, ITable<TToId> relatedTable) : base(baseTable)
        {
            RelatedTable = relatedTable;
        }

        private static readonly IEqualityComparer<TToId> s_toComparer = default(TToId).Comparer;

        /// <summary>
        /// Construct a RelationTable from a one-to-one SingleValueTable.
        /// </summary>
        /// <remarks>
        /// The only real point of doing this is to be able to invert the resulting relation.
        /// </remarks>
        public static RelationTable<TFromId, TToId> FromSingleValueTable(
            SingleValueTable<TFromId, TToId> baseTable,
            ITable<TToId> relatedTable)
        {
            RelationTable<TFromId, TToId> result = new RelationTable<TFromId, TToId>(baseTable, relatedTable);

            TToId[] buffer = new TToId[1];
            TToId[] empty = new TToId[0];
            foreach (TFromId id in baseTable.Ids)
            {
                if (!s_toComparer.Equals(baseTable[id], default))
                {
                    buffer[0] = baseTable[id];
                    result.Add(buffer);
                }
                else
                {
                    result.Add(empty);
                }
            }

            return result;
        }

        /// <summary>
        /// Get the span of IDs related to the given ID.
        /// </summary>
        public override ReadOnlySpan<TToId> this[TFromId id]
        { 
            get => base[id];
            set
            {
                CheckRelatedIds(value);
                base[id] = value;
            }
        }

        private void CheckRelatedIds(ReadOnlySpan<TToId> ids)
        {
            foreach (TToId id in ids)
            {
                RelatedTable.CheckValid(id);
            }
        }

        /// <summary>
        /// Add relations in sequence; must always add the next id.
        /// </summary>
        /// <remarks>
        /// This supports only a very rudimentary form of appending, where we always append the
        /// full set of relations (possibly empty) of a subsequent ID in the base table.
        /// TODO: extend this to support skipping IDs without having to call for every non-related ID.
        /// </remarks>
        public override TFromId Add(ReadOnlySpan<TToId> newRelations)
        {
            CheckRelatedIds(newRelations);
            // Ensure newRelations are sorted.
            for (int i = 1; i < newRelations.Length; i++)
            {
                int previous = newRelations[i - 1].Value;
                int current = newRelations[i].Value;
                if (previous >= current)
                {
                    throw new ArgumentException($"Cannot add unsorted and/or duplicate data to RelationTable: data[{i - 1}] = {previous}; data[{i}] = {current}");
                }
            }

            return base.Add(newRelations);
        }

        /// <summary>
        /// Create a new RelationTable that is the inverse of this relation.
        /// </summary>
        /// <remarks>
        /// Very useful for calculating, for example, pip dependents based on pip dependencies, or
        /// per-file producers based on per-pip files produced.
        /// </remarks>
        public RelationTable<TToId, TFromId> Invert()
        {
            RelationTable<TToId, TFromId> result = new RelationTable<TToId, TFromId>(RelatedTable, BaseTableOpt);

            // We will use result.Values to accumulate the counts as usual.
            result.SingleValues.Fill(RelatedTable.Count, 0);
            // And we will use result.m_offsets to store the offsets as usual.
            result.Offsets.Fill(RelatedTable.Count, 0);

            int sum = 0;
            foreach (TFromId id in BaseTableOpt.Ids)
            {
                foreach (TToId relatedId in this[id])
                {
                    result.SingleValues[relatedId.Value - 1]++;
                    sum++;
                }
            }

            // Now we can calculate m_offsets.
            result.CalculateOffsets();

            // And we know the necessary size of m_relations.
            result.MultiValues.Capacity = sum;
            result.MultiValues.Fill(sum, default);

            // Allocate an array of positions to track how many relations we have filled in.
            SpannableList<int> positions = new SpannableList<int>(RelatedTable.Count + 1);
            positions.Fill(RelatedTable.Count + 1, 0);

            // And accumulate all the inverse relations.
            foreach (TFromId id in BaseTableOpt.Ids)
            {
                foreach (TToId relatedId in this[id])
                {
                    int relatedIdInt = relatedId.Value - 1;
                    int idInt = id.Value - 1;
                    int offset = result.Offsets[relatedIdInt];
                    int position = positions[relatedIdInt];
                    int relationIndex = result.Offsets[relatedIdInt] + positions[relatedIdInt];
                    result.MultiValues[relationIndex] = id;
                    positions[relatedIdInt]++;
                    if (positions[relatedIdInt] > result.SingleValues[relatedIdInt])
                    {
                        // this is a logic bug, should never happen
                        throw new Exception(
                            $"RelationTable.Inverse: logic exception: positions[{relatedIdInt}] = {positions[relatedIdInt]}, result.SingleValues[{relatedIdInt}] = {result.SingleValues[relatedIdInt]}");
                    }
                    else if (positions[relatedIdInt] == result.SingleValues[relatedIdInt])
                    {
                        // all the relations for this ID are known. now, we have to sort them.
                        Span<TFromId> finalSpan = 
                            result.MultiValues.AsSpan().Slice(result.Offsets[relatedIdInt], result.SingleValues[relatedIdInt]);
                        finalSpan.Sort((id1, id2) => id1.Value.CompareTo(id2.Value));
                    }
                }
            }

            // TODO: error check that there are no zero entries in m_relations

            return result;
        }

        /// <summary>
        /// Build a Relation by adding unordered (from, to) tuples, and then finally completing the collection, which sorts
        /// and deduplicates the Relation.
        /// </summary>
        /// <remarks>
        /// This type is more strict than the MultiValueTable.Builder it derives from; it ensures that all relations are sorted
        /// by TToId, and it deduplicates to ensure no duplicated TToId entries.
        /// </remarks>
        public class Builder : Builder<RelationTable<TFromId, TToId>>
        {
            /// <summary>
            /// Construct a Builder.
            /// </summary>
            public Builder(RelationTable<TFromId, TToId> table, int capacity = DefaultCapacity)
                : base(table, capacity)
            {
            }

            /// <summary>Compare these values; for relation tables, these must be sorted.</summary>
            public override int Compare(TToId value1, TToId value2) => value1.Value.CompareTo(value2.Value);

            /// <summary>Detect any duplicates; relation tables must not contain duplicate entries.</summary>
            public override bool IsConsideredDistinct(TToId value1, TToId value2) => !value1.Comparer.Equals(value1, value2);
        }
    }
}
