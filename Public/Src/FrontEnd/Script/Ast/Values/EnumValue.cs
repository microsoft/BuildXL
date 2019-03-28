// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Literals;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Enum value.
    /// </summary>
    [DebuggerDisplay("{ToString(), nq}")]
    public sealed class EnumValue : IConstantExpression
    {
        /// <summary>
        /// Integral value of the enum.
        /// </summary>
        public int Value { get; }

        /// <inheritdoc />
        object IConstantExpression.Value => this;

        /// <summary>
        /// We don't carry the location information of an enum member
        /// </summary>
        public LineInfo Location => default(LineInfo);

        /// <summary>
        /// Constant name of the enum value.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <nodoc />
        public EnumValue(SymbolAtom name, int value)
        {
            Name = name;
            Value = value;
        }

        /// <summary>
        /// Factory method that creates instance of enum value without a name.
        /// </summary>
        public static EnumValue Create(int value)
        {
            return new EnumValue(SymbolAtom.Invalid, value);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            var objEnumValue = obj as EnumValue;

            return objEnumValue?.Value == Value;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Value.ToString(CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Value;
        }

        /// <summary>
        /// Implements implicit conversion to int.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator int(EnumValue enumValue)
        {
            Contract.Requires(enumValue != null);
            return enumValue.Value;
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            ConstExpressionSerializer.Write(writer, this);
        }
    }
}
