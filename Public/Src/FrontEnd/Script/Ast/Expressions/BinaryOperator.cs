// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Binary operator.
    /// </summary>
    public enum BinaryOperator : byte
    {
        /// <summary>
        /// a + b;
        /// </summary>
        Addition,

        /// <summary>
        /// a - b;
        /// </summary>
        Subtraction,

        /// <summary>
        /// a * b;
        /// </summary>
        Multiplication,

        // Division, // not supported!

        /// <summary>
        /// a % b;
        /// </summary>
        Remainder,

        /// <summary>
        /// a ** b;
        /// </summary>
        Exponentiation,

        /// <summary>
        /// a > b;
        /// </summary>
        GreaterThan,

        /// <summary>
        /// a &lt; b;
        /// </summary>
        LessThan,

        /// <summary>
        /// a >= b;
        /// </summary>
        GreaterThanOrEqual,

        /// <summary>
        /// Le.
        /// </summary>
        LessThanOrEqual,

        /// <summary>
        /// a === b (a == b is forbidden in DScript).
        /// </summary>
        Equal,

        /// <summary>
        /// a !== b (a != b is forbidden in DScript).
        /// </summary>
        NotEqual,

        /// <summary>
        /// a &amp;&amp; b;
        /// </summary>
        And,

        /// <summary>
        /// a || b;
        /// </summary>
        Or,

        /// <summary>
        /// a | b;
        /// </summary>
        BitWiseOr,

        /// <summary>
        /// a &amp; b;
        /// </summary>
        BitWiseAnd,

        /// <summary>
        /// a ^ b;
        /// </summary>
        BitWiseXor,

        /// <summary>
        /// Bit shift operator &lt;&lt;
        /// </summary>
        LeftShift,

        /// <summary>
        /// a >> b: Shifts a in binary representation b (&lt; 32) bits to the right, discarding bits shifted off.
        /// </summary>
        /// <remarks>
        /// To understand the difference between >> and >>> see following: https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Bitwise_Operators.
        /// But basically, the difference would be visible only for signed numbers.
        /// </remarks>
        SignPropagatingRightShift,

        /// <summary>
        /// Shifts a in binary representation b (&lt; 32) bits to the right, discarding bits shifted off, and shifting in zeroes from the left.
        /// </summary>
        ZeroFillingRightShift,
    }

    /// <summary>
    /// Binary operator extension.
    /// </summary>
    public static class BinaryOperatorExtension
    {
        /// <summary>
        /// Gets string representation.
        /// </summary>
        public static string ToDisplayString(this BinaryOperator op)
        {
            switch (op)
            {
                case BinaryOperator.Addition:
                    return "+";
                case BinaryOperator.And:
                    return "&&";
                case BinaryOperator.Equal:
                    return "===";
                case BinaryOperator.GreaterThan:
                    return ">";
                case BinaryOperator.GreaterThanOrEqual:
                    return ">=";
                case BinaryOperator.LessThan:
                    return "<";
                case BinaryOperator.LessThanOrEqual:
                    return "<=";
                case BinaryOperator.Multiplication:
                    return "*";
                case BinaryOperator.Remainder:
                    return "%";
                case BinaryOperator.NotEqual:
                    return "!==";
                case BinaryOperator.Or:
                    return "||";
                case BinaryOperator.BitWiseOr:
                    return "|";
                case BinaryOperator.BitWiseAnd:
                    return "&";
                case BinaryOperator.BitWiseXor:
                    return "^";
                case BinaryOperator.Subtraction:
                    return "-";
                case BinaryOperator.LeftShift:
                    return "<<";
                case BinaryOperator.SignPropagatingRightShift:
                    return ">>";
                case BinaryOperator.ZeroFillingRightShift:
                    return ">>>";
                case BinaryOperator.Exponentiation:
                    return "**";
                default:
                    Contract.Assert(false);
                    return op.ToString();
            }
        }
    }
}
