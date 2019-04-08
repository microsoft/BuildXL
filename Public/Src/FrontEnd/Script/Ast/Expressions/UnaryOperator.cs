// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Unary operator.
    /// </summary>
    public enum UnaryOperator : byte
    {
        /// <summary>
        /// Negative: -x;
        /// </summary>
        Negative = 0,

        /// <summary>
        /// Not: !x;
        /// </summary>
        Not,

        /// <summary>
        /// Bitwise not: ~x;
        /// </summary>
        BitwiseNot,

        /// <summary>
        /// Array spread: ...x;
        /// </summary>
        Spread,

        /// <summary>
        /// Typeof: typeof x;
        /// </summary>
        TypeOf,

        /// <summary>
        /// Unary plus: +x;
        /// </summary>
        UnaryPlus,
    }

    /// <summary>
    /// Unary operator extension.
    /// </summary>
    public static class UnaryOperatorExtensions
    {
        /// <summary>
        /// Gets string representation.
        /// </summary>
        public static string ToDisplayString(this UnaryOperator operatorKind)
        {
            switch (operatorKind)
            {
                case UnaryOperator.Negative:
                    return "-";
                case UnaryOperator.Not:
                    return "!";
                case UnaryOperator.BitwiseNot:
                    return "~";
                case UnaryOperator.Spread:
                    return "...";
                case UnaryOperator.TypeOf:
                    return "typeof";
                case UnaryOperator.UnaryPlus:
                    return "+";
                default:
                    Contract.Assert(false, "Unknown unary operator '" + operatorKind + "'.");
                    throw new InvalidOperationException("Unknown unary operator '" + operatorKind + "'.");
            }
        }
    }
}
