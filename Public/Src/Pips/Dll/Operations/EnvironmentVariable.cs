// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// An environment variable definition.
    /// </summary>
    public readonly struct EnvironmentVariable : IEquatable<EnvironmentVariable>
    {
        // This struct used to have the following structure:
        // readonly record struct EnvironmentVariable(StringId Name, PipData Value, bool IsPassThrough);
        // Unfortunately, due to layout issues, the old version was 48 bytes in size even though we can manually
        // pack all the required data into 32 bytes.

        // StringId
        private readonly int m_nameValue;

        // PipData
        private readonly bool m_pipDataAvailable;

        // PipData.EntriesBinarySegmentPointer
        private readonly int m_pipDataEntriesBinarySegmentPointer;
        // PipData.HeaderEntry
        private readonly PipDataEntryType m_pipDataEntryType;
        private readonly PipDataFragmentEscaping m_pipDataEscaping;
        private readonly int m_pipDataValue;
        // PipData.Entries
        private readonly PipDataEntryList m_pipDataEntries;

        /// <summary>
        /// Name of the variable.
        /// </summary>
        public StringId Name => StringId.UnsafeCreateFrom(m_nameValue);

        /// <summary>
        /// Value of the variable.
        /// </summary>
        public PipData Value
        {
            get
            {
                if (!m_pipDataAvailable)
                {
                    return PipData.Invalid;
                }

                return PipData.CreateInternal(
                    new PipDataEntry(m_pipDataEscaping, m_pipDataEntryType, m_pipDataValue),
                    m_pipDataEntries,
                    StringId.UnsafeCreateFrom(m_pipDataEntriesBinarySegmentPointer));
            }
        }

        /// <summary>
        /// Whether this is a pass-through environment variable
        /// </summary>
        public bool IsPassThrough { get; }
        
        /// <summary>
        /// Creates an environment variable definition.
        /// </summary>
        public EnvironmentVariable(StringId name, PipData value, bool isPassThrough = false)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(value.IsValid || isPassThrough);

            m_nameValue = name.Value;
            if (value.IsValid)
            {
                m_pipDataAvailable = true;
                m_pipDataEntriesBinarySegmentPointer = value.EntriesBinarySegmentPointer.Value;
                m_pipDataEntryType = value.HeaderEntry.EntryType;
                m_pipDataEscaping = value.HeaderEntry.RawEscaping;
                m_pipDataValue = value.HeaderEntry.RawData;
                m_pipDataEntries = value.Entries;
            }
            else
            {
                m_pipDataAvailable = false;
                m_pipDataEntriesBinarySegmentPointer = default;
                m_pipDataEntryType = default;
                m_pipDataEscaping = default;
                m_pipDataValue = default;
                m_pipDataEntries = default;
            }

            IsPassThrough = isPassThrough;
        }

        #region Serialization

        /// <nodoc />
        internal void Serialize(PipWriter writer)
        {
            Contract.Requires(writer != null);
            writer.Write(Name);
            if (Name.IsValid)
            {
                writer.Write(Value);
                writer.Write(IsPassThrough);
            }
        }

        /// <nodoc />
        internal static EnvironmentVariable Deserialize(PipReader reader)
        {
            Contract.Requires(reader != null);
            StringId name = reader.ReadStringId();
            if (name.IsValid)
            {
                PipData value = reader.ReadPipData();
                bool isPassThrough = reader.ReadBoolean();
                Contract.Assume(value.IsValid || isPassThrough);
                return new EnvironmentVariable(name, value, isPassThrough);
            }
            else
            {
                return default(EnvironmentVariable);
            }
        }
        #endregion

        #region IEquatable<EnvironmentVariable> implementation

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other" /> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        /// <filterpriority>2</filterpriority>
        public bool Equals(EnvironmentVariable other)
        {
            return Value == other.Value && Name == other.Name && IsPassThrough == other.IsPassThrough;
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <returns>
        /// true if the specified object  is equal to the current object; otherwise, false.
        /// </returns>
        /// <param name="obj">The object to compare with the current object. </param>
        /// <filterpriority>2</filterpriority>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Serves as a hash function for a particular type.
        /// </summary>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Value.GetHashCode(), Name.GetHashCode(), IsPassThrough.GetHashCode());
        }

        /// <summary>
        /// Indicates whether two object instances are equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare. </param>
        /// <param name="right">The second object to compare. </param>
        /// <filterpriority>3</filterpriority>
        public static bool operator ==(EnvironmentVariable left, EnvironmentVariable right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two objects instances are not equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are not equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <filterpriority>3</filterpriority>
        public static bool operator !=(EnvironmentVariable left, EnvironmentVariable right)
        {
            return !left.Equals(right);
        }
        #endregion
    }
}
