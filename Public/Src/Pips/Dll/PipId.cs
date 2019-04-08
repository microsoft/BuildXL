// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Utilities;

namespace BuildXL.Pips
{
    /// <summary>
    /// Pip identifier
    /// </summary>
    public readonly struct PipId : IEquatable<PipId>
    {
        internal const int ValueBits = 27;

        // 4 bits reserved to allow packing the id with PipType
        // leaves 1 bit (the highest) for the light edge

        private const uint MaxValue = (1U << ValueBits) - 1;

        /// <summary>
        /// The invalid pip identifier
        /// </summary>
        public static readonly PipId Invalid = default(PipId);

        /// <summary>
        /// HashSourceFilePip value defined as the max valid value.
        /// </summary>
        public static PipId DummyHashSourceFilePipId = new PipId(MaxValue);

        private readonly uint m_encoded;

        /// <summary>
        /// Whether this pip identifier is valid
        /// </summary>
        public bool IsValid => m_encoded != 0;

        /// <summary>
        /// Unique identifier for this pip (does not encode type).
        /// </summary>
        public uint Value => m_encoded & MaxValue;

        /// <summary>
        /// Create a new PipId
        /// </summary>
        public PipId(uint value)
        {
            Contract.Requires(value > 0);
            Contract.Requires(value <= MaxValue);
            m_encoded = value;
        }

        /// <summary>
        /// Indicates if this Pip ID and the one given represent the same underlying value. Note that it is only meaningful
        /// to compare Pip IDs generated from the same <see cref="PipTable" />, but that that condition is not enforced.
        /// </summary>
        public bool Equals(PipId other)
        {
            return other.m_encoded == m_encoded;
        }

        /// <summary>
        /// Indicates if a given object is a PipId equal to this one. See <see cref="Equals(BuildXL.Pips.PipId)" />.
        /// </summary>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (int)m_encoded;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (this == Invalid)
            {
                return "{Invalid}";
            }

            return string.Format(CultureInfo.InvariantCulture, "{{Pip (id: {0:x})}}", Value);
        }

        /// <summary>
        /// Equality operator for two PipIds
        /// </summary>
        public static bool operator ==(PipId left, PipId right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two PipIds
        /// </summary>
        public static bool operator !=(PipId left, PipId right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Helper to serialize a PipId
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(Value);
        }

        /// <summary>
        /// Helper to deserialize PipId's
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static PipId Deserialize(BuildXLReader reader)
        {
            var value = reader.ReadUInt32();
            return value == 0 ? PipId.Invalid : new PipId(value);
        }
    }
}
