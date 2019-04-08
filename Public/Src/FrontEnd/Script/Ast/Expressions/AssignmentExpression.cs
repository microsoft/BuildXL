// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Script.Core;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using SyntaxKind = TypeScript.Net.Types.SyntaxKind;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Assignment expression.
    /// </summary>
    public class AssignmentExpression : AssignmentOrIncrementDecrementExpression
    {
        /// <summary>
        /// Index of the l-value.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Name of the variable for compound assignment ('x' in 'x += 42' expression).
        /// </summary>
        public SymbolAtom LeftExpression { get; }

        /// <summary>
        /// Kind of the compound assignment.
        /// </summary>
        public AssignmentOperator OperatorKind { get; }

        /// <summary>
        /// Right-hand side expression.
        /// </summary>
        [NotNull]
        public Expression RightExpression { get; }

        /// <nodoc />
        public AssignmentExpression(SymbolAtom leftExpression, int index, AssignmentOperator operatorKind, [NotNull]Expression rightExpression, LineInfo location)
            : base(location)
        {
            Contract.Requires(index >= 0);
            Contract.Requires(rightExpression != null);

            LeftExpression = leftExpression;
            OperatorKind = operatorKind;
            RightExpression = rightExpression;
            Index = index;
        }

        /// <nodoc />
        public AssignmentExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            var reader = context.Reader;
            LeftExpression = reader.ReadSymbolAtom();
            OperatorKind = (AssignmentOperator)reader.ReadByte();
            RightExpression = ReadExpression(context);
            Index = reader.ReadInt32Compact();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(LeftExpression);
            writer.Write((byte)OperatorKind);
            RightExpression.Serialize(writer);
            writer.WriteCompact(Index);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.AssignmentExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{ToDebugString(LeftExpression)} {OperatorKind.ToDisplayString()} {RightExpression.ToDebugString()}");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            Contract.Assume(frame.Length > Index);

            // By construction, the above preconditions should hold.
            var value = RightExpression.Eval(context, env, frame);

            if (value.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            if (OperatorKind == AssignmentOperator.Assignment)
            {
                frame[Index] = value;
                return frame[Index];
            }

            try
            {
                // All operations should be in the checked context to fail on overflow!
                checked
                {
                    var arg = frame[Index];

                    switch (OperatorKind)
                    {
                        case AssignmentOperator.AdditionAssignment:
                            if (Converter.TryGet<int>(arg, out int lIntValue) && Converter.TryGet<int>(value, out int rIntValue))
                            {
                                frame[Index] = NumberLiteral.Box(lIntValue + rIntValue);
                                break;
                            }
                            else if (Converter.TryGet<string>(arg, out string lStrValue) && Converter.TryGet<string>(value, out string rStrValue))
                            {
                                frame[Index] = EvaluationResult.Create(lStrValue + rStrValue);
                                break;
                            }

                            throw Converter.CreateException<int>(value, context: new ConversionContext(pos: Index));
                        case AssignmentOperator.SubtractionAssignment:
                            frame[Index] = NumberLiteral.Box(Converter.ExpectNumber(arg, position: 0) - Converter.ExpectNumber(value, position: 1));
                            break;
                        case AssignmentOperator.MultiplicationAssignment:
                            frame[Index] = NumberLiteral.Box(Converter.ExpectNumber(arg, position: 0) * Converter.ExpectNumber(value, position: 1));
                            break;
                        case AssignmentOperator.RemainderAssignment:
                            frame[Index] = NumberLiteral.Box(Converter.ExpectNumber(arg, position: 0) % Converter.ExpectNumber(value, position: 1));
                            break;
                        case AssignmentOperator.ExponentiationAssignment:
                            frame[Index] = NumberOperations.Power(
                                context,
                                Converter.ExpectNumber(arg, position: 0),
                                Converter.ExpectNumber(value, position: 1), LocationForLogging(context, env));
                            break;
                        case AssignmentOperator.LeftShiftAssignment:
                            frame[Index] = NumberLiteral.Box(Converter.ExpectNumber(frame[Index], position: 0) << Converter.ExpectNumber(value, position: 1));
                            break;
                        case AssignmentOperator.RightShiftAssignment:
                            frame[Index] = NumberLiteral.Box(NumberOperations.SignPropagatingRightShift(
                                Converter.ExpectNumber(arg, position: 0),
                                Converter.ExpectNumber(value, position: 1)).Value);
                            break;
                        case AssignmentOperator.UnsignedRightShiftAssignment:
                            frame[Index] = NumberLiteral.Box(NumberOperations.ZeroFillingRightShift(
                                Converter.ExpectNumber(arg, position: 0),
                                Converter.ExpectNumber(value, position: 1)).Value);
                            break;
                        case AssignmentOperator.BitwiseAndAssignment:
                            frame[Index] =
                                Converter.ToEnumValueIfNeeded(
                                    arg.GetType(),
                                    Converter.ExpectNumberOrEnum(arg, position: 0) & Converter.ExpectNumberOrEnum(value, position: 1));
                            break;
                        case AssignmentOperator.BitwiseXorAssignment:
                            frame[Index] =
                                Converter.ToEnumValueIfNeeded(
                                    arg.GetType(),
                                    Converter.ExpectNumberOrEnum(arg, position: 0) ^ Converter.ExpectNumberOrEnum(value, position: 1));
                            break;
                        case AssignmentOperator.BitwiseOrAssignment:
                            frame[Index] =
                                Converter.ToEnumValueIfNeeded(
                                    arg.GetType(),
                                    Converter.ExpectNumberOrEnum(arg, position: 0) | Converter.ExpectNumberOrEnum(value, position: 1));
                            break;
                        default:
                            var message = I($"Unknown operator kind '{OperatorKind}'");
                            Contract.Assert(false, message);
                            throw new InvalidOperationException(message);
                    }
                }

                return frame[Index];
            }
            catch (OverflowException)
            {
                context.Logger.ReportArithmeticOverflow(context.LoggingContext, LocationForLogging(context, env), this.ToDisplayString(context));
            }
            catch (ConvertException exception)
            {
                if (exception.ErrorContext.Pos == 0)
                {
                    context.Errors.ReportUnexpectedValueTypeForName(env, LeftExpression, exception.ExpectedTypesToString(context), exception.Value, Location);
                }
                else
                {
                    context.Errors.ReportUnexpectedValueType(env, RightExpression, exception.Value, exception.ExpectedTypesToString(context));
                }
            }

            return EvaluationResult.Error;
        }
    }
}
