// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Represents a mutable set of <see cref="NodeId"/>s in a given range.
    /// </summary>
    /// <remarks>
    /// Since node IDs are simply integers under the covers, this implementation ideally spends 1 bit per node possibly in the range
    /// (an eight million node set costs about one SI megabyte).
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public sealed class RangedNodeSet : IEnumerable<NodeId>
    {
        private readonly BitSet m_members;

        /// <nodoc />
        public RangedNodeSet()
        {
            m_members = new BitSet();
        }

        /// <summary>
        /// Create a new RangedNodeSet by using one
        /// </summary>
        public RangedNodeSet Clone()
        {
            return new RangedNodeSet(Range, m_members.Clone());
        }

        /// <summary>
        /// Deserialization constructor.
        /// </summary>
        private RangedNodeSet(NodeRange range, BitSet members)
        {
            Contract.Requires(members != null);
            Contract.Assume(members.Length == BitSet.RoundToValidBitCount(range.Size));
            m_members = members;
            Range = range;
        }

        /// <summary>
        /// Inclusive range of nodes possibly contained in this set.
        /// </summary>
        public NodeRange Range { get; private set; }

        /// <summary>
        /// Removes all existing nodes from this set, while simultaneously setting a new <see cref="Range"/>.
        /// </summary>
        public void ClearAndSetRange(NodeRange newRange)
        {
            if (newRange.Size > Range.Size)
            {
                m_members.Clear();
                m_members.SetLength(BitSet.RoundToValidBitCount(newRange.Size));
            }
            else
            {
                m_members.SetLength(BitSet.RoundToValidBitCount(newRange.Size));
                m_members.Clear();
            }

            Range = newRange;
        }

        /// <summary>
        /// Removes all existing nodes from this set, and clears the <see cref="Range"/> to be empty
        /// (this restores the set to its initial state, but leaves capacity reserved).
        /// </summary>
        public void Clear()
        {
            ClearAndSetRange(NodeRange.Empty);
        }

        /// <summary>
        /// Adds all nodes in the current range.
        /// </summary>
        public void Fill()
        {
            m_members.Fill(Range.Size);
        }

        /// <summary>
        /// Removes all existing nodes from this set, sets the <see cref="Range"/> to only contain <paramref name="node"/>,
        /// and then adds it to the set (the set is the fully populated).
        /// </summary>
        public void SetSingular(NodeId node)
        {
           ClearAndSetRange(new NodeRange(node, node));
           Add(node);
        }

        /// <summary>
        /// Adds a node to this set. It must be within <see cref="Range"/>.
        /// </summary>
        public void Add(NodeId node)
        {
            Contract.Requires(Range.Contains(node));

            m_members.Add((int)(node.Value - Range.FromInclusive.Value));
        }

        /// <summary>
        /// Adds a node to this set. It must be within <see cref="Range"/>.
        /// This is an atomic operation.
        /// </summary>
        public void AddAtomic(NodeId node)
        {
            Contract.Requires(Range.Contains(node));

            m_members.AddAtomic((int)(node.Value - Range.FromInclusive.Value));
        }

        /// <summary>
        /// Removes a node from this set, if present. It must be within <see cref="Range"/>.
        /// </summary>
        public void Remove(NodeId node)
        {
            Contract.Requires(Range.Contains(node));

            m_members.Remove((int)(node.Value - Range.FromInclusive.Value));
        }

        /// <summary>
        /// Indicates if this set contains the given node. If the given node is not in <see cref="Range"/>,
        /// then <c>false</c> will definitely be returned.
        /// </summary>
        [Pure]
        public bool Contains(NodeId node)
        {
            if (!Range.Contains(node))
            {
                return false;
            }

            return m_members.Contains((int)(node.Value - Range.FromInclusive.Value));
        }

        /// <summary>
        /// Returns an enumerator for the integer entries in this set.
        /// </summary>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(m_members.GetEnumerator(), Range.FromInclusive.Value);
        }

        IEnumerator<NodeId> IEnumerable<NodeId>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #region Serialization

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            if (Range.IsEmpty)
            {
                writer.Write(false);
            }
            else
            {
                writer.Write(true);
                writer.Write(Range.FromInclusive.Value);
                writer.Write(Range.ToInclusive.Value);
            }

            m_members.Serialize(writer);
        }

        /// <nodoc />
        public static RangedNodeSet Deserialize(BuildXLReader reader)
        {
            NodeRange range;
            if (!reader.ReadBoolean())
            {
                range = NodeRange.Empty;
            }
            else
            {
                uint fromValue = reader.ReadUInt32();
                uint toValue = reader.ReadUInt32();
                Contract.Assume(fromValue != 0 && toValue != 0);
                range = new NodeRange(new NodeId(fromValue), new NodeId(toValue));
            }

            BitSet members = BitSet.Deserialize(reader);
            return new RangedNodeSet(range, members);
        }

        #endregion

        /// <summary>
        /// Enumerator for the nodes in a <see cref="RangedNodeSet" />.
        /// </summary>
        public struct Enumerator : IEnumerator<NodeId>
        {
            private BitSet.Enumerator m_innerEnumerator;
            private readonly uint m_offset;

            internal Enumerator(BitSet.Enumerator innerEnumerator, uint offset)
            {
                m_innerEnumerator = innerEnumerator;
                m_offset = offset;
            }

            /// <inheritdoc />
            public NodeId Current => new NodeId((uint)m_innerEnumerator.Current + m_offset);

            /// <inheritdoc />
            public void Dispose()
            {
                m_innerEnumerator.Dispose();
            }

            object IEnumerator.Current => Current;

            /// <inheritdoc />
            public bool MoveNext()
            {
                return m_innerEnumerator.MoveNext();
            }

            /// <inheritdoc />
            public void Reset()
            {
                m_innerEnumerator.Reset();
            }
        }
    }
}
