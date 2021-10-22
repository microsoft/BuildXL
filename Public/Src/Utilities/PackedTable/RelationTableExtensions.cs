// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// The four kinds of outcomes from a traversal filter.
    /// </summary>
    /// <remarks>
    /// When traversing a recursive relation, there are two independent choices:
    /// 1) should this element be included in the results of the traversal?
    /// 2) should the traversal continue to the relations of this element?
    /// 
    /// This enum captures all four possibilities for the answers to these two
    /// questions.
    /// </remarks>
    public enum TraversalFilterResult
    {
        /// <summary>Accept the element in the results, and continue beyond it.</summary>
        AcceptAndContinue,

        /// <summary>Accept the element in the results, and do not continue beyond it.</summary>
        AcceptAndHalt,

        /// <summary>Reject the element, and continue beyond it.</summary>
        RejectAndContinue,

        /// <summary>Reject the element, and do not continue beyond it.</summary>
        RejectAndHalt,
    }

    /// <summary>
    /// Extension methods for RelationTable.
    /// </summary>
    public static class RelationTableExtensions
    {
        /// <summary>
        /// Maximum number of times we will try to traverse before deciding we are in a cycle.
        /// </summary>
        public static readonly int MaximumTraverseLimit = 1000;

        /// <summary>Is this an Accept result?</summary>
        public static bool IsAccept(this TraversalFilterResult result)
        {
            return result == TraversalFilterResult.AcceptAndContinue
                || result == TraversalFilterResult.AcceptAndHalt;
        }

        /// <summary>Is this a Continue result?</summary>
        public static bool IsContinue(this TraversalFilterResult result)
        {
            return result == TraversalFilterResult.AcceptAndContinue
                || result == TraversalFilterResult.RejectAndContinue;
        }

        /// <summary>
        /// Recursively traverse this self-relationship, collecting all IDs that pass the isResult check.
        /// </summary>
        /// <remarks>
        /// Note that this extension method only applies to relations from a given ID type to itself.
        /// That is how the relation can be traversed multiple times.
        /// 
        /// This can be used, for instance, to collect all (and only) the process pips that are dependencies of a
        /// given pip: just traverse the PipDependencies relation looking for PipType.ProcessPips only.
        /// 
        /// The traversal stops once there are no more IDs to traverse, because the traversal reached a
        /// Halt result for all nodes, and/or because the traversal reached the end of the graph. If there
        /// is a cycle in the graph, the algorithm will fail after MaximumTraverseLimit iterations.
        /// </remarks>
        /// <typeparam name="TId">The ID type of the self-relationship.</typeparam>
        /// <param name="relation">The relation.</param>
        /// <param name="initial">The initial value to start the iteration.</param>
        /// <param name="filter">Returns whether to accept or reject the value, and whether to continue or halt the traversal.</param>
        /// <returns>The collection of all IDs that pass the isResult check, transitively from the initial ID.</returns>
        public static IEnumerable<TId> Traverse<TId>(
            this RelationTable<TId, TId> relation,
            TId initial,
            Func<TId, TraversalFilterResult> filter)
            where TId : unmanaged, Id<TId>
        {
            return relation.Traverse(new TId[] { initial }, filter);
        }

        /// <summary>
        /// Recursively traverse this self-relationship, collecting all IDs that pass the isResult check.
        /// </summary>
        /// <remarks>
        /// Note that this extension method only applies to relations from a given ID type to itself.
        /// That is how the relation can be traversed multiple times.
        /// 
        /// This can be used, for instance, to collect all (and only) the process pips that are dependencies of a
        /// given pip: just traverse the PipDependencies relation looking for PipType.ProcessPips only.
        /// 
        /// The traversal stops once there are no more IDs to traverse, because the traversal reached a
        /// Halt result for all nodes, and/or because the traversal reached the end of the graph. If there
        /// is a cycle in the graph, the algorithm will fail after MaximumTraverseLimit iterations.
        /// </remarks>
        /// <typeparam name="TId">The ID type of the self-relationship.</typeparam>
        /// <param name="relation">The relation.</param>
        /// <param name="initialValues">The initial values to start the iteration.</param>
        /// <param name="filter">Returns whether to accept or reject the value, and whether to continue or halt the traversal.</param>
        /// <returns>The collection of all IDs that pass the isResult check, transitively from the initial ID.</returns>
        public static IEnumerable<TId> Traverse<TId>(
            this RelationTable<TId, TId> relation,
            IEnumerable<TId> initialValues,
            Func<TId, TraversalFilterResult> filter)
            where TId : unmanaged, Id<TId>
        {
            var prospects = new HashSet<TId>(initialValues, default(TId).Comparer);
            var results = new HashSet<TId>(default(TId).Comparer);
            var nextProspects = new HashSet<TId>(default(TId).Comparer);

            var traverseCount = 0;
            while (prospects.Count > 0)
            {
                nextProspects.Clear();
                foreach (var next in prospects.SelectMany(p => relation.Enumerate(p)))
                {
                    TraversalFilterResult result = filter(next);

                    if (result.IsAccept())
                    {
                        results.Add(next);
                    }

                    if (result.IsContinue())
                    {
                        nextProspects.Add(next);
                    }
                }

                // swap the collections
                HashSet<TId> temp = prospects;
                prospects = nextProspects;
                nextProspects = temp;

                if (++traverseCount > MaximumTraverseLimit)
                {
                    throw new Exception($"Exceeded maximum relation traversal depth of {MaximumTraverseLimit}, probably due to cycle in data");
                }
            }

            return results;
        }
    }
}
