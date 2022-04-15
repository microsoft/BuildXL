// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Parallel sorting extension method for Memory[T]
    /// </summary>
    /// <remarks>
    /// Hand coded using our weak assumptions of "OK to temporarily allocate an O(N) output buffer".
    /// </remarks>
    public static class ParallelMemorySortExtensions
    {
        internal delegate void SpanSort<T>(Span<T> span);

        /// <summary>
        /// Sort this Span.
        /// </summary>
        public static void ParallelSort<T>(this Memory<T> memory, IComparer<T> comparer, int minimumSubspanSize = 1024, int parallelism = -1)
        {
            new ParallelSortHelper<T>(memory, minimumSubspanSize, parallelism).Sort(s => s.Sort(comparer), (i, j) => comparer.Compare(i, j));
        }

        /// <summary>
        /// Sort this Span.
        /// </summary>
        public static void ParallelSort<T>(this Memory<T> memory, Comparison<T> comparison, int minimumSubspanSize = 1024, int parallelism = -1)
        {
            new ParallelSortHelper<T>(memory, minimumSubspanSize, parallelism).Sort(s => s.Sort(comparison), comparison);
        }

        /// <summary>
        /// Helper class for parallel sorting.
        /// </summary>
        internal class ParallelSortHelper<T>
        {
            /// <summary>
            /// The minimum size of subspans (to avoid merge overhead when small spans are being sorted).
            /// </summary>
            /// <remarks>
            /// This is not hardcoded because setting a small value here is useful when testing.
            /// </remarks>
            int minimumSubspanSize;
            /// <summary>
            /// The Memory we're sorting.
            /// </summary>
            private readonly Memory<T> memory;
            /// <summary>
            /// The number of subspans we'll concurrently sort.
            /// </summary>
            private readonly int subspanCount;
            /// <summary>
            /// The length of each subspan.
            /// </summary>
            private readonly int elementsPerSubspan;
            /// <summary>
            /// The start indices of each subspan (when merging).
            /// </summary>
            private readonly List<int> subspanStartOffsets;
            /// <summary>
            /// The ordered subspan indices during merging.
            /// </summary>
            private readonly List<int> subspanSortedIndices;

            internal ParallelSortHelper(Memory<T> memory, int minimumSubspanSize, int parallelism = -1)
            {
                if (memory.IsEmpty)
                {
                    throw new ArgumentOutOfRangeException("Memory must not be empty");
                }
                if (minimumSubspanSize <= 0)
                {
                    throw new ArgumentOutOfRangeException($"Minimum subspan size {minimumSubspanSize} must be greater than 0");
                }

                // Overridable for testing purposes
                if (parallelism == -1)
                {
                    parallelism = Environment.ProcessorCount;
                }

                this.minimumSubspanSize = minimumSubspanSize;
                this.memory = memory;

                // What subspan size do we want?
                int length = memory.Length;
                int minimumSizedSubspanCount = (int)Math.Ceiling((float)length / minimumSubspanSize);
                if (minimumSizedSubspanCount > parallelism)
                {
                    subspanCount = parallelism;
                }
                else
                {
                    subspanCount = minimumSizedSubspanCount;
                }

                elementsPerSubspan = (int)Math.Ceiling((float)memory.Length / subspanCount);

                // Depending on elementsPerSubspan, for small numbers we may have picked too many subspans. Decrease if necessary
                subspanCount = (int)Math.Ceiling((float)length / elementsPerSubspan);

                // Ensure there are not too many or too few subspans
                Contract.Assert(subspanCount * elementsPerSubspan >= length, "Not enough subspans to hold data");
                Contract.Assert(subspanCount * elementsPerSubspan <= length + elementsPerSubspan, "Too many subspans");
                
                // List of the start index in each subspan (e.g. how many elements of that subspan have been merged).
                subspanStartOffsets = new List<int>(subspanCount);
                // Sorted list of subspan indices, ordered by first item of each subspan.
                subspanSortedIndices = new List<int>(subspanCount);

                // initialize subspanIndexOrder
                for (int i = 0; i < subspanCount; i++)
                {
                    subspanStartOffsets.Add(0);
                    subspanSortedIndices.Add(i);
                }
            }

            internal void Sort(SpanSort<T> subspanSortDelegate, Comparison<T> comparison)
            {
                // Sort each subspan, in parallel.
                Parallel.For(0, subspanCount, i => subspanSortDelegate(GetSubspan(i)));

                // Now memory consists of N subspans, which are all sorted.
                // We want to perform an in-place merge sort over all of these subspans.
                // The optimal space algorithm is http://akira.ruc.dk/~keld/teaching/algoritmedesign_f04/Artikler/04/Huang88.pdf
                // but for our purposes we can afford to allocate a whole copy of the input, so we don't have to merge in place.
                Memory<T> output = new Memory<T>(new T[memory.Length]);

                // sort subspan indices in order of their first items
                subspanSortedIndices.Sort((i, j) => comparison(GetFirstSubspanElement(i), GetFirstSubspanElement(j)));

                // now walk through all of memory, merging into it as we go
                for (int i = 0; i < memory.Length; i++)
                {
                    // the first element of subspanSortedIndices is the next subspan to pick from
                    int firstSubspanIndex = subspanSortedIndices[0];
                    T firstSubspanElement = GetFirstSubspanElement(firstSubspanIndex);
                    output.Span[i] = firstSubspanElement;
                    subspanStartOffsets[firstSubspanIndex]++;

                    // Now firstSubspan may be out of order in the subspanSortedIndices list, or it may even be empty.
                    if (subspanStartOffsets[firstSubspanIndex] == GetSubspanLength(firstSubspanIndex))
                    {
                        // just remove it, that subspan's done
                        subspanSortedIndices.RemoveAt(0);
                    }
                    else
                    {
                        // There are more elements in firstSubspanIndex.
                        T firstSubspanNextElement = GetFirstSubspanElement(firstSubspanIndex);

                        // Find the next subspan with a first element that is bigger than firstSubspanNextElement,
                        // and move firstSubspanIndex to that location in subspanIndexOrder.
                        for (int j = 1; j <= subspanSortedIndices.Count; j++)
                        {
                            if (j == subspanSortedIndices.Count)
                            {
                                // we reached the end and all subspans had first elements that were smaller than firstSubspanElement.
                                // So, move firstSubspanIndex to the end, and we're done.
                                subspanSortedIndices.RemoveAt(0);
                                subspanSortedIndices.Add(firstSubspanIndex);
                            }
                            else
                            {
                                T nextSubspanFirstElement = GetFirstSubspanElement(subspanSortedIndices[j]);
                                if (comparison(firstSubspanNextElement, nextSubspanFirstElement) <= 0)
                                {
                                    // We found a subspan with a bigger first element.
                                    // Move firstSubspanIndex to be just before it.
                                    // (If j == 1 here, then we don't actually need to move firstSubspanIndex.)
                                    if (j > 1)
                                    {
                                        subspanSortedIndices.Insert(j, firstSubspanIndex);
                                        subspanSortedIndices.RemoveAt(0);
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    // invariant check: first element of first subspan must be equal to or greater than element we just added
                    if (i < memory.Length - 1)
                    {
                        // there should still be a first subspan
                        T firstSubspanElementAfterReordering = GetFirstSubspanElement(subspanSortedIndices[0]);
                        int order = comparison(firstSubspanElement, firstSubspanElementAfterReordering);
                        if (order > 0)
                        {
                            // oops, we have a bug
                            throw new Exception("Wrong order");
                        }
                    }
                }

                // now copy output back to memory
                output.Span.CopyTo(memory.Span);

                // and we're done!
            }

            internal Span<T> GetSubspan(int subspanIndex)
            {
                int startIndex = subspanIndex * elementsPerSubspan;
                int length = GetSubspanLength(subspanIndex);
                return memory.Span.Slice(startIndex, length);
            }

            internal int GetSubspanLength(int subspanIndex)
            {
                if (subspanIndex < subspanCount - 1)
                {
                    return elementsPerSubspan;
                }
                else
                {
                    return memory.Length - (elementsPerSubspan * (subspanCount - 1));
                }
            }

            internal T GetFirstSubspanElement(int subspanIndex)
            {
                return GetSubspan(subspanIndex)[subspanStartOffsets[subspanIndex]];
            }
        }
    }
}