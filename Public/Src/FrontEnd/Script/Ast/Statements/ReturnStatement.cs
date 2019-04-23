// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Statements
{
    /// <summary>
    /// Return statement.
    /// </summary>
    public class ReturnStatement : Statement
    {
        /// <nodoc />
        public Expression ReturnExpression { get; }

        /// <nodoc />
        public ReturnStatement(Expression returnExpression, LineInfo location)
            : base(location)
        {
            ReturnExpression = returnExpression;
        }

        /// <nodoc />
        public ReturnStatement(DeserializationContext context, LineInfo location)
            : base(location)
        {
            ReturnExpression = ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            Serialize(ReturnExpression, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ReturnStatement;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return "return" + (ReturnExpression != null ? " " + ReturnExpression.ToDebugString() : string.Empty) + ";";
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            if (ReturnExpression == null)
            {
                frame.ReturnStatementWasEvaluated = true;
                return EvaluationResult.Undefined;
            }

            var value = ReturnExpression.Eval(context, env, frame);

            frame.ReturnStatementWasEvaluated = true;

            return value;
        }
    }
}
