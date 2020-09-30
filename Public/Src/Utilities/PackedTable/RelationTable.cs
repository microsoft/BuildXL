// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

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
                int previous = newRelations[i - 1].FromId();
                int current = newRelations[i].FromId();
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
                    result.SingleValues[relatedId.FromId() - 1]++;
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
                    int relatedIdInt = relatedId.FromId() - 1;
                    int idInt = id.FromId() - 1;
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
                        SpanUtilities.Sort(finalSpan, (id1, id2) => id1.FromId().CompareTo(id2.FromId()));
                    }
                }
            }

            // TODO: error check that there are no zero entries in m_relations

            return result;
        }

        /// <summary>
        /// Build a Relation by adding unordered (from, to) tuples, and then finally completing the collection, which sorts
        /// and populates the Relation.
        /// </summary>
        public class Builder
        {
            /// <summary>
            /// The table being built.
            /// </summary>
            public readonly RelationTable<TFromId, TToId> Table;

            private readonly SpannableList<(TFromId fromId, TToId toId)> m_list;

            /// <summary>
            /// Construct a Builder.
            /// </summary>
            public Builder(RelationTable<TFromId, TToId> table, int capacity = DefaultCapacity)
            {
                Table = table ?? throw new ArgumentException("Table argument must not be null");
                m_list = new SpannableList<(TFromId, TToId)>(capacity);
            }

            /// <summary>
            /// Add this relationship.
            /// </summary>
            public void Add(TFromId fromId, TToId toId)
            {
                m_list.Add((fromId, toId));
            }

            /// <summary>
            /// All relationships have been added; sort them all and build the final relation table.
            /// </summary>
            public void Complete()
            {
                m_list.AsSpan().Sort((tuple1, tuple2) =>
                {
                    int fromIdCompare = tuple1.fromId.FromId().CompareTo(tuple2.fromId.FromId());
                    if (fromIdCompare != 0)
                    {
                        return fromIdCompare;
                    }
                    return tuple1.toId.FromId().CompareTo(tuple2.toId.FromId());
                });

                // and bin them by groups
                int listIndex = 0;
                SpannableList<TToId> buffer = new SpannableList<TToId>();
                int listCount = m_list.Count;
                Table.SetMultiValueCapacity(listCount);

                foreach (TFromId id in Table.BaseTableOpt.Ids)
                {
                    if (listIndex >= m_list.Count)
                    {
                        // ran outta entries, rest all 0
                        break;
                    }

                    // Count up how many are for id.
                    int count = 0;
                    buffer.Clear();
                    
                    // create a to-ID that will never equal any other ID (even default)
                    TToId lastToId = default(TToId).ToId(-1);

                    while (listIndex + count < m_list.Count)
                    {
                        var (fromId, toId) = m_list[listIndex + count];
                        if (fromId.Equals(id))
                        {
                            // drop duplicates (silently...)
                            // TODO: are duplicates here a logic bug? Because they do happen in practice.
                            if (!toId.Equals(lastToId))
                            {
                                buffer.Add(toId);
                            }
                            count++;
                            lastToId = toId;
                            continue;
                        }
                        // ok we're done
                        break;
                    }

                    Table.Add(buffer.AsSpan());
                    listIndex += count;
                }
            }
        }
    }
}
