// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Statements
{
    /// <summary>
    /// Continue statement.
    /// </summary>
    public class ContinueStatement : Statement
    {
        /// <nodoc />
        public ContinueStatement(LineInfo location)
            : base(location)
        {
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ContinueStatement;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return "continue;";
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Continue;
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            // Intionally doing nothing.
        }
    }
}
