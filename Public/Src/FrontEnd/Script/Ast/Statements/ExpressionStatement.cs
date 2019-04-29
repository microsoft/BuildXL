// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Statements
{
    /// <summary>
    /// Expression statement.
    /// </summary>
    public class ExpressionStatement : Statement
    {
        /// <nodoc />
        public Expression Expression { get; }

        /// <nodoc />
        public ExpressionStatement(Expression expression, LineInfo location)
            : base(location)
        {
            Contract.Requires(expression != null);

            Expression = expression;
        }

        /// <nodoc />
        public ExpressionStatement(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Expression = ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            Expression.Serialize(writer);
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var value = Expression.Eval(context, env, frame);

            // TODO: this is very strange! Why error value is not propagated?
            return value.IsErrorValue ? value : EvaluationResult.Undefined;
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inhertidoc />
        public override SyntaxKind Kind => SyntaxKind.ExpressionStatement;

        /// <inhertidoc />
        public override string ToDebugString()
        {
            return Expression.ToDebugString() + ";";
        }
    }
}
