// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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
        /// Whether the Optional contains a value
        /// </summary>
        public readonly bool HasValue;

        /// <summary>
        /// The empty Optional value, with no value set.
        /// </summary>
        public static readonly Optional<T> Empty = default;

        private readonly T m_value;

        /// <summary>
        /// Gets the value
        /// </summary>
        public T Value
        {
            get
            {
                Contract.Requires(HasValue);
                return m_value;
            }
        }

        /// <summary>
        /// Creates a new Optional holding the given value
        /// </summary>
        public Optional(T value)
        {
            HasValue = true;
            m_value = value;
        }

        /// <summary>
        /// Gets the value if available. Otherwise, returns false.
        /// </summary>
        public bool TryGetValue(out T value)
        {
            value = m_value;
            return HasValue;
        }

        /// <summary>
        /// Returns the value if this Optional has a value, or the specified alternative if this is the empty Optional
        /// </summary>
        public T ValueOrElse(T alternativeValue)
        {
            return HasValue ? m_value : alternativeValue;
        }

        /// <summary>
        /// Returns the value if this Optional has a value, or calls the producer if this is the empty Optional
        /// </summary>
        /// <remarks>The producer of the alternative value is only called if this Optional is empty</remarks>
        public T ValueOrElse(Func<T> alternativeProducer)
        {
            return HasValue ? m_value : alternativeProducer();
        }

        /// <summary>
        /// Returns a new Optional applying the binder if this Optional has a value; 
        /// otherwise returns the empty Optional.
        /// </summary>
        public Optional<T2> Then<T2>(Func<T, Optional<T2>> binder)
        {
            Contract.RequiresNotNull(binder);
            return HasValue ? binder(m_value) : Optional<T2>.Empty;
        }

        /// <summary>
        /// Returns a new Optional applying the specified function to the value if this Optional has a value; 
        /// otherwise returns the empty Optional.
        /// </summary>
        public Optional<T2> Then<T2>(Func<T, T2> then)
        {
            Contract.RequiresNotNull(then);
            return HasValue ? new Optional<T2>(then(m_value)) : Optional<T2>.Empty;
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
