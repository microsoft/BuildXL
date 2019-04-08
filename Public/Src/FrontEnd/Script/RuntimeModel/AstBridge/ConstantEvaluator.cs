// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using TypeScript.Net.Types;
using Number = BuildXL.FrontEnd.Script.Core.Number;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Special class that evaluates constant expressions.
    /// </summary>
    /// <remarks>
    /// Particularly useful for enum members.
    /// </remarks>
    internal static class ConstantEvaluator
    {
        /// <summary>
        /// Evaluate constant value for the <paramref name="node"/>.
        /// </summary>
        /// <remarks>
        /// This function is very similar to evalConstant function from checker.ts.
        /// </remarks>
        /// <returns>
        /// Returns null if node can't be folded or is not constant.
        /// Returns Number if constant folding is possible (note, that return object could represent an overflowen value).
        /// </returns>
        public static Number? EvalConstant(INode node)
        {
            try
            {
                checked
                {
                    switch (node.Kind)
                    {
                        case TypeScript.Net.Types.SyntaxKind.PrefixUnaryExpression:
                            var prefixUnary = node.Cast<IPrefixUnaryExpression>();
                            var value = EvalConstant(prefixUnary.Operand);

                            if (!IsValid(value))
                            {
                                return value;
                            }

                            switch (prefixUnary.Operator)
                            {
                                case TypeScript.Net.Types.SyntaxKind.PlusToken:
                                    return value;
                                case TypeScript.Net.Types.SyntaxKind.MinusToken:
                                    return -GetNumber(value);
                                case TypeScript.Net.Types.SyntaxKind.TildeToken:
                                    return ~GetNumber(value);
                            }

                            return null;
                        case TypeScript.Net.Types.SyntaxKind.BinaryExpression:
                            var binary = node.Cast<IBinaryExpression>();

                            var left = EvalConstant(binary.Left);
                            if (!IsValid(left))
                            {
                                return left;
                            }

                            var right = EvalConstant(binary.Right);
                            if (!IsValid(right))
                            {
                                return right;
                            }

                            switch (binary.OperatorToken.Kind)
                            {
                                case TypeScript.Net.Types.SyntaxKind.BarToken:
                                    return GetNumber(left) | GetNumber(right);
                                case TypeScript.Net.Types.SyntaxKind.AmpersandToken:
                                    return GetNumber(left) & GetNumber(GetNumber(right));
                                case TypeScript.Net.Types.SyntaxKind.CaretToken:
                                    return GetNumber(left) ^ GetNumber(right);

                                case TypeScript.Net.Types.SyntaxKind.GreaterThanGreaterThanGreaterThanToken:
                                    // >>> is Zero-fill right shift
                                    return Script.Core.NumberOperations.ZeroFillingRightShift(left.Value, right.Value);
                                case TypeScript.Net.Types.SyntaxKind.GreaterThanGreaterThanToken:
                                    // >> is Sign-propagating right shift
                                    return Script.Core.NumberOperations.SignPropagatingRightShift(left.Value, right.Value);

                                case TypeScript.Net.Types.SyntaxKind.LessThanLessThanToken:
                                    return GetNumber(left) << GetNumber(right);

                                case TypeScript.Net.Types.SyntaxKind.AsteriskToken:
                                    return GetNumber(left) * GetNumber(right);
                                case TypeScript.Net.Types.SyntaxKind.SlashToken:
                                    return GetNumber(left) / GetNumber(right);
                                case TypeScript.Net.Types.SyntaxKind.PlusToken:
                                    return GetNumber(left) + GetNumber(right);
                                case TypeScript.Net.Types.SyntaxKind.MinusToken:
                                    return GetNumber(left) - GetNumber(right);
                                case TypeScript.Net.Types.SyntaxKind.PercentToken:
                                    return GetNumber(left) % GetNumber(right);
                            }

                            return null;

                        case TypeScript.Net.Types.SyntaxKind.NumericLiteral:
                            return node.Cast<ILiteralExpression>().TryConvertToNumber();

                        case TypeScript.Net.Types.SyntaxKind.ParenthesizedExpression:
                            return EvalConstant(node.Cast<IParenthesizedExpression>().Expression);

                        // TODO:ST: TypeScript compiler supports Identifier, ElementAccessExpression and PropertyAccessExpression
                        // For instance, following code is totally valid:
                        // const enum Enum1 { value1 = 42, }
                        // const enum Enum2 { value1 = Enum1.value1, }
                        case TypeScript.Net.Types.SyntaxKind.Identifier:
                        case TypeScript.Net.Types.SyntaxKind.ElementAccessExpression:
                        case TypeScript.Net.Types.SyntaxKind.PropertyAccessExpression:
                            return null;
                    }
                }
            }
            catch (OverflowException)
            {
                return Number.Overflow();
            }

            return null;
        }

        private static bool IsValid(Number? number)
        {
            return number?.IsOverflow == false;
        }

        private static int GetNumber(Number? number)
        {
            Contract.Requires(number != null);

            return number.Value.Value;
        }
    }
}
