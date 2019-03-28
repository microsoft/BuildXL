// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.FrontEnd.Script.Values;

namespace BuildXL.FrontEnd.Script.Ambients.Set
{
    /// <summary>
    /// A set element or a map key tagged with an insertion order.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1036:OverrideMethodsOnComparableTypes")]
    public readonly struct TaggedEntry : IEquatable<TaggedEntry>, IComparable<TaggedEntry>
    {
        /// <summary>
        /// Value.
        /// </summary>
        public EvaluationResult Value { get; }

        /// <summary>
        /// Tag.
        /// </summary>
        public long Tag { get; }

        /// <nodoc />
        public TaggedEntry(EvaluationResult value, long tag)
        {
            Value = value;
            Tag = tag;
        }

        /// <nodoc />
        public TaggedEntry(EvaluationResult value)
            : this(value, 0)
        {
        }

        /// <inheritdoc />
        /// <remarks>Equality is only on the value, and not on the tag.</remarks>
        public bool Equals(TaggedEntry other)
        {
            return Value.Equals(other.Value);
        }

        /// <inheritdoc />
        /// <remarks>Hash code is the hash code of the value, not a combination of value and tag.</remarks>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <inheritdoc />
        public int CompareTo(TaggedEntry other)
        {
            return Tag.CompareTo(other.Tag);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (!(obj is TaggedEntry))
            {
                return false;
            }

            return Equals((TaggedEntry)obj);
        }

        /// <summary>
        /// Equal operator.
        /// </summary>
        public static bool operator ==(TaggedEntry left, TaggedEntry right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Not-equal operator.
        /// </summary>
        public static bool operator !=(TaggedEntry left, TaggedEntry right)
        {
            return !(left == right);
        }
    }
}
