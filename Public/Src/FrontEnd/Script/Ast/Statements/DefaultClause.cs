// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Core;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Statements
{
    /// <summary>
    /// Default statement.
    /// </summary>
    public class DefaultClause : Statement
    {
        /// <nodoc />
        public IReadOnlyList<Statement> Statements { get; }

        /// <nodoc />
        public DefaultClause(IReadOnlyList<Statement> statements, LineInfo location)
            : base(location)
        {
            Contract.Requires(statements != null);
            Contract.RequiresForAll(statements, s => s != null);

            Statements = statements;
        }

        /// <nodoc />
        public DefaultClause(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Statements = ReadArrayOf<Statement>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            WriteArrayOf(Statements, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.DefaultClause;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var statements = string.Join(Environment.NewLine, Statements.Select(statement => statement.ToDebugString()));
            return "default: " + Environment.NewLine + statements;
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvalStatements(context, env, Statements, frame);
        }
    }
}
