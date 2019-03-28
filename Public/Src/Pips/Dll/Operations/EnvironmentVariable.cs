// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// An environment variable definition.
    /// </summary>
    public readonly struct EnvironmentVariable : IEquatable<EnvironmentVariable>
    {
        /// <summary>
        /// Name of the variable.
        /// </summary>
        public readonly StringId Name;

        /// <summary>
        /// Value of the variable.
        /// </summary>
        public readonly PipData Value;

        /// <summary>
        /// Whether this is a pass-through environment variable
        /// </summary>
        public readonly bool IsPassThrough;

        /// <summary>
        /// Creates an environment variable definition.
        /// </summary>
        public EnvironmentVariable(StringId name, PipData value, bool isPassThrough = false)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(value.IsValid ^ isPassThrough);

            Name = name;
            Value = value;
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
                Contract.Assume(value.IsValid ^ isPassThrough);
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
            return Value == other.Value && Name == other.Name;
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
            return HashCodeHelper.Combine(Value.GetHashCode(), Name.GetHashCode());
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
