// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using SyntaxKind = TypeScript.Net.Types.SyntaxKind;

namespace BuildXL.FrontEnd.Script.Statements
{
    /// <summary>
    /// Case statement.
    /// </summary>
    public class CaseClause : Statement
    {
        /// <nodoc />
        public Expression CaseExpression { get; }

        /// <nodoc />
        public IReadOnlyList<Statement> Statements { get; }

        /// <nodoc />
        public CaseClause(Expression caseExpression, IReadOnlyList<Statement> statements, LineInfo location)
            : base(location)
        {
            Contract.Requires(caseExpression != null);
            Contract.Requires(statements != null);
            Contract.RequiresForAll(statements, s => s != null);

            CaseExpression = caseExpression;
            Statements = statements;
        }

        /// <nodoc />
        public CaseClause(DeserializationContext context, LineInfo location)
            : base(location)
        {
            CaseExpression = ReadExpression(context);
            Statements = ReadArrayOf<Statement>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            CaseExpression.Serialize(writer);
            WriteArrayOf(Statements, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.CaseClause;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var statements = string.Join(Environment.NewLine, Statements.Select(statement => statement.ToDebugString()));
            return "case " + CaseExpression.ToDebugString() + ": " + Environment.NewLine + statements;
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvalStatements(context, env, Statements, frame);
        }
    }
}
