// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using SyntaxKind = TypeScript.Net.Types.SyntaxKind;

namespace BuildXL.FrontEnd.Script.Statements
{
    /// <summary>
    /// Block statement.
    /// </summary>
    public class BlockStatement : Statement
    {
        /// <nodoc />
        public IReadOnlyList<Statement> Statements { get; }

        /// <nodoc />
        public BlockStatement(IReadOnlyList<Statement> statements, LineInfo location)
            : base(location)
        {
            Contract.Requires(statements != null);
            Contract.RequiresForAll(statements, s => s != null);

            Statements = statements;
        }

        /// <nodoc />
        public BlockStatement(DeserializationContext context, LineInfo location)
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
        public override SyntaxKind Kind => SyntaxKind.BlockStatement;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return "{" + Environment.NewLine + string.Join(Environment.NewLine, Statements.Select(s => s.ToDebugString())) + Environment.NewLine + "}";
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvalStatements(context, env, Statements, frame);
        }
    }
}
