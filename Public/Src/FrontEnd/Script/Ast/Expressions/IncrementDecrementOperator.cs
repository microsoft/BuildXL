// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Increment and decrement operators.
    /// </summary>
    /// <remarks>
    /// These operators are a subset of all arithmetic operators that mutates state.
    /// For more details see https://developer.mozilla.org/en-US/docs/Web/JavaScript/Guide/Expressions_and_Operators#Arithmetic_operators
    /// </remarks>
    [Flags]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1008:EnumsShouldHaveZeroValue")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1714:FlagsEnumsShouldHavePluralNames")]
    public enum IncrementDecrementOperator : byte
    {
        #region Bits and Masks

        /// <summary>
        /// After applying <see cref="PrefixPostfixMask"/>, whether this operator represents a prefix operation.
        /// </summary>
        Prefix = 0x0,

        /// <summary>
        /// After applying <see cref="PrefixPostfixMask"/>, whether this operator represents a postfix operation.
        /// </summary>
        Postfix = 0x1,

        /// <summary>
        /// Mask to select prefix or postfix.
        /// </summary>
        PrefixPostfixMask = 0x1,

        /// <summary>
        /// After applying <see cref="IncrementDecrementMask"/>, whether this operator represents an increment operation.
        /// </summary>
        Increment = 0x0,

        /// <summary>
        /// After applying <see cref="IncrementDecrementMask"/>, whether this operator represents an decrement operation.
        /// </summary>
        Decrement = 0x2,

        /// <summary>
        /// Mask to select increment or decrement
        /// </summary>
        IncrementDecrementMask = 0x2,
        #endregion

        #region Values

        /// <summary>
        /// ++x.
        /// </summary>
        PrefixIncrement = Prefix | Increment,

        /// <summary>
        /// x++.
        /// </summary>
        PostfixIncrement = Postfix | Increment,

        /// <summary>
        /// --x.
        /// </summary>
        PrefixDecrement = Prefix | Decrement,

        /// <summary>
        /// x--.
        /// </summary>
        PostfixDecrement = Postfix | Decrement,
        #endregion
    }

    /// <summary>
    /// Increment decrement operator extension.
    /// </summary>
    public static class IncrementDecrementOperatorExtension
    {
        /// <summary>
        /// Gets string representation.
        /// </summary>
        public static string ToDisplayString(this IncrementDecrementOperator @operator)
        {
            switch (@operator)
            {
                case IncrementDecrementOperator.PrefixIncrement:
                case IncrementDecrementOperator.PostfixIncrement:
                    return "++";
                case IncrementDecrementOperator.PrefixDecrement:
                case IncrementDecrementOperator.PostfixDecrement:
                    return "--";
                default:
                    Contract.Assert(false);
                    throw new ArgumentOutOfRangeException(nameof(@operator), @operator, null);
            }
        }
    }
}
