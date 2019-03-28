// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache.Serialization
{
    /// <summary>
    /// A class used to diff two ordered lists.
    /// A "diff" is considered a set of add or remove operations applied to
    /// the first list to transform it the second list.
    /// </summary>
    public class ChangeList<T>
    {
        /// <summary>
        /// Represents the type of change operations that can be
        /// used by this class when diffing two lists.
        /// </summary>
        public enum ChangeType
        {
            /// <summary>
            /// Default.
            /// </summary>
            None,

            /// <summary>
            /// The element was removed.
            /// </summary>
            Removed,

            /// <summary>
            /// The element was added.
            /// </summary>
            Added
        }

        /// <summary>
        /// Represents an element in a <see cref="ChangeList{T}"/>.
        /// </summary>
        public struct ChangeListValue
        {
            /// <summary>
            /// Depending on the <see cref="ChangeType"/>, a value from either <see cref="OldList"/> or <see cref="NewList"/>.
            /// </summary>
            public T Value;

            /// <summary>
            /// The type of change that was applied to the element.
            /// </summary>
            public ChangeType ChangeType;

            /// <summary>
            /// Constructor.
            /// </summary>
            public ChangeListValue(T value, ChangeType changeType)
            {
                Value = value;
                ChangeType = changeType;
            }

            /// <inheritdoc />
            public override string ToString()
            {
                var changeSymbol = ChangeType == ChangeType.Removed ? "-" : "+";
                return string.Format("\t{0} {1}", changeSymbol, Value.ToString());
            }
        }

        /// <summary>
        /// When computing <see cref="Changes"/>, the
        /// original list that was transformed to <see cref="NewList"/>.
        /// </summary>
        public IReadOnlyList<T> OldList { get; private set; }

        /// <summary>
        /// When computing <see cref="Changes"/>, the
        /// resulting list that was transformed from <see cref="OldList"/>.
        /// </summary>
        public IReadOnlyList<T> NewList { get; private set; }

        /// <summary>
        /// A list of changes that could have been made to transform 
        /// <see cref="OldList"/> to <see cref="NewList"/>.
        /// This is the underlying list for the <see cref="ChangeList{T}"/>.
        /// </summary>
        private List<ChangeListValue> Changes { get; set; }

        /// <summary>
        /// The number of changes in the change list.
        /// </summary>
        public int Count => Changes.Count;

        /// <summary>
        /// Constructor.
        /// </summary>
        public ChangeList(IReadOnlyList<T> oldList, IReadOnlyList<T> newList)
        {
            OldList = oldList;
            NewList = newList;
            Changes = new List<ChangeListValue>();

            DiffLists();
        }

        /// <summary>
        /// Adds an element that was removed from
        /// <see cref="OldList"/> to the <see cref="ChangeList{T}"/>.
        /// </summary>
        /// <param name="element"></param>
        private void AddRemoved(T element)
        {
            Changes.Add(new ChangeListValue
            {
                Value = element,
                ChangeType = ChangeType.Removed
            });
        }

        /// <summary>
        /// Adds an element that was added to
        /// <see cref="NewList"/> to the <see cref="ChangeList{T}"/>.
        /// </summary>
        /// <param name="element"></param>
        private void AddAdded(T element)
        {
            Changes.Add(new ChangeListValue
            {
                Value = element,
                ChangeType = ChangeType.Added
            });
        }

        /// <summary>
        /// Computes the longest common subsequence of two lists.
        /// A subsequence is a sequence that appears in the same relative order, 
        /// but not necessarily contiguously.
        /// </summary>
        /// <note>
        /// This function implements the dynamic programming solution to the longest common 
        /// subsequence problem. Since the problem definition and algorithm are well documented 
        /// publicly, full details are not provided here.
        /// </note>
        /// <returns>
        /// The longest common subsequence represented by their indices in <see cref="OldList"/>
        /// (to prevent copying the elements).
        /// </returns>
        private List<int> ComputeLongestCommonSubsequence()
        {
            // Produces a memoization matrix of size [old list size + 1][new list size + 1].
            // The index [r, c] represents the size of the longest common subsequence between
            // the sub-lists OldList[0, r - 1] and NewList[0, c - 1].
            var lcs_Matrix = new int[OldList.Count + 1][];

            for (int i = 0; i < OldList.Count + 1; ++i)
            {
                lcs_Matrix[i] = new int[NewList.Count + 1];
            }

            for (int r = 1; r < OldList.Count + 1; ++r)
            {
                for (int c = 1; c < NewList.Count + 1; ++c)
                {
                    if (OldList[r - 1].Equals(NewList[c - 1]))
                    {
                        lcs_Matrix[r][c] = lcs_Matrix[r - 1][c - 1] + 1;
                    }
                    else
                    {
                        lcs_Matrix[r][c] = System.Math.Max(lcs_Matrix[r - 1][c], lcs_Matrix[r][c - 1]);
                    }
                }
            }

            // Traversing the matrix according to the algorithm produces the longest common subsequence
            // in reverse order. Add to the front of a linked list to reverse the order back.
            var longestCommonSubsequence = new LinkedList<int>();

            for (int r = OldList.Count, c = NewList.Count; r != 0 && c != 0;)
            {
                if (OldList[r - 1].Equals(NewList[c - 1]))
                {
                    longestCommonSubsequence.AddFirst(r - 1);

                    r--;
                    c--;
                }
                else
                {
                    if (lcs_Matrix[r - 1][c] > lcs_Matrix[r][c - 1])
                    {
                        r--;
                    }
                    else
                    {
                        c--;
                    }
                }
            }

            return new List<int>(longestCommonSubsequence);
        }

        /// <summary>
        /// Diffs two ordered (but not necessarily sorted) lists.
        /// </summary>
        private void DiffLists()
        {
            // The longest common subsequence is the maximum amount of elements shared
            // between both lists. Since shared elements are not included in the diff, 
            // maximizing the number of shared elements minimizes the amount of elements diffed.
            var lcs = ComputeLongestCommonSubsequence();

            int oldIdx = 0;
            int newIdx = 0;
            for (int i = 0; i < lcs.Count; ++i, ++oldIdx, ++newIdx)
            {
                // To prevent copying the elements over to the subsequence,
                // subsequence elements are stored as their corresponding indices
                // in OldList
                var commonElement = OldList[lcs[i]];

                // Any element that is in the old list, but not the longest common subsequence
                // must have been removed to get to the new list
                while (!OldList[oldIdx].Equals(commonElement))
                {
                    AddRemoved(OldList[oldIdx]);
                    oldIdx++;
                }

                // Any element that is the new list, but not the longest common subsequence
                // must have been added to get to the new list
                while (!NewList[newIdx].Equals(commonElement))
                {
                    AddAdded(NewList[newIdx]);
                    newIdx++;
                }
            }

            // Handle trailing elements after the last element in the longest common subsequence
            while (oldIdx < OldList.Count)
            {
                AddRemoved(OldList[oldIdx]);
                ++oldIdx;
            }

            while (newIdx < NewList.Count)
            {
                AddAdded(NewList[newIdx]);
                ++newIdx;
            }
        }

        /// <summary>
        /// Index into the change list.
        /// </summary>
        public ChangeListValue this[int i]
        {
            get
            {
                return Changes[i];
            }
        }

        /// <inheritdoc />
        public override string ToString() => ToString(string.Empty);

        /// <summary>
        /// Converts a <see cref="ChangeList{T}"/> to a string.
        /// </summary>
        /// <param name="prefix">
        /// A prefix string to append to each value.
        /// </param>
        public string ToString(string prefix)
        {
            using (var pool = Pools.GetStringBuilder())
            {
                var sb = pool.Instance;
                foreach (var change in Changes)
                {
                    sb.AppendLine(prefix + change.ToString());
                }

                return sb.ToString();
            }
        }
    }
}
