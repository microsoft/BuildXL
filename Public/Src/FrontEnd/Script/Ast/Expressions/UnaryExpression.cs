// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Runtime;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Unary expression.
    /// </summary>
    public class UnaryExpression : Expression
    {
        /// <summary>
        /// Kind of the unary operator.
        /// </summary>
        public UnaryOperator OperatorKind { get; }

        /// <summary>
        /// Expression.
        /// </summary>
        public Expression Expression { get; }

        /// <nodoc />
        public UnaryExpression(UnaryOperator operatorKind, Expression expression, LineInfo location)
            : base(location)
        {
            Contract.Requires(expression != null);
            OperatorKind = operatorKind;
            Expression = expression;
        }

        /// <nodoc />
        public UnaryExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            OperatorKind = (UnaryOperator)context.Reader.ReadByte();
            Expression = ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write((byte)OperatorKind);
            Expression.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.UnaryExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{OperatorKind.ToDisplayString()}({Expression.ToDebugString()})");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var v = Expression.Eval(context, env, frame);

            if (v.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            switch (OperatorKind)
            {
                case UnaryOperator.Negative:
                    try
                    {
                        checked
                        {
                            return EvaluationResult.Create(-Converter.ExpectNumber(v, position: 0));
                        }
                    }
                    catch (OverflowException)
                    {
                        context.Logger.ReportArithmeticOverflow(
                            context.LoggingContext,
                            LocationForLogging(context, env),
                            this.ToDisplayString(context));
                        return EvaluationResult.Error;
                    }

                case UnaryOperator.Not:
                    return EvaluationResult.Create(!IsTruthy(v));

                case UnaryOperator.BitwiseNot:
                    {
                        int? value = Converter.GetNumberOrEnumValue(v);
                        if (value != null)
                        {
                            return Converter.ToEnumValueIfNeeded(v.Value.GetType(), ~value.Value);
                        }

                        context.Errors.ReportUnexpectedValueType(env, Expression, v, typeof(int), typeof(EnumValue));
                        return EvaluationResult.Error;
                    }

                case UnaryOperator.Spread:
                    // Spread operator simply returns the evaluated value. This allows the operator to be context insensitive.
                    // TODO: In the future we may want to only allow spread operator inside arrays or function calls.
                    return v;

                case UnaryOperator.TypeOf:
                    return EvaluationResult.Create(EvalTypeOf(v.Value));

                // TODO: add default case. Throw there or report an error gracefully!
                case UnaryOperator.UnaryPlus:
                    {
                        int? numericValue = Converter.GetNumberOrEnumValue(v);
                        if (numericValue != null)
                        {
                            return EvaluationResult.Create(numericValue.Value);
                        }

                        if (v.Value is string stringValue)
                        {
                            return ConvertStringToNumber(stringValue, context, env);
                        }

                        if (v.Value is bool b)
                        {
                            return EvaluationResult.Create(ConvertBooleanToNumber(b));
                        }

                        context.Errors.ReportUnexpectedValueType(env, Expression, v, typeof(int), typeof(EnumValue), typeof(bool), typeof(string));
                    }

                    return EvaluationResult.Error;
            }

            return EvaluationResult.Error;
        }

        private EvaluationResult ConvertStringToNumber(string value, Context context, ModuleLiteral env)
        {
            var result = LiteralConverter.TryConvertNumber(value);
            if (result.IsValid)
            {
                return EvaluationResult.Create(result.Value);
            }

            if (result.IsOverflow)
            {
                context.Logger.ReportArithmeticOverflow(context.LoggingContext, LocationForLogging(context, env), value);
            }
            else if (result.IsInvalidFormat)
            {
                context.Logger.ReportInvalidFormatForStringToNumberConversion(context.LoggingContext, LocationForLogging(context, env), value);
            }
            else
            {
                Contract.Assert(false, "Unknown reason why number is invalid!");
            }

            return EvaluationResult.Error;
        }

        private static int ConvertBooleanToNumber(bool value)
        {
            return value ? 1 : 0;
        }

        private static string EvalTypeOf(object value)
        {
            var typeOfKind = RuntimeTypeIdExtensions.ComputeTypeOfKind(value);

            return typeOfKind.ToRuntimeString();
        }
    }
}
