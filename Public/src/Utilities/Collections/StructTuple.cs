// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Reflection;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Constructor for struct tuples (value-type variants of <see cref="Tuple" />).
    /// </summary>
    /// <remarks>
    /// These trivial things forward to constructors so we get inferencing for the type parameters.
    /// </remarks>
    public static class StructTuple
    {
        /// <summary>
        /// Tuple constructor.
        /// </summary>
        public static StructTuple<TItem1, TItem2> Create<TItem1, TItem2>(TItem1 item1, TItem2 item2)
        {
            return new StructTuple<TItem1, TItem2>(item1, item2);
        }

        /// <summary>
        /// Tuple constructor.
        /// </summary>
        public static StructTuple<TItem1, TItem2, TItem3> Create<TItem1, TItem2, TItem3>(TItem1 item1, TItem2 item2, TItem3 item3)
        {
            return new StructTuple<TItem1, TItem2, TItem3>(item1, item2, item3);
        }

        /// <summary>
        /// Tuple constructor.
        /// </summary>
        public static StructTuple<TItem1, TItem2, TItem3, TItem4> Create<TItem1, TItem2, TItem3, TItem4>(
            TItem1 item1,
            TItem2 item2,
            TItem3 item3,
            TItem4 item4)
        {
            return new StructTuple<TItem1, TItem2, TItem3, TItem4>(item1, item2, item3, item4);
        }

        // From System.Web.Util.HashCodeCombiner
        internal static int CombineHashCodes(int h1, int h2)
        {
            unchecked
            {
                return ((h1 << 5) + h1) ^ h2;
            }
        }

        internal static int CombineHashCodes(int h1, int h2, int h3)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2), h3);
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2), CombineHashCodes(h3, h4));
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2, h3, h4), h5);
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5, int h6)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2, h3, h4), CombineHashCodes(h5, h6));
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5, int h6, int h7)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2, h3, h4), CombineHashCodes(h5, h6, h7));
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5, int h6, int h7, int h8)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2, h3, h4), CombineHashCodes(h5, h6, h7, h8));
        }
    }

    /// <summary>
    /// Constructor for <see cref="EquatableClass{T}" />.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public static class EquatableClass
    {
        /// <summary>
        /// Constructor for <see cref="EquatableClass{T}" />.
        /// </summary>
        public static EquatableClass<T> Create<T>(T value) where T : class
        {
            return new EquatableClass<T>(value);
        }
    }

    /// <summary>
    /// Adapting wrapper from a reference type to <see cref="IEquatable{T}" />
    /// </summary>
    /// <remarks>
    /// Equation is performed without boxing since ref types can't be further boxed.
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public readonly struct EquatableClass<T> : IEquatable<EquatableClass<T>>
        where T : class
    {
        private readonly T m_value;

        /// <summary>
        /// Creates an equatable wrapper for the given value.
        /// </summary>
        public EquatableClass(T value)
        {
            m_value = value;
        }

        /// <summary>
        /// Gets the wrapped value.
        /// </summary>
        public T Value => m_value;

        /// <inheritdoc />
        public bool Equals(EquatableClass<T> other)
        {
            return EqualityComparer<T>.Default.Equals(m_value, other.m_value);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return m_value == null ? 0 : m_value.GetHashCode();
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator T(EquatableClass<T> me)
        {
            return me.m_value;
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator EquatableClass<T>(T unwrapped)
        {
            return new EquatableClass<T>(unwrapped);
        }

        /// <nodoc />
        public static bool operator ==(EquatableClass<T> left, EquatableClass<T> right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(EquatableClass<T> left, EquatableClass<T> right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Constructor for <see cref="EquatableEnum{T}" />.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public static class EquatableEnum
    {
        /// <summary>
        /// Constructor for <see cref="EquatableEnum{T}" />.
        /// </summary>
        public static EquatableEnum<T> Create<T>(T value) where T : struct
        {
            return new EquatableEnum<T>(value);
        }
    }

    /// <summary>
    /// Adapting wrapper from an enum type to <see cref="IEquatable{T}" />
    /// </summary>
    /// <remarks>
    /// Equation is performed without boxing since we defer to the (secret enum specialization of) <see cref="EqualityComparer{T}.Default" />.
    /// This wrapper allows constraining to <see cref="IEquatable{T}" /> while still taking enum parameters (which don't implement that interface,
    /// for perhaps historical reasons).
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public readonly struct EquatableEnum<T> : IEquatable<EquatableEnum<T>>
        where T : struct
    {
        private readonly T m_value;

        [SuppressMessage("Microsoft.Usage", "CA2207:InitializeValueTypeStaticFieldsInline")]
        static EquatableEnum()
        {
            Contract.Assume(typeof(T).GetTypeInfo().IsEnum, "Bad instantiation; type parameter T must be an enum");
        }

        /// <summary>
        /// Creates an equatable wrapper for the given value.
        /// </summary>
        public EquatableEnum(T value)
        {
            m_value = value;
        }

        /// <summary>
        /// Gets the wrapped value.
        /// </summary>
        public T Value => m_value;

        /// <inheritdoc />
        public bool Equals(EquatableEnum<T> other)
        {
            // Though enums aren't IEquatable for some reason, there's a nonboxing specialization provided by EqualityComparer.Default
            return EqualityComparer<T>.Default.Equals(m_value, other.m_value);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return m_value.GetHashCode();
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator T(EquatableEnum<T> me)
        {
            return me.m_value;
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator EquatableEnum<T>(T unwrapped)
        {
            return new EquatableEnum<T>(unwrapped);
        }

        /// <nodoc />
        public static bool operator ==(EquatableEnum<T> left, EquatableEnum<T> right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(EquatableEnum<T> left, EquatableEnum<T> right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Value-type variant of <see cref="Tuple" />.
    /// </summary>
    /// <remarks>
    /// This type is optimized to minimize the need for allocations and boxing. Implementing <see cref="IEquatable{T}" />
    /// ensures that no boxing is needed when using <see cref="EqualityComparer{T}.Default" /> for some collection.
    /// The constraints to <see cref="IEquatable{T}" /> for the item types ensures that there is no accidental boxing of
    /// the items themselves during comparison. Note that to wrap an enum or non-IEquatable class one must thus use
    /// wrappers like <see cref="EquatableEnum{T}" /> or <see cref="EquatableClass{T}" />.
    /// </remarks>
    public readonly struct StructTuple<TItem1, TItem2> : IEquatable<StructTuple<TItem1, TItem2>>
    {
        /// <summary>
        /// Item the first.
        /// </summary>
        public readonly TItem1 Item1;

        /// <summary>
        /// Item the second.
        /// </summary>
        public readonly TItem2 Item2;

        internal StructTuple(TItem1 item1, TItem2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // Adapted from System.Tuple.
            int h1 = EqualityComparer<TItem1>.Default.GetHashCode(Item1);
            int h2 = EqualityComparer<TItem2>.Default.GetHashCode(Item2);
            return StructTuple.CombineHashCodes(h1, h2);
        }

        /// <inheritdoc />
        public bool Equals(StructTuple<TItem1, TItem2> other)
        {
            return EqualityComparer<TItem1>.Default.Equals(Item1, other.Item1) &&
                   EqualityComparer<TItem2>.Default.Equals(Item2, other.Item2);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public static bool operator ==(StructTuple<TItem1, TItem2> left, StructTuple<TItem1, TItem2> right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(StructTuple<TItem1, TItem2> left, StructTuple<TItem1, TItem2> right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Value-type variant of <see cref="Tuple" />.
    /// </summary>
    /// <remarks>
    /// This type is optimized to minimize the need for allocations and boxing. The constraints to <see cref="IEquatable{T}" />
    /// ensure that no boxing is needed when using <see cref="EqualityComparer{T}.Default" /> for some collection.
    /// TODO: Would be nice to instead have a safe variant of EqualityComparer{T}.Default instead of these constraints. Hard to use enums, etc.
    /// which the default comparer handles specially. (Enums don't implement IEquatable{T} for some reason). We just want to avoid the
    /// case of normal structs that don't implement IEquatable{T}. Could do some overload resolution trickery like with <see cref="StructUtilities.Equals{T}" />.
    /// </remarks>
    public readonly struct StructTuple<TItem1, TItem2, TItem3> : IEquatable<StructTuple<TItem1, TItem2, TItem3>>
    {
        /// <summary>
        /// Item the first.
        /// </summary>
        public readonly TItem1 Item1;

        /// <summary>
        /// Item the second.
        /// </summary>
        public readonly TItem2 Item2;

        /// <summary>
        /// Item the third.
        /// </summary>
        public readonly TItem3 Item3;

        internal StructTuple(TItem1 item1, TItem2 item2, TItem3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // Adapted from System.Tuple.
            int h1 = EqualityComparer<TItem1>.Default.GetHashCode(Item1);
            int h2 = EqualityComparer<TItem2>.Default.GetHashCode(Item2);
            int h3 = EqualityComparer<TItem3>.Default.GetHashCode(Item3);
            return StructTuple.CombineHashCodes(h1, h2, h3);
        }

        /// <inheritdoc />
        public bool Equals(StructTuple<TItem1, TItem2, TItem3> other)
        {
            return EqualityComparer<TItem1>.Default.Equals(Item1, other.Item1) &&
                   EqualityComparer<TItem2>.Default.Equals(Item2, other.Item2) &&
                   EqualityComparer<TItem3>.Default.Equals(Item3, other.Item3);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public static bool operator ==(StructTuple<TItem1, TItem2, TItem3> left, StructTuple<TItem1, TItem2, TItem3> right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(StructTuple<TItem1, TItem2, TItem3> left, StructTuple<TItem1, TItem2, TItem3> right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Value-type variant of <see cref="Tuple" />.
    /// </summary>
    /// <remarks>
    /// This type is optimized to minimize the need for allocations and boxing. The constraints to <see cref="IEquatable{T}" />
    /// ensure that no boxing is needed when using <see cref="EqualityComparer{T}.Default" /> for some collection.
    /// TODO: Would be nice to instead have a safe variant of EqualityComparer{T}.Default instead of these constraints. Hard to use enums, etc.
    /// which the default comparer handles specially. (Enums don't implement IEquatable{T} for some reason). We just want to avoid the
    /// case of normal structs that don't implement IEquatable{T}. Could do some overload resolution trickery like with <see cref="StructUtilities.Equals{T}" />.
    /// </remarks>
    public readonly struct StructTuple<TItem1, TItem2, TItem3, TItem4> : IEquatable<StructTuple<TItem1, TItem2, TItem3, TItem4>>
    {
        /// <summary>
        /// Item the first.
        /// </summary>
        public readonly TItem1 Item1;

        /// <summary>
        /// Item the second.
        /// </summary>
        public readonly TItem2 Item2;

        /// <summary>
        /// Item the third.
        /// </summary>
        public readonly TItem3 Item3;

        /// <summary>
        /// Item the fourth.
        /// </summary>
        public readonly TItem4 Item4;

        internal StructTuple(TItem1 item1, TItem2 item2, TItem3 item3, TItem4 item4)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // Adapted from System.Tuple.
            int h1 = EqualityComparer<TItem1>.Default.GetHashCode(Item1);
            int h2 = EqualityComparer<TItem2>.Default.GetHashCode(Item2);
            int h3 = EqualityComparer<TItem3>.Default.GetHashCode(Item3);
            int h4 = EqualityComparer<TItem4>.Default.GetHashCode(Item4);
            return StructTuple.CombineHashCodes(h1, h2, h3, h4);
        }

        /// <inheritdoc />
        public bool Equals(StructTuple<TItem1, TItem2, TItem3, TItem4> other)
        {
            return EqualityComparer<TItem1>.Default.Equals(Item1, other.Item1) &&
                   EqualityComparer<TItem2>.Default.Equals(Item2, other.Item2) &&
                   EqualityComparer<TItem3>.Default.Equals(Item3, other.Item3) &&
                   EqualityComparer<TItem4>.Default.Equals(Item4, other.Item4);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public static bool operator ==(StructTuple<TItem1, TItem2, TItem3, TItem4> left, StructTuple<TItem1, TItem2, TItem3, TItem4> right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(StructTuple<TItem1, TItem2, TItem3, TItem4> left, StructTuple<TItem1, TItem2, TItem3, TItem4> right)
        {
            return !left.Equals(right);
        }
    }
}
