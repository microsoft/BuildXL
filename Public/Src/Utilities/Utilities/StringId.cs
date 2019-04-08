// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Identifies a unique string within a string table.
    /// </summary>
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public readonly struct StringId : IEquatable<StringId>
    {
        /// <summary>
        /// An invalid string.
        /// </summary>
        public static readonly StringId Invalid = new StringId(0);

        /// <summary>
        /// Identifier of this string as understood by the owning string table.
        /// </summary>
        public readonly int Value;

#if DebugStringTable
        /// <summary>
        /// A byte representing the parent StringTable id used for displaying this stringId under the debugger.
        /// </summary>
        internal readonly byte DebugStringTableOwnerId;
#endif

        /// <summary>
        /// Creates a string ID for some underlying integer value.
        /// </summary>
        /// <remarks>
        /// Since the value must have some meaning to a string table, this constructor should primarily be called by StringTables.
        /// The only other reasonable usage would be for temporary serialization (e.g. to a child process).
        /// </remarks>
        internal StringId(int value
        )
        {
            Value = value;
#if DebugStringTable
            DebugStringTableOwnerId = StringTable.UnallocatedDebugId;
#endif
        }

#if DebugStringTable
        /// <summary>
        /// Creates a string ID for some underlying integer value.
        /// </summary>
        /// <remarks>
        /// Since the value must have some meaning to a string table, this constructor should primarily be called by StringTables.
        /// The only other reasonable usage would be for temporary serialization (e.g. to a child process).
        /// </remarks>
        internal StringId(int value, byte debugStringTableOwnerId)
        {
            Value = value;
            DebugStringTableOwnerId = debugStringTableOwnerId;
        }
#endif
        
        /// <summary>
        /// Unsafe factory method that constructs <see cref="StringId"/> instance from the underlying integer value.
        /// </summary>
        /// <remarks>
        /// The method should be used only when <see cref="Create(StringTable, string)"/> is unsuitable, like for deserialization purposes or to avoid memory overhead.
        /// </remarks>
        public static StringId UnsafeCreateFrom(int value)
        {
            return new StringId(value);
        }

        /// <summary>
        /// Indicates if this string ID corresponds to a valid string table entry (i.e., is not <see cref="Invalid"/>).
        /// </summary>
        public bool IsValid => Value != 0;

        /// <summary>
        /// Indicates if this string ID and the one given represent the same underlying value. Note that it is only meaningful
        /// to compare string IDs generated from the same <see cref="StringTable" />, but that this condition is not enforced.
        /// </summary>
        public bool Equals(StringId other)
        {
            return other.Value == Value;
        }

        /// <summary>
        /// Indicates if a given object is a StringId equal to this one. See <see cref="Equals(BuildXL.Utilities.StringId)" />.
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

        /// <summary>
        /// Creates a new string ID.
        /// </summary>
        public static StringId Create(StringTable table, string value)
        {
            return table.AddString(value);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return this == Invalid ? "{Invalid}" : I($"{{String (id: 0x{Value:x})}}");
        }

        /// <summary>
        /// Converts the StringId to a string for logging
        /// </summary>
        [Pure]
        public string ToString(StringTable stringTable)
        {
            return stringTable.GetString(this);
        }

        /// <summary>
        /// Equality operator for two StringIds
        /// </summary>
        public static bool operator ==(StringId left, StringId right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two StringIds.
        /// </summary>
        public static bool operator !=(StringId left, StringId right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Returns a string to be displayed as the debugger representation of this value.
        /// This string contains the expanded string when possible. See the comments in StringTable.cs
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Nothing is private to the debugger.")]
        [ExcludeFromCodeCoverage]
        [Pure]
        public string ToDebuggerDisplay()
        {
#if DebugStringTable
            if (this == Invalid)
            {
                return ToString();
            }

            StringTable owner = StringTable.DebugTryGetTableByDebugId(DebugStringTableOwnerId);
            return owner == null
                ? "{Unable to expand StringId; this may occur after the allocation of a many StringTables}"
                : I($"{{String '{ToString(owner)}' (id: {Value:x})}}");
#else
            return ToString();
#endif

        }
    }
}
