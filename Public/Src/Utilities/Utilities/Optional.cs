// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents an optional value
    /// </summary>
    /// <typeparam name="T">the value type</typeparam>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct Optional<T>
    {
        /// <summary>
        /// Gets whether the optional value contains a valid value
        /// </summary>
        public readonly bool IsValid;

        /// <summary>
        /// Gets the invalid Optional value with no value set.
        /// </summary>
        public static Optional<T> Invalid => default(Optional<T>);

        private readonly T m_value;

        /// <summary>
        /// Gets the value
        /// </summary>
        public T Value
        {
            get
            {
                Contract.Requires(IsValid);
                return m_value;
            }
        }

        /// <summary>
        /// Creates a new valid optional value with the given value
        /// </summary>
        public Optional(T value)
        {
            IsValid = true;
            m_value = value;
        }

        /// <summary>
        /// Implicit conversion of value to valid Optional.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator Optional<T>(T value)
        {
            return new Optional<T>(value);
        }
    }
}
