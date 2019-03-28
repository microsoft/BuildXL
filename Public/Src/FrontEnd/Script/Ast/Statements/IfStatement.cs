// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Statements
{
    /// <summary>
    /// If statement.
    /// </summary>
    public class IfStatement : Statement
    {
        /// <nodoc />
        public Expression Condition { get; }

        /// <nodoc />
        public Statement ThenStatement { get; }

        /// <nodoc />
        [CanBeNull]
        public Statement ElseStatement { get; }

        /// <nodoc />
        public IfStatement(Expression condition, Statement thenStatement, Statement elseStatement, LineInfo location)
            : base(location)
        {
            Contract.Requires(condition != null);
            Contract.Requires(thenStatement != null);

            Condition = condition;
            ThenStatement = thenStatement;
            ElseStatement = elseStatement;
        }

        /// <nodoc />
        public IfStatement(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Condition = ReadExpression(context);
            ThenStatement = Read<Statement>(context);
            ElseStatement = Read<Statement>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            Condition.Serialize(writer);
            ThenStatement.Serialize(writer);
            Serialize(ElseStatement, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.IfStatement;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var @else = ElseStatement != null ? "else " + ElseStatement.ToDebugString() : string.Empty;
            return I($"if ({Condition.ToDebugString()}) {ThenStatement.ToDebugString()}{@else}");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var condition = Condition.Eval(context, env, frame);

            if (condition.IsErrorValue)
            {
                return condition;
            }

            // TODO:ST: I don't think this condition is correct.
            // In this case the result would be 1: const x = 1; const y = if (false) {x++;}else{x++;}
            // if statement is a statement, the result always should be void or undefined.
            return Expression.IsTruthy(condition)
                ? ThenStatement.Eval(context, env, frame)
                : ((ElseStatement == null) ? EvaluationResult.Undefined : ElseStatement.Eval(context, env, frame));
        }
    }
}
