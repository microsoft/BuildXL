// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Identifies a unique entry within a hierarchical name table.
    /// </summary>
    public readonly struct HierarchicalNameId : IEquatable<HierarchicalNameId>
    {
        /// <summary>
        /// An invalid entry.
        /// </summary>
        public static readonly HierarchicalNameId Invalid = new HierarchicalNameId(0);

        /// <summary>
        /// Identifier of this entry as understood by the owning name table.
        /// </summary>
        /// <remarks>
        /// Name IDs are a single integer in memory. However, we wrap these integers in a struct to get a new type identity
        /// and the ability to customize the debugger representation.
        /// </remarks>
        public readonly int Value;

        /// <summary>
        /// Returns the index in the <see cref="HierarchicalNameTable"/> of the <see cref="HierarchicalNameId"/>
        /// </summary>
        public int Index => HierarchicalNameTable.GetIndexFromValue(Value);

        /// <summary>
        /// Creates a name ID for some underlying integer value.
        /// </summary>
        /// <remarks>
        /// Since the value must have some meaning to a name table, this constructor should primarily be called by HierarchicalNameTable.
        /// The only other reasonable usage would be for temporary serialization (e.g. to a child process).
        /// </remarks>
        public HierarchicalNameId(int value)
        {
            Value = value;
        }

        /// <summary>
        /// Indicates if this name ID and the one given represent the same underlying value.
        /// </summary>
        /// <remarks>
        /// Note that it is only meaningful
        /// to compare name IDs generated from the same <see cref="HierarchicalNameTable" />, but that condition is not enforced.
        /// </remarks>
        public bool Equals(HierarchicalNameId other)
        {
            return other.Value == Value;
        }

        /// <summary>
        /// Indicates if a given object is a HierarchicalNameId equal to this one. See <see cref="Equals(BuildXL.Utilities.HierarchicalNameId)" />.
        /// </summary>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Value;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return this == Invalid ? "{Invalid}" : I($"{{Name (id: {Value:x})}}");
        }

        /// <summary>
        /// Equality operator for two HierarchicalNameIds
        /// </summary>
        public static bool operator ==(HierarchicalNameId left, HierarchicalNameId right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two HierarchicalNameIds
        /// </summary>
        public static bool operator !=(HierarchicalNameId left, HierarchicalNameId right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Determines whether a name id is valid or not.
        /// </summary>
        [Pure]
        public bool IsValid => this != Invalid;
    }
}
