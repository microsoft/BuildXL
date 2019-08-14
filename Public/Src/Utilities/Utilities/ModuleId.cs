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
    /// Identifies a unique module within a string table.
    /// </summary>
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public readonly struct ModuleId : IEquatable<ModuleId>
    {
        /// <summary>
        /// An invalid string.
        /// </summary>
        public static readonly ModuleId Invalid = new ModuleId(StringId.Invalid);

        /// <summary>
        /// Identifier of this string as understood by the owning string table.
        /// </summary>
        public readonly StringId Value;

        /// <summary>
        /// Creates a string ID for some underlying integer value.
        /// </summary>
        /// <remarks>
        /// Since the value must have some meaning to a string table, this constructor should primarily be called by StringTables.
        /// The only other reasonable usage would be for temporary serialization (e.g. to a child process).
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1801")]
        private ModuleId(StringId value, string friendlyNameForDebugging = null)
        {
            Analysis.IgnoreArgument(friendlyNameForDebugging);
            Value = value;
        }

        /// <nodoc />
        public static ModuleId Create(StringTable table, string identity, string version = null, string friendlyNameForDebugging = null) => Create(StringId.Create(table, identity), StringId.Invalid, friendlyNameForDebugging);

        /// <nodoc />
        public static ModuleId Create(StringId identity, StringId version = default, string friendlyNameForDebugging = null)
        {
            Contract.Requires(identity.IsValid);
            return new ModuleId(identity, friendlyNameForDebugging);
        }

        /// <nodoc />
        public static ModuleId UnsafeCreate(int stringIdValue, string friendlyNameForDebugging = null)
        {
            return new ModuleId(new StringId(stringIdValue), friendlyNameForDebugging);
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
            return Value.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return this == Invalid ? "{Invalid}" : I($"{{Module (id: {Value})}}");
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
        /// Deserializes <see cref="ModuleId"/>.
        /// </summary>
        public static ModuleId Deserialize(BuildXLReader reader) => new ModuleId(reader.ReadStringId());

        /// <summary>
        /// Serialzies this instance.
        /// </summary>
        public void Serialize(BuildXLWriter writer) => writer.Write(Value);

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
            return this == Invalid ? ToString() : I($"{{Module '{Value.ToDebuggerDisplay()}'}}");

#else
            return ToString();
#endif
        }
    }
}
