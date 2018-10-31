// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Identifies a unique module within a string table.
    /// </summary>
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public readonly struct ModuleId : IEquatable<ModuleId>
    {
        /// <summary>
        /// An invalid string.
        /// </summary>
        public static readonly ModuleId Invalid = new ModuleId(-1);

        /// <summary>
        /// Identifier of this string as understood by the owning string table.
        /// </summary>
        public readonly int Value;

#if DEBUG
        /// <summary>
        /// Friendly name only to be used for debugging.
        /// </summary>
        private readonly string m_friendlyNameForDebugging;
#endif

        /// <summary>
        /// Creates a string ID for some underlying integer value.
        /// </summary>
        /// <remarks>
        /// Since the value must have some meaning to a string table, this constructor should primarily be called by StringTables.
        /// The only other reasonable usage would be for temporary serialization (e.g. to a child process).
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1801")]
        public ModuleId(int value, string friendlyNameForDebugging = null)
        {
            Value = value;
#if DEBUG
            m_friendlyNameForDebugging = friendlyNameForDebugging;
#endif
        }

        /// <summary>
        /// Indicates if this module ID corresponds to a valid module table entry (i.e., is not <see cref="Invalid" />).
        /// </summary>
        public bool IsValid => Value != Invalid.Value;

        /// <summary>
        /// Indicates if this module ID and the one given represent the same underlying value. Note that it is only meaningful
        /// to compare module IDs generated from the same ModuleTable, but that that condition is not enforced.
        /// </summary>
        public bool Equals(ModuleId other)
        {
            return other.Value == Value;
        }

        /// <summary>
        /// Indicates if a given object is a ModuleId equal to this one. See <see cref="Equals(ModuleId)" />.
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
            return this == Invalid ? "{Invalid}" : I($"{{Module (id: {Value:x})}}");
        }

        /// <summary>
        /// Equality operator for two ModuleIds
        /// </summary>
        public static bool operator ==(ModuleId left, ModuleId right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two ModuleIds
        /// </summary>
        public static bool operator !=(ModuleId left, ModuleId right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Returns a string to be displayed as the debugger representation of this value.
        /// This string contains an expanded path when possible. See the comments in PathTable.cs
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode",
            Justification = "Nothing is private to the debugger.")]
        [ExcludeFromCodeCoverage]
        private string ToDebuggerDisplay()
        {
#if DEBUG
            return this == Invalid ? ToString() : I($"{{Module '{m_friendlyNameForDebugging}' (id: {Value:x})}}");

#else
            return ToString();
#endif
        }
    }
}
