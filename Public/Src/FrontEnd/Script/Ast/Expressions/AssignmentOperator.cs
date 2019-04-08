// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Supported compound assignment operators.
    /// </summary>
    /// <remarks>
    /// For more details see https://developer.mozilla.org/en-US/docs/Web/JavaScript/Guide/Expressions_and_Operators#Assignment_operators
    /// </remarks>
    public enum AssignmentOperator : byte
    {
        /// <summary>
        /// x = y.
        /// </summary>
        Assignment,

        /// <summary>
        /// x += y.
        /// </summary>
        AdditionAssignment,

        /// <summary>
        /// x -= y.
        /// </summary>
        SubtractionAssignment,

        /// <summary>
        /// x *= y.
        /// </summary>
        MultiplicationAssignment,

        // DivisionAssignment, // Not supported, because division is not supported in DScript.

        /// <summary>
        /// x %= y.
        /// </summary>
        RemainderAssignment,

        /// <summary>
        /// x **= y.
        /// </summary>
        ExponentiationAssignment,

        /// <summary>
        /// x &lt;&lt;= y.
        /// </summary>
        LeftShiftAssignment,

        /// <summary>
        /// x >>= y.
        /// </summary>
        RightShiftAssignment,

        /// <summary>
        /// x >>>= y.
        /// </summary>
        UnsignedRightShiftAssignment,

        /// <summary>
        /// x &amp;= y.
        /// </summary>
        BitwiseAndAssignment,

        /// <summary>
        /// x ^= y.
        /// </summary>
        BitwiseXorAssignment,

        /// <summary>
        /// x |= y.
        /// </summary>
        BitwiseOrAssignment,
    }

    /// <summary>
    /// Assignment operator extension.
    /// </summary>
    public static class AssignmentOperatorExtension
    {
        /// <summary>
        /// Gets string representation.
        /// </summary>
        public static string ToDisplayString(this AssignmentOperator @operator)
        {
            switch (@operator)
            {
                case AssignmentOperator.Assignment:
                    return "=";
                case AssignmentOperator.AdditionAssignment:
                    return "+=";
                case AssignmentOperator.SubtractionAssignment:
                    return "-=";
                case AssignmentOperator.MultiplicationAssignment:
                    return "*=";
                case AssignmentOperator.RemainderAssignment:
                    return "%=";
                case AssignmentOperator.ExponentiationAssignment:
                    return "**=";
                case AssignmentOperator.LeftShiftAssignment:
                    return "<<=";
                case AssignmentOperator.RightShiftAssignment:
                    return ">>=";
                case AssignmentOperator.UnsignedRightShiftAssignment:
                    return ">>>=";
                case AssignmentOperator.BitwiseAndAssignment:
                    return "&=";
                case AssignmentOperator.BitwiseXorAssignment:
                    return "^=";
                case AssignmentOperator.BitwiseOrAssignment:
                    return "|=";
                default:
                    Contract.Assert(false);
                    throw new ArgumentOutOfRangeException(nameof(@operator), @operator, null);
            }
        }
    }
}
