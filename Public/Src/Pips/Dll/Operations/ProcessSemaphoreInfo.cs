// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Information about a semaphore that is acquired by a process
    /// </summary>
    public readonly struct ProcessSemaphoreInfo : IEquatable<ProcessSemaphoreInfo>
    {
        /// <summary>
        /// Name of the semaphore
        /// </summary>
        public readonly StringId Name;

        /// <summary>
        /// Number of units used up by this instance
        /// </summary>
        public readonly int Value;

        /// <summary>
        /// Default limit of the semaphore
        /// </summary>
        public readonly int Limit;

        /// <summary>
        /// Creates an instance of this structure.
        /// </summary>
        public ProcessSemaphoreInfo(StringId name, int value, int limit)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(value > 0);
            Contract.Requires(value <= limit);

            Name = name;
            Value = value;
            Limit = limit;
        }

        /// <summary>
        /// Whether this instance is valid.
        /// </summary>
        public bool IsValid => Name.IsValid;

        #region Serialziation
        internal void Serialize(PipWriter pipWriter)
        {
            Contract.Requires(pipWriter != null);

            pipWriter.Write(Name);
            pipWriter.WriteCompact(Value);
            pipWriter.WriteCompact(Limit);
        }

        internal static ProcessSemaphoreInfo Deserialize(PipReader reader)
        {
            Contract.Requires(reader != null);

            StringId name = reader.ReadStringId();
            int value = reader.ReadInt32Compact();
            int limit = reader.ReadInt32Compact();

            return new ProcessSemaphoreInfo(name, value, limit);
        }
        #endregion

        #region IEquatable<ProcessSemaphoreInfo> implementation

        /// <summary>
        /// Indicates if a given object is a ProcessSemaphoreInfo equal to this one. See
        /// <see cref="Equals(ProcessSemaphoreInfo)" />.
        /// </summary>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(ProcessSemaphoreInfo other)
        {
            return Name == other.Name && Value == other.Value && Limit == other.Limit;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Name.GetHashCode(), Value.GetHashCode());
        }

        /// <summary>
        /// Equality operator for two SemaphoreIncrements
        /// </summary>
        public static bool operator ==(ProcessSemaphoreInfo left, ProcessSemaphoreInfo right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two SemaphoreIncrements
        /// </summary>
        public static bool operator !=(ProcessSemaphoreInfo left, ProcessSemaphoreInfo right)
        {
            return !left.Equals(right);
        }
        #endregion
    }
}
