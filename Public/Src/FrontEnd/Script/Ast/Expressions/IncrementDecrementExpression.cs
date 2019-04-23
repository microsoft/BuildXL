// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Increment decrement expression.
    /// </summary>
    public class IncrementDecrementExpression : AssignmentOrIncrementDecrementExpression
    {
        /// <summary>
        /// Index of the l-value.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Name of the target variable ('x' in 'x++' expression).
        /// </summary>
        public SymbolAtom Operand { get; }

        /// <nodoc />
        public IncrementDecrementOperator OperatorKind { get; }

        /// <nodoc />
        public IncrementDecrementExpression(SymbolAtom operand, int index, IncrementDecrementOperator operatorKind, LineInfo location)
            : base(location)
        {
            Contract.Requires(operand != null);
            Contract.Requires(index >= 0);

            Operand = operand;
            OperatorKind = operatorKind;
            Index = index;
        }

        /// <nodoc />
        public IncrementDecrementExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            var reader = context.Reader;
            Operand = reader.ReadSymbolAtom();
            OperatorKind = (IncrementDecrementOperator)reader.ReadByte();
            Index = reader.ReadInt32Compact();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Operand);
            writer.Write((byte)OperatorKind);
            writer.WriteCompact(Index);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.IncrementDecrementExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return ((OperatorKind & IncrementDecrementOperator.PrefixPostfixMask) == IncrementDecrementOperator.Prefix)
                ? OperatorKind.ToDisplayString() + ToDebugString(Operand)
                : ToDebugString(Operand) + OperatorKind.ToDisplayString();
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            Contract.Assume(frame.Length > Index);

            // By construction, the above preconditions should hold.
            try
            {
                // All operations should be in the checked context to fail on overflow!
                checked
                {
                    var oldValue = Converter.ExpectNumber(frame[Index], position: 0);
                    switch (OperatorKind)
                    {
                        case IncrementDecrementOperator.PrefixIncrement:
                            {
                                var newValue = NumberLiteral.Box(oldValue + 1);
                                frame[Index] = newValue;
                                return newValue;
                            }

                        case IncrementDecrementOperator.PostfixIncrement:
                            {
                                var newValue = NumberLiteral.Box(oldValue + 1);
                                frame[Index] = newValue;
                                return EvaluationResult.Create(oldValue);
                            }

                        case IncrementDecrementOperator.PrefixDecrement:
                            {
                                var newValue = NumberLiteral.Box(oldValue - 1);
                                frame[Index] = newValue;
                                return newValue;
                            }

                        case IncrementDecrementOperator.PostfixDecrement:
                            {
                                var newValue = NumberLiteral.Box(oldValue - 1);
                                frame[Index] = newValue;
                                return EvaluationResult.Create(oldValue);
                            }

                        default:
                            var message = I($"Unknown operator kind '{OperatorKind}'");
                            Contract.Assert(false, message);
                            throw new InvalidOperationException(message);
                    }
                }
            }
            catch (OverflowException)
            {
                context.Logger.ReportArithmeticOverflow(context.LoggingContext, LocationForLogging(context, env), this.ToDisplayString(context));
            }
            catch (ConvertException exception)
            {
                context.Errors.ReportUnexpectedValueTypeForName(env, Operand, exception.ExpectedTypesToString(context), exception.Value, Location);
            }

            return EvaluationResult.Error;
        }
    }
}
