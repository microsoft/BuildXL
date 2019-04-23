// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Ternary conditional expression.
    /// </summary>
    /// <remarks>
    /// This type is similar to IConditionalExpression from typescript AST.
    /// </remarks>
    public class ConditionalExpression : Expression
    {
        /// <summary>
        /// Condition.
        /// </summary>
        public Expression ConditionExpression { get; }

        /// <summary>
        /// Then expression.
        /// </summary>
        public Expression ThenExpression { get; }

        /// <summary>
        /// Else expression.
        /// </summary>
        public Expression ElseExpression { get; }

        /// <nodoc />
        public ConditionalExpression(
            Expression conditionExpression,
            Expression thenExpression,
            Expression elseExpression,
            LineInfo location)
            : base(location)
        {
            Contract.Requires(conditionExpression != null);
            Contract.Requires(thenExpression != null);
            Contract.Requires(elseExpression != null);

            ConditionExpression = conditionExpression;
            ThenExpression = thenExpression;
            ElseExpression = elseExpression;
        }

        /// <nodoc />
        public ConditionalExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            ConditionExpression = ReadExpression(context);
            ThenExpression = ReadExpression(context);
            ElseExpression = ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            ConditionExpression.Serialize(writer);
            ThenExpression.Serialize(writer);
            ElseExpression.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.IteExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{ConditionExpression.ToDebugString()} ? {ThenExpression.ToDebugString()} : {ElseExpression.ToDebugString()}");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var condition = ConditionExpression.Eval(context, env, frame);

            if (condition.IsErrorValue)
            {
                return condition;
            }

            return IsTruthy(condition.Value) ? ThenExpression.Eval(context, env, frame) : ElseExpression.Eval(context, env, frame);
        }
    }
}
