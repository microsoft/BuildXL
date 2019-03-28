// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Inclusive range of <see cref="NodeId"/>s.
    /// </summary>
    public readonly struct NodeRange : IEquatable<NodeRange>
    {
        /// <summary>
        /// First node in the range. Set to <see cref="NodeId.Invalid"/> if this range is empty.
        /// </summary>
        public readonly NodeId FromInclusive;

        /// <summary>
        /// Last node in the range. Set to <see cref="NodeId.Invalid"/> if this range is empty.
        /// </summary>
        public readonly NodeId ToInclusive;

        /// <summary>
        /// Creates a non-empty node range.
        /// </summary>
        public NodeRange(NodeId fromInclusive, NodeId toInclusive)
        {
            Contract.Requires(fromInclusive.IsValid && toInclusive.IsValid);
            Contract.Requires(fromInclusive.Value <= toInclusive.Value);

            FromInclusive = fromInclusive;
            ToInclusive = toInclusive;
        }

        /// <summary>
        /// Creates a range normally if <paramref name="fromInclusive"/> and <paramref name="toInclusive"/> form a nonempty range.
        /// If instead the range is non-representable ('from' greater than 'to'), an empty range is returned instead.
        /// </summary>
        public static NodeRange CreatePossiblyEmpty(NodeId fromInclusive, NodeId toInclusive)
        {
            Contract.Requires(fromInclusive.IsValid && toInclusive.IsValid);

            return fromInclusive.Value <= toInclusive.Value ? new NodeRange(fromInclusive, toInclusive) : Empty;
        }

        /// <summary>
        /// Creates a range which includes all possible nodes with a value at least as large as <paramref name="lowerBound"/>.
        /// </summary>
        public static NodeRange CreateLowerBound(NodeId lowerBound)
        {
            Contract.Requires(lowerBound.IsValid);

            return new NodeRange(lowerBound, NodeId.Max);
        }

        /// <summary>
        /// Creates a range which includes all possible nodes with a value no larger than <paramref name="upperBound"/>.
        /// </summary>
        public static NodeRange CreateUpperBound(NodeId upperBound)
        {
            Contract.Requires(upperBound.IsValid);

            return new NodeRange(NodeId.Min, upperBound);
        }

        /// <summary>
        /// Indicates if this range contains the specified node.
        /// </summary>
        [Pure]
        public bool Contains(NodeId node)
        {
            Contract.Requires(node.IsValid);

            return node.Value <= ToInclusive.Value && node.Value >= FromInclusive.Value;
        }

        /// <summary>
        /// Empty range.
        /// </summary>
        public static NodeRange Empty => default(NodeRange);

        /// <summary>
        /// Indicates if this range is equivalent <see cref="Empty"/>.
        /// </summary>
        public bool IsEmpty => !FromInclusive.IsValid;

        /// <summary>
        /// Number of nodes represented by this range.
        /// </summary>
        public int Size => IsEmpty ? 0 : (int)(ToInclusive.Value - FromInclusive.Value + 1);

        /// <inheritdoc />
        public bool Equals(NodeRange other)
        {
            return ToInclusive == other.ToInclusive && FromInclusive == other.FromInclusive;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(FromInclusive.GetHashCode(), ToInclusive.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(NodeRange left, NodeRange right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(NodeRange left, NodeRange right)
        {
            return !left.Equals(right);
        }
    }
}
