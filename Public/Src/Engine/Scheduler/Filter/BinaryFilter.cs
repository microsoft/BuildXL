// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Binary filter. Evaluated left to right
    /// </summary>
    public class BinaryFilter : PipFilter
    {
        /// <summary>
        /// Left filter
        /// </summary>
        public readonly PipFilter Left;

        /// <summary>
        /// Right filter
        /// </summary>
        public readonly PipFilter Right;

        /// <summary>
        /// Operator
        /// </summary>
        public readonly FilterOperator FilterOperator;

        private readonly int m_cachedHashCode;

        /// <summary>
        /// Creates a new instance of <see cref="BinaryFilter"/>.
        /// </summary>
        public BinaryFilter(PipFilter left, FilterOperator op, PipFilter right)
        {
            Left = left;
            FilterOperator = op;
            Right = right;
            m_cachedHashCode = HashCodeHelper.Combine(Left.GetHashCode() ^ Right.GetHashCode(), FilterOperator.GetHashCode());
        }

        private FilterOperator GetEffectiveFilterOperator(bool negate)
        {
            if (negate)
            {
                return FilterOperator == FilterOperator.And ? FilterOperator.Or : FilterOperator.And;
            }

            return FilterOperator;
        }

        private IEnumerable<T> CombineFilters<T>(bool negate, Func<PipFilter, IEnumerable<T>> getFromFilter)
        {
            IEnumerable<T> left = getFromFilter(Left);

            var effectiveFilterOperator = GetEffectiveFilterOperator(negate);
            if (effectiveFilterOperator == FilterOperator.Or)
            {
                // When the operator is OR, we may only short circuit resolving values if both left and right have short circuit values
                if (left != null)
                {
                    IEnumerable<T> right = getFromFilter(Right);
                    if (right != null)
                    {
                        return left.Union(right);
                    }
                }

                return null;
            }
            else
            {
                Contract.Assert(effectiveFilterOperator == FilterOperator.And);

                // When the operator is AND, we can return either the right or left set of values to resolve.
                // No attempt is made to pick the side with fewer values to resolve since that doesn't necessarily
                // mean fewer overall values will be resolved. That depends on how deep in the evaluation tree the
                // values we pick to resolve are.
                if (left != null)
                {
                    return left;
                }

                IEnumerable<T> right = getFromFilter(Right);
                return right;
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<FullSymbol> GetValuesToResolve(bool negate = false)
        {
            return CombineFiltersLoop(f => f.GetValuesToResolve(negate), negate);
        }

        /// <inheritdoc/>
        public override IEnumerable<AbsolutePath> GetSpecRootsToResolve(bool negate = false)
        {
            return CombineFiltersLoop(f => f.GetSpecRootsToResolve(negate), negate);
        }

        /// <inheritdoc/>
        public override IEnumerable<StringId> GetModulesToResolve(bool negate = false)
        {
            return CombineFiltersLoop(f => f.GetModulesToResolve(negate), negate);
        }

        private IEnumerable<T> CombineFiltersLoop<T>(Func<PipFilter, IEnumerable<T>> toResolve, bool negate = false)
        {
            var flattenQueue = new Queue<PipFilter>();
            var flattenStack = new Stack<PipFilter>();
            var computedValues = new Dictionary<PipFilter, IEnumerable<T>>();

            flattenQueue.Enqueue(this);
            while (flattenQueue.Count > 0)
            {
                var f = flattenQueue.Dequeue();
                flattenStack.Push(f);
                if (f is BinaryFilter bf)
                {
                    flattenQueue.Enqueue(bf.Left);
                    flattenQueue.Enqueue(bf.Right);
                }
            }

            while (flattenStack.Count > 0)
            {
                var f = flattenStack.Pop();
                if (f is BinaryFilter bf && !computedValues.ContainsKey(bf))
                {
                    var lValue = computedValues[bf.Left];
                    var rValue = computedValues[bf.Right];
                    var effectiveFilterOperator = bf.GetEffectiveFilterOperator(negate);
                    if (effectiveFilterOperator == FilterOperator.Or)
                    {
                        if (lValue == null || rValue == null)
                        {
                            computedValues.Add(bf, null);
                        }
                        else
                        {
                            computedValues.Add(bf, lValue.Union(rValue).ToList());
                        }
                    }
                    else
                    {
                        Contract.Assert(effectiveFilterOperator == FilterOperator.And);
                        if (lValue != null)
                        {
                            computedValues.Add(bf, lValue);
                        }
                        else
                        {
                            computedValues.Add(bf, rValue);
                        }
                    }
                }
                else
                {
                    if (!computedValues.ContainsKey(f))
                    {
                        computedValues.Add(f, toResolve(f));
                    }
                }
            }

            return computedValues[this];
        }

        /// <inheritdoc/>
        public override void AddStatistics(ref FilterStatistics statistics)
        {
            statistics.BinaryFilterCount++;

            foreach (var child in TraverseBinaryChildFilters(bf => true))
            {
                if (child is BinaryFilter)
                {
                    statistics.BinaryFilterCount++;
                }
                else
                {
                    child.AddStatistics(ref statistics);
                }
            }
        }

        internal IEnumerable<PipFilter> TraverseBinaryChildFilters(Func<BinaryFilter, bool> includeBinaryChildFilterChildren)
        {
            Queue<PipFilter> childFilters = new Queue<PipFilter>();
            childFilters.Enqueue(Left);
            childFilters.Enqueue(Right);

            while (childFilters.Count > 0)
            {
                PipFilter child = childFilters.Dequeue();
                yield return child;

                BinaryFilter childAsBinary = child as BinaryFilter;
                if (childAsBinary != null && includeBinaryChildFilterChildren(childAsBinary))
                {
                    childFilters.Enqueue(childAsBinary.Left);
                    childFilters.Enqueue(childAsBinary.Right);
                }
            }
        }

        /// <inheritdoc/>
        protected override int GetDerivedSpecificHashCode()
        {
            return m_cachedHashCode;
        }

        /// <inheritdoc/>
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            BinaryFilter binaryFilter;
            return (binaryFilter = pipFilter as BinaryFilter) != null &&
                   FilterOperator == binaryFilter.FilterOperator &&
                   ((ReferenceEquals(Left, binaryFilter.Left) && ReferenceEquals(Right, binaryFilter.Right)) ||
                    (ReferenceEquals(Right, binaryFilter.Left) && ReferenceEquals(Left, binaryFilter.Right)));
        }

        /// <inheritdoc/>
        public override PipFilter Canonicalize(FilterCanonicalizer canonicalizer)
        {
            var visitedFilters = new HashSet<PipFilter>();
            var awaitingProcessing = new Stack<PipFilter>();
            Dictionary<PipFilter, PipFilter> canonicalizedFilters = new Dictionary<PipFilter, PipFilter>();
            Dictionary<PipFilter, PipFilter> reducedFilters = new Dictionary<PipFilter, PipFilter>();

            awaitingProcessing.Push(this);

            while (awaitingProcessing.Count > 0)
            {
                var pipFilter = awaitingProcessing.Pop();
                BinaryFilter asBinary = pipFilter as BinaryFilter;
                if (asBinary == null)
                {
                    canonicalizedFilters.Add(pipFilter, pipFilter.Canonicalize(canonicalizer));
                }
                else
                {
                    if (asBinary.FilterOperator == FilterOperator.Or && !canonicalizer.IsReduced(asBinary))
                    {
                        List<PipFilter> childFilters = new List<PipFilter>();

                        // Collect the transitive leaf child filters into a list
                        // NOTE: Children are only collected which are descended through only Or filters. Because of the
                        // commutative property of Or these filters can safely be rearranged and combined into equivalent
                        // filters
                        foreach (var child in asBinary.TraverseBinaryChildFilters(bf => bf.FilterOperator == FilterOperator.Or))
                        {
                            if (child is BinaryFilter bf && bf.FilterOperator == FilterOperator.Or)
                            {
                                // All nested binary filter are reduced after this operation
                                canonicalizer.MarkReduced(bf);

                                // Skip intermediate child Or filters
                                continue;
                            }

                            childFilters.Add(child);
                        }

                        if (TryReduce(childFilters, canonicalizer, out var reducedFilter))
                        {
                            // a reduced filter might not be canonicalized, so put it on stack for processing
                            awaitingProcessing.Push(reducedFilter);
                            reducedFilters.Add(asBinary, reducedFilter);
                            continue;
                        }
                    }

                    if (!visitedFilters.Contains(asBinary))
                    {
                        // 1st time seeing this filter; we are not ready yet to process it
                        // mark it, put it back, and deal with its children first
                        visitedFilters.Add(asBinary);
                        awaitingProcessing.Push(asBinary);
                        awaitingProcessing.Push(asBinary.Left);
                        awaitingProcessing.Push(asBinary.Right);
                    }
                    else
                    {
                        // 2nd time we are looking at this filter => its children have been canonicalized
                        var canonLeft = canonicalizedFilters[reducedFilters.ContainsKey(asBinary.Left) ? reducedFilters[asBinary.Left] : asBinary.Left];
                        var canonRight = canonicalizedFilters[reducedFilters.ContainsKey(asBinary.Right) ? reducedFilters[asBinary.Right] : asBinary.Right];

                        if (ReferenceEquals(canonLeft, canonRight))
                        {
                            canonicalizedFilters.Add(asBinary, canonLeft);
                        }
                        else
                        {
                            canonicalizedFilters.Add(
                                asBinary,
                                canonicalizer.TryGet(new BinaryFilter(canonRight, asBinary.FilterOperator, canonLeft), out var canonicalizedFilter)
                                    ? canonicalizedFilter
                                    : canonicalizer.GetOrAdd(new BinaryFilter(canonLeft, asBinary.FilterOperator, canonRight)));
                        }
                    }
                }
            }

            return canonicalizedFilters[reducedFilters.ContainsKey(this) ? reducedFilters[this] : this];
        }

        private static readonly IComparer<PipFilter> FilterKeyComparer =
            Comparer<PipFilter>.Create((p1, p2) => p1.UnionFilterKind.CompareTo(p2.UnionFilterKind));

        private static bool TryReduce(List<PipFilter> operandFilters, FilterCanonicalizer canonicalizer, out PipFilter reducedFilter)
        {
            // Example:
            // Combinable filters: T = Tag, M = Module, MT = MultiTag, 
            // Non-combinable filters: S = Spec, P = Pip
            //
            // [T1, M1, M2, T2, T3, (AND P1,P2), T4,  M3, M4, S1]
            // A. Sort
            // [(AND P1,P2), S1, T1, T2, T3, T4, M1, M2, M3, M4]
            // B. Walk through filters combining contiguous groups of same filter kind
            // 1.
            // operandFilters = [(AND P1,P2), S1, T1, T2, T3, T4, M1, M2, M3, M4]
            // currentFilter = null
            // 2.
            // operandFilters = [S1, T1, T2, T3, T4, M1, M2, M3, M4]
            // currentFilter = (AND P1,P2)
            // 3.
            // operandFilters = [T1, T2, T3, T4, M1, M2, M3, M4]
            // currentFilter = (OR (AND P1,P2) S1)
            // 4.
            // operandFilters = [M1, M2, M3, M4]
            // currentFilter = (OR (OR (AND P1,P2) S1) (MT 1,2,3,4))
            // 5.
            // operandFilters = [M1, M2, M3, M4]
            // currentFilter = (OR (OR (OR (AND P1,P2) S1) (MT 1,2,3,4)) (M 1,2,3,4))


            // Sort by filter key to group combinable filters
            // into contiguous regions
            operandFilters.Sort(FilterKeyComparer);

            List<PipFilter> filtersToCombine = null;
            UnionFilterKind lastFilterKind = UnionFilterKind.None;
            bool reduced = false;
            PipFilter currentFilter = null;

            foreach (var operandFilter in operandFilters)
            {
                var filterKind = operandFilter.UnionFilterKind;
                if (filterKind != UnionFilterKind.None)
                {
                    if (filterKind != lastFilterKind)
                    {
                        // Filter kind has changed. Combine current set of filters
                        // and start another region of same filter kind
                        combineFiltersAndAddToFinalFilter();
                    }

                    filtersToCombine = filtersToCombine ?? new List<PipFilter>();
                    filtersToCombine.Add(operandFilter);
                }
                else
                {
                    // Filter cannot be unioned, just add it to the result filter
                    addToFinalFilter(operandFilter);
                }

                lastFilterKind = filterKind;
            }

            // Combine and push the last group of combinable filters
            combineFiltersAndAddToFinalFilter();

            reducedFilter = currentFilter;
            return reduced;

            // Local functions

            // Combine the filters and add the unioned filter
            void combineFiltersAndAddToFinalFilter()
            {
                if (filtersToCombine != null)
                {
                    if (filtersToCombine.Count == 1)
                    {
                        addToFinalFilter(filtersToCombine[0]);
                    }
                    else if (filtersToCombine.Count >= 2)
                    {
                        reduced = true;
                        addToFinalFilter(filtersToCombine[0].Union(filtersToCombine));
                        filtersToCombine.Clear();
                    }
                }
            }

            // Push the reduced filter into the combined result
            void addToFinalFilter(PipFilter filter)
            {
                if (currentFilter == null)
                {
                    currentFilter = filter;
                }
                else
                {
                    var orFilter = new BinaryFilter(currentFilter, FilterOperator.Or, filter);
                    canonicalizer.MarkReduced(orFilter);

                    // Prevent redundant reduction on this filter
                    currentFilter = orFilter;
                }
            }
        }

        /// <inheritdoc/>
        public override IReadOnlySet<FileOrDirectoryArtifact> FilterOutputsCore(IPipFilterContext context, bool negate = false, IList<PipId> constrainingPips = null)
        {
            IReadOnlySet<FileOrDirectoryArtifact> left;
            IReadOnlySet<FileOrDirectoryArtifact> right;
            ReadOnlyHashSet<FileOrDirectoryArtifact> result;

            switch (GetEffectiveFilterOperator(negate))
            {
                case FilterOperator.And:
                    left = Left.FilterOutputs(context, negate, constrainingPips);

                    if (left.Count == 0)
                    {
                        // left returns no files, return left since it's already an empty set
                        return left;
                    }

                    right = Right.FilterOutputs(context, negate, new HashSet<PipId>(left.Select(l => context.GetProducer(l))).ToList());

                    if (right.Count == 0)
                    {
                        // right matches no files. return it as an empty set
                        return right;
                    }

                    // Each side has some set of files and we must compute the intersection
                    result = new ReadOnlyHashSet<FileOrDirectoryArtifact>(left);
                    result.IntersectWith(right);

                    return result;

                case FilterOperator.Or:
                    left = Left.FilterOutputs(context, negate, constrainingPips);
                    right = Right.FilterOutputs(context, negate, constrainingPips);

                    // If either left or right are an empty set, just return the other
                    if (right.Count == 0)
                    {
                        return left;
                    }

                    if (left.Count == 0)
                    {
                        return right;
                    }

                    result = new ReadOnlyHashSet<FileOrDirectoryArtifact>(left);
                    result.UnionWith(right);

                    return result;

                default:
                    Contract.Assume(false);
                    throw new NotImplementedException();
            }
        }
    }

    /// <summary>
    /// Operator for a compound filter
    /// </summary>
    public enum FilterOperator : byte
    {
        /// <summary>
        /// Logical and
        /// </summary>
        And = 0,

        /// <summary>
        /// Logical or
        /// </summary>
        Or = 1,
    }
}
