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
    /// Switch expression clause.
    /// </summary>
    public class SwitchExpressionClause : Expression
    {
        /// <nodoc/>
        public bool IsDefaultFallthrough { get; set; }

        /// <nodoc />
        public Expression Match { get; }

        /// <nodoc />
        public Expression Expression { get; }

        /// <nodoc />
        public SwitchExpressionClause(
            Expression match,
            Expression expression,
            LineInfo location)
            : base(location)
        {
            Contract.Requires(match != null);
            Contract.Requires(expression != null);

            Match = match;
            Expression = expression;
        }

        /// <nodoc />
        public SwitchExpressionClause(
            Expression expression,
            LineInfo location)
            : base(location)
        {
            Contract.Requires(expression != null);

            IsDefaultFallthrough = true;
            Expression = expression;
        }

        /// <nodoc />
        public SwitchExpressionClause(DeserializationContext context, LineInfo location)
            : base(location)
        {
            IsDefaultFallthrough = context.Reader.ReadBoolean();
            if (!IsDefaultFallthrough)
            {
                Match = ReadExpression(context);
            }

            Expression = ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(IsDefaultFallthrough);
            if (!IsDefaultFallthrough)
            {
                Match.Serialize(writer);
            }

            Expression.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.SwitchExpressionClause;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var match = IsDefaultFallthrough ? "default" : Match.ToDebugString();
            return I($"{match} : {Expression.ToDebugString()}");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return Expression.Eval(context, env, frame);
        }
    }
}
