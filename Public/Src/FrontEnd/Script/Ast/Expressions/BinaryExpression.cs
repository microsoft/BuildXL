// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Core;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Binary expression.
    /// </summary>
    public class BinaryExpression : Expression
    {
        /// <summary>
        /// Left-hand side expression.
        /// </summary>
        public Expression LeftExpression { get; }

        /// <summary>
        /// Binary operator.
        /// </summary>
        public BinaryOperator OperatorKind { get; }

        /// <summary>
        /// Right-hand side expression.
        /// </summary>
        public Expression RightExpression { get; }

        /// <nodoc />
        public BinaryExpression(Expression leftExpression, BinaryOperator operatorKind, Expression rightExpression, LineInfo location)
            : base(location)
        {
            Contract.Requires(leftExpression != null);
            Contract.Requires(rightExpression != null);

            LeftExpression = leftExpression;
            OperatorKind = operatorKind;
            RightExpression = rightExpression;
        }

        /// <nodoc />
        public BinaryExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            LeftExpression = (Expression)Read(context);
            OperatorKind = (BinaryOperator)context.Reader.ReadByte();
            RightExpression = (Expression)Read(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            LeftExpression.Serialize(writer);
            writer.Write((byte)OperatorKind);
            RightExpression.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.BinaryExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{LeftExpression.ToDebugString()} {OperatorKind.ToDisplayString()} {RightExpression.ToDebugString()}");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var leftCandidate = LeftExpression.Eval(context, env, frame);

            if (leftCandidate.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            EvaluationResult left = leftCandidate;
            EvaluationResult right = default(EvaluationResult);

            if (OperatorKind != BinaryOperator.And && OperatorKind != BinaryOperator.Or)
            {
                // Don't eval right expression for And and Or operators due to possible short circuit.
                var rightCandidate = RightExpression.Eval(context, env, frame);

                if (rightCandidate.IsErrorValue)
                {
                    return EvaluationResult.Error;
                }

                right = rightCandidate;
            }

            try
            {
                checked
                {
                    int leftNumber;
                    int rightNumber;

                    switch (OperatorKind)
                    {
                        case BinaryOperator.Addition:
                            // Different cases:

                            // 1. If left OR right is a string result is a string
                            // 2. If left AND right are numbers - result is a number
                            string leftString = left.Value as string;
                            string rightString = right.Value as string;

                            // First case: if any of the frame is string
                            if (leftString != null)
                            {
                                if (rightString != null)
                                {
                                    return EvaluationResult.Create(leftString + rightString);
                                }

                                return EvaluationResult.Create(leftString + ToStringConverter.ObjectToString(context, right));
                            }

                            if (rightString != null)
                            {
                                return EvaluationResult.Create(ToStringConverter.ObjectToString(context, left) + rightString);
                            }

                            // Expecting numbers, but can't report type mismatch error, because in this case string or number are allowed.
                            if (TryGetNumbers(left, right, out leftNumber, out rightNumber))
                            {
                                return EvaluationResult.Create(leftNumber + rightNumber);
                            }

                            context.Errors.ReportUnexpectedValueType(env, LeftExpression, left, typeof(int), typeof(string));
                            return EvaluationResult.Error;

                        // Math operators
                        case BinaryOperator.Remainder:
                            return EvaluationResult.Create(Converter.ExpectNumber(left, position: 0) % Converter.ExpectNumber(right, position: 1));
                        case BinaryOperator.Multiplication:
                            return EvaluationResult.Create(Converter.ExpectNumber(left, position: 0) * Converter.ExpectNumber(right, position: 1));
                        case BinaryOperator.Subtraction:
                            return EvaluationResult.Create(Converter.ExpectNumber(left, position: 0) - Converter.ExpectNumber(right, position: 1));
                        case BinaryOperator.Exponentiation:
                            return NumberOperations.Power(context, Converter.ExpectNumber(left), Converter.ExpectNumber(right), LocationForLogging(context, env));

                        // Equality + Comparison
                        case BinaryOperator.Equal:
                            return EvaluationResult.Create(left.Equals(right));
                        case BinaryOperator.NotEqual:
                            return EvaluationResult.Create(!left.Equals(right));
                        case BinaryOperator.GreaterThanOrEqual:
                            return EvaluationResult.Create(Converter.ExpectNumber(left, position: 0) >= Converter.ExpectNumber(right, position: 1));
                        case BinaryOperator.GreaterThan:
                            return EvaluationResult.Create(Converter.ExpectNumber(left, position: 0) > Converter.ExpectNumber(right, position: 1));
                        case BinaryOperator.LessThanOrEqual:
                            return EvaluationResult.Create(Converter.ExpectNumber(left, position: 0) <= Converter.ExpectNumber(right, position: 1));
                        case BinaryOperator.LessThan:
                            return EvaluationResult.Create(Converter.ExpectNumber(left, position: 0) < Converter.ExpectNumber(right, position: 1));

                        // Conditionals
                        case BinaryOperator.Or:
                            return EvalOr(context, env, frame, left);
                        case BinaryOperator.And:
                            return EvalAnd(context, env, frame, left);

                            // Bitwise operations
                            // For all bitwise operations call to ToEnumValueIfNeeded is required to convert
                            // numeric representation to enum value if left hand side is enum
                            // (ExpectNumberOrEnums will make sure that left and right operands have the same type).
                        case BinaryOperator.BitWiseOr:
                            if (ExpectNumbersOrEnums(context, env, left, right, out leftNumber, out rightNumber))
                            {
                                return Converter.ToEnumValueIfNeeded(left.GetType(), leftNumber | rightNumber);
                            }

                            return EvaluationResult.Error;

                        case BinaryOperator.BitWiseAnd:
                            if (ExpectNumbersOrEnums(context, env, left, right, out leftNumber, out rightNumber))
                            {
                                return Converter.ToEnumValueIfNeeded(left.GetType(), leftNumber & rightNumber);
                            }

                            return EvaluationResult.Error;
                        case BinaryOperator.BitWiseXor:
                            if (ExpectNumbersOrEnums(context, env, left, right, out leftNumber, out rightNumber))
                            {
                                return Converter.ToEnumValueIfNeeded(left.GetType(), leftNumber ^ rightNumber);
                            }

                            break;
                        case BinaryOperator.LeftShift:
                            return EvaluationResult.Create(Converter.ExpectNumber(left, position: 0) << Converter.ExpectNumber(right, position: 1));
                        case BinaryOperator.SignPropagatingRightShift:
                            return EvaluationResult.Create(NumberOperations.SignPropagatingRightShift(
                                    Converter.ExpectNumber(left, position: 0),
                                    Converter.ExpectNumber(right, position: 1)).Value);
                        case BinaryOperator.ZeroFillingRightShift:
                            return EvaluationResult.Create(NumberOperations.ZeroFillingRightShift(
                                Converter.ExpectNumber(left, position: 0),
                                Converter.ExpectNumber(right, position: 1)).Value);

                        default:
                            // And and Or operator should have been replaced by the ite expression.
                            // case BinaryOperator.And:
                            // case BinaryOperator.Or:
                            //    return (bool)l && (bool)RightExpression.Eval(context, env, frame);
                            //    return (bool) l || (bool) RightExpression.Eval(context, env, frame);
                            Contract.Assert(false);
                            break;
                    }
                }
            }
            catch (OverflowException)
            {
                context.Logger.ReportArithmeticOverflow(
                    context.LoggingContext,
                    LocationForLogging(context, env),
                    this.ToDisplayString(context));
            }
            catch (ConvertException convertException)
            {
                context.Errors.ReportUnexpectedValueType(
                    env,
                    convertException.ErrorContext.Pos == 0 ? LeftExpression : RightExpression,
                    convertException.Value, convertException.ExpectedTypesToString(context));
            }
            catch (DivideByZeroException)
            {
                context.Errors.ReportDivideByZeroException(env, this, Location);
            }

            return EvaluationResult.Error;
        }

        private EvaluationResult EvalOr(Context context, ModuleLiteral env, EvaluationStackFrame args, EvaluationResult left)
        {
            return IsTruthy(left) ? left : RightExpression.Eval(context, env, args);
        }

        private EvaluationResult EvalAnd(Context context, ModuleLiteral env, EvaluationStackFrame args, EvaluationResult left)
        {
            return !IsTruthy(left) ? left : RightExpression.Eval(context, env, args);
        }

        private bool ExpectNumbersOrEnums(Context context, ModuleLiteral env, EvaluationResult left, EvaluationResult right, out int leftNumber, out int rightNumber)
        {
            // For bitwise operators arguments could be of type int or enum
            // but both should be of the same type!
            int? leftNumberCandidate = left.Value as int?;

            if (leftNumberCandidate != null)
            {
                // if left is number, then right should be number as well.
                rightNumber = Converter.ExpectNumber(right, position: 1);
                leftNumber = leftNumberCandidate.Value;
                return true;
            }

            // Left is not a number. Maybe it is an enum.
            leftNumberCandidate = (left.Value as EnumValue)?.Value;
            if (leftNumberCandidate != null)
            {
                // Then right hand side should be enum as well
                rightNumber = Converter.ExpectEnumValue(right, position: 1);
                leftNumber = leftNumberCandidate.Value;
                return true;
            }

            context.Errors.ReportUnexpectedValueType(env, LeftExpression, left, typeof(int), typeof(EnumValue));

            // Results should not be used in this case!
            leftNumber = -1;
            rightNumber = -1;
            return false;
        }

        private static bool TryGetNumbers(EvaluationResult left, EvaluationResult right, out int leftNumber, out int rightNumber)
        {
            // For bitwise operators arguments could be of type int or enum
            // but both should be of the same type!
            if (left.Value is int leftNumberCanidate)
            {
                // if left is number, then right should be number as well.
                if (right.Value is int rightNumberCandidate)
                {
                    leftNumber = leftNumberCanidate;
                    rightNumber = rightNumberCandidate;
                    return true;
                }

                // Failure case.
                Converter.ExpectNumber(right, position: 1);

                // This is unreachable, because ExpectNumber will throw.
                leftNumber = -1;
                rightNumber = -1;
                return false;
            }

            // Results should not be used in this case!
            leftNumber = -1;
            rightNumber = -1;

            return false;
        }
    }
}
