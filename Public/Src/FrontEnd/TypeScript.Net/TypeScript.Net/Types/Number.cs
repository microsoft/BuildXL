// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using static BuildXL.Utilities.FormattableStringEx;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Type that models 'number' type from JavaScript.
    /// </summary>
    /// <remarks>
    /// - JavaScript operations never throws, instead of that Number.Infinity would be provided.
    /// - JavaScript Number is a floating point number that could be integral in some cases (for instance, it has >>, &lt;&lt; operations etc).
    ///   This could lead to very weird behavior when the client will consume this value as an integral one.
    /// </remarks>
    public readonly struct Number : IEquatable<Number>
    {
        private readonly double m_value;

        /// <nodoc />
        public Number(double value)
            : this()
        {
            m_value = value;
        }

        /// <nodoc />
        public static bool operator ==(Number n1, Number n2)
        {
            return n1.Equals(n2);
        }

        /// <nodoc />
        public static bool operator !=(Number n1, Number n2)
        {
            return !n1.Equals(n2);
        }

        /// <nodoc />
        public static Number operator ++(Number number)
        {
            return number.Increment();
        }

        /// <nodoc />
        public static Number operator +(Number number)
        {
            return number;
        }

        /// <nodoc />
        public static Number operator -(Number number)
        {
            return new Number(-number.m_value);
        }

        /// <nodoc />
        public static Number operator ~(Number number)
        {
            return new Number(~(long)number.m_value);
        }

        /// <nodoc />
        public static Number operator |(Number left, Number right)
        {
            return new Number(left.AsInt64() | (long)right.m_value);
        }

        /// <nodoc />
        public static Number operator &(Number left, Number right)
        {
            return new Number(left.AsInt64() & (long)right.m_value);
        }

        /// <nodoc />
        public static Number operator >>(Number left, int right)
        {
            return new Number(left.AsInt64() >> right);
        }

        /// <nodoc />
        public static Number operator <<(Number left, int right)
        {
            return new Number(left.AsInt64() << right);
        }

        /// <nodoc />
        public static Number operator +(Number left, Number right)
        {
            return new Number(left.m_value + right.m_value);
        }

        /// <nodoc />
        public static Number operator -(Number left, Number right)
        {
            return new Number(left.m_value - right.m_value);
        }

        /// <nodoc />
        public static Number operator *(Number left, Number right)
        {
            return new Number(left.m_value * right.m_value);
        }

        /// <nodoc />
        public static Number operator /(Number left, Number right)
        {
            return new Number(left.m_value / right.m_value);
        }

        /// <nodoc />
        public static Number operator %(Number left, Number right)
        {
            return new Number(left.m_value % right.m_value);
        }

        /// <nodoc />
        public static Number operator ^(Number left, Number right)
        {
            return new Number(left.AsInt64() ^ right.AsInt64());
        }

        /// <nodoc />
        public bool IsNaN() => double.IsNaN(m_value);

        /// <nodoc />
        public bool IsInfinity() => double.IsInfinity(m_value);

        /// <nodoc />
        public bool IsFinite() => !IsInfinity();

        /// <nodoc />
        public bool IsPositiveInfinity() => double.IsPositiveInfinity(m_value);

        /// <nodoc />
        public bool IsNegativeInfinity() => double.IsNegativeInfinity(m_value);

        /// <summary>
        /// Returns <see cref="long"/> representation of the numeric value.
        /// </summary>
        [Pure]
        public long AsInt64()
        {
            return (long)m_value;
        }

        /// <summary>
        /// Returns <see cref="int"/> representation of the numeric value.
        /// </summary>
        [Pure]
        public int AsInt32()
        {
            return (int)m_value;
        }

        /// <summary>
        /// Returns whether this number can be converted to <see cref="int"/> without causing an overflow exception.
        /// </summary>
        public bool IsInt32()
        {
            return m_value <= int.MaxValue && m_value >= int.MinValue;
        }

        /// <summary>
        /// Returns <see cref="uint"/> representation of the numeric value.
        /// </summary>
        [Pure]
        public uint AsUInt32()
        {
            return (uint)m_value;
        }

        /// <summary>
        /// "Increments" current value by crating new instance with an old_value + 1.
        /// </summary>
        /// <remarks>
        /// Number in JavaScript is a floating point number that works pretty bad with such operations
        /// like increment/decrement.
        /// It means that this operation could potentially return a value with similar integral part:
        /// <code>
        /// Number n = CreateWeirdNumber();
        /// bool b = n.AsInt64() == n.Increment().AsInt64(); // this could be true!
        /// </code>
        /// </remarks>
        [Pure]
        public Number Increment()
        {
            // This potentially can lead to issues, because m_value is double!
            return new Number(m_value + 1);
        }

        /// <nodoc />
        public bool Equals(Number other)
        {
            return m_value.Equals(other.m_value);
        }

        /// <inheritdoc />
        bool IEquatable<Number>.Equals(Number number)
        {
            return this.Equals(number);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is Number && Equals((Number)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return m_value.GetHashCode();
        }

        /// <summary>
        /// Returns true when the <paramref name="name"/> is a valid numeric literal.
        /// </summary>
        public static bool IsNumericLiteralName(string name)
        {
            // The intent of numeric names is that
            //     - they are names with text in a numeric form, and that
            //     - setting properties/indexing with them is always equivalent to doing so with the numeric literal 'numLit',
            //         acquired by applying the abstract 'ToNumber' operation on the name's text.
            //
            // The subtlety is in the latter portion, as we cannot reliably say that anything that looks like a numeric literal is a numeric name.
            // In fact, it is the case that the text of the name must be equal to 'ToString(numLit)' for this to hold.
            //
            // Consider the property name '"0xF00D"'. When one indexes with '0xF00D', they are actually indexing with the value of 'ToString(0xF00D)'
            // according to the ECMAScript specification, so it is actually as if the user indexed with the string '"61453"'.
            // Thus, the text of all numeric literals equivalent to '61543' such as '0xF00D', '0xf00D', '0170015', etc. are not valid numeric names
            // because their 'ToString' representation is not equal to their original text.
            // This is motivated by ECMA-262 sections 9.3.1, 9.8.1, 11.1.5, and 11.2.1.
            //
            // Here, we test whether 'ToString(ToNumber(name))' is exactly equal to 'name'.
            // The '+' prefix operator is equivalent here to applying the abstract ToNumber operation.
            // Applying the 'toString()' method on a number gives us the abstract ToString operation on a number.
            //
            // Note that this accepts the values 'Infinity', '-Infinity', and 'NaN', and that this is intentional.
            // This is desired behavior, because when indexing with them as numeric entities, you are indexing
            // with the strings '"Infinity"', '"-Infinity"', and '"NaN"' respectively.

            // TODO: Verify equivalence!
            // return (+name).toString() == name;
            return TryConvertFromString(name).ToString().Equals(name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Factory method that parses <paramref name="value"/> to <see cref="Number"/>.
        /// </summary>
        public static Number? TryParse(string value)
        {
            var doubleValue = TryConvertFromString(value);
            if (doubleValue == null)
            {
                return null;
            }

            return new Number(doubleValue.Value);
        }

        /// <summary>
        /// Factory method that parses <paramref name="value"/> to <see cref="Number"/>.
        /// </summary>
        /// <exception cref="FormatException">Throws if <paramref name="value"/> is not in the correct format.</exception>
        public static Number Parse(string value)
        {
            var result = TryParse(value);
            if (result == null)
            {
                throw new FormatException(I($"Input string '{value}' is not in a correct format for type number."));
            }

            return result.Value;
        }

        private static double? TryConvertFromString(string value)
        {
            // TODO: this implementation coudl be very naive right now!

            // First, trying to convert to long
            long result;

            // Checking integer values
            if (long.TryParse(value, out result))
            {
                return result;
            }

            // Ignoring culture for now!
            double doubleResult;
            if (double.TryParse(value, out doubleResult))
            {
                return doubleResult;
            }

            if (value.StartsWith("0x"))
            {
                return Convert.ToInt64(value, 16);
            }

            if (value.StartsWith("0b"))
            {
                return Convert.ToInt64(value, 2);
            }

            if (value.StartsWith("0o"))
            {
                return Convert.ToInt64(value, 8);
            }

            switch (value)
            {
                case "+Infinity":
                case "Infinity":
                    return double.PositiveInfinity;
                case "-Infinity":
                    return double.NegativeInfinity;
                default:
                    return double.NaN;
            }
        }
    }
}
