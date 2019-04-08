// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Special <see cref="Dictionary{TKey,TValue}"/> that mimics Map{T} from TypeScript.
    /// </summary>
    public class Map<T> : Dictionary<string, T> { }

    /// <nodoc />
    public class ConcurrentMap<T> : ConcurrentDictionary<string, T> { }

    /// <summary>
    /// Helper factory class for creating <see cref="Optional{T}"/> instances.
    /// </summary>
    public static class Optional
    {
        /// <summary>
        /// Creates a new instance of <see cref="Optional{T}"/> type.
        /// </summary>
        public static Optional<T> Create<T>(T value)
        {
            return value;
        }

        /// <summary>
        /// Create an undefined Optional{T} type (effectively a default wrapper around empty value).
        /// </summary>
        public static Optional<T> Undefined<T>()
        {
            return default(Optional<T>);
        }
    }

    /// <summary>
    /// Monadic optional type that mimics optional argument in the TypeScript language.
    /// </summary>
    public readonly struct Optional<T> : IEquatable<Optional<T>>
    {
        private readonly T m_value;
        private readonly bool m_hasValue;

        /// <nodoc/>
        public Optional(T value)
            : this()
        {
            m_value = value;
            m_hasValue = m_value != null;
        }

        /// <nodoc/>
        public bool HasValue => m_hasValue;

        /// <nodoc/>
        public T Value => m_value;

        /// <nodoc/>
        public T ValueOrDefault => m_hasValue ? m_value : default(T);

        /// <summary>
        /// Implicit operator for converting any values to <see cref="Optional{T}"/>.
        /// </summary>
        /// <remarks>
        /// If the value is null than HasValue return false for the newly created instance.
        /// Note, that this method is not 100% bulett proof for value types.
        /// </remarks>
        public static implicit operator Optional<T>(T value)
        {
            if ((default(T) == null) && (value == null))
            {
                return default(Optional<T>);
            }

            return new Optional<T>(value);
        }

        /// <summary>
        /// Implicit convertion to bool that simplifies migration from TypeScript code to C# because will allow to use
        /// optioanl instances in the boolean context.
        /// </summary>
        public static implicit operator bool(Optional<T> v)
        {
            return v.HasValue;
        }

        /// <summary>
        /// Implicit conversion from optional value to underlying value.
        /// </summary>
        public static implicit operator T(Optional<T> v)
        {
            return v.ValueOrDefault;
        }

        /// <nodoc/>
        public static bool operator ==(Optional<T> u, Optional<T> v)
        {
            return u.Equals(v);
        }

        /// <nodoc/>
        public static bool operator !=(Optional<T> u, Optional<T> v)
        {
            return !u.Equals(v);
        }

        /// <nodoc/>
        public bool Equals(Optional<T> other)
        {
            return (!HasValue && !other.HasValue) || (HasValue && other.HasValue && Value.Equals(other.Value));
        }

        /// <inheritdoc/>
        public override bool Equals(object other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            return GetType() == other.GetType() && Equals((Optional<T>)other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return !HasValue ? 0 : Value.GetHashCode();
        }
    }
}
