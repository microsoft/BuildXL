// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.FrontEnd.Script.Core
{
    /// <summary>
    /// Represents a reason why conversion to number was not successful.
    /// </summary>
    internal enum InvalidNumberKind : byte
    {
        None,
        Overflow,
        InvalidFormat,
    }

    /// <summary>
    /// Represents 'number' type in DScript.
    /// </summary>
    /// <remarks>
    /// Unlike in TypeScript, 'number' in DScript is a 32-bit integer, not a floating point.
    /// This type wraps int32 and adds some additional semantics, like whether number is valid or invalid due to overflow.
    /// </remarks>
    [DebuggerDisplay("{ToString(), nq}")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct Number
    {
        private readonly InvalidNumberKind m_invalidNumberKind;
        private readonly int m_value;

        /// <nodoc />
        public Number(int value)
            : this()
        {
            m_value = value;
            m_invalidNumberKind = InvalidNumberKind.None;
        }

        private Number(InvalidNumberKind invalidNumberKind)
        {
            m_invalidNumberKind = invalidNumberKind;
            m_value = -1;
        }

        /// <summary>
        /// Factory method that construct invalid number because of overflow.
        /// </summary>
        public static Number Overflow()
        {
            return new Number(InvalidNumberKind.Overflow);
        }

        /// <summary>
        /// Factory method that construct invalid number because of invalid format.
        /// </summary>
        public static Number InvalidFormat()
        {
            return new Number(InvalidNumberKind.InvalidFormat);
        }

        /// <summary>
        /// Returns numeric value.
        /// </summary>
        /// <exception cref="InvalidOperationException">Throws when <see cref="IsOverflow"/> is true.</exception>
        public int Value
        {
            get
            {
                if (!IsValid)
                {
                    throw new InvalidOperationException("Number should be valid.");
                }

                return m_value;
            }
        }

        /// <summary>
        /// Returns true if number is valid.
        /// </summary>
        public bool IsValid => m_invalidNumberKind == InvalidNumberKind.None;

        /// <summary>
        /// Returns true when number is invalid and was constructed as a result of overflow.
        /// </summary>
        public bool IsOverflow => m_invalidNumberKind == InvalidNumberKind.Overflow;

        /// <summary>
        /// Returns true when number is invalid and was constructed from invalid string.
        /// </summary>
        public bool IsInvalidFormat => m_invalidNumberKind == InvalidNumberKind.InvalidFormat;

        /// <summary>
        /// Implicit conversion from <paramref name="value"/> to <see cref="Number"/>.
        /// </summary>
        public static implicit operator Number(int value)
        {
            return new Number(value);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return IsValid ? Value.ToString(CultureInfo.InvariantCulture) : GetInvalidRepresentation();
        }

        private string GetInvalidRepresentation()
        {
            Contract.Requires(!IsValid);

            if (IsOverflow)
            {
                return "overflow";
            }

            if (IsInvalidFormat)
            {
                return "invalid format";
            }

            Contract.Assert(false, "Should not get here!");
            return string.Empty;
        }
    }
}
