// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Core;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Statements
{
    /// <summary>
    /// Break statement.
    /// </summary>
    public class BreakStatement : Statement
    {
        /// <nodoc />
        public BreakStatement(LineInfo location)
            : base(location)
        {
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.BreakStatement;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return "break;";
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Break;
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            // Intionally doing nothing.
        }
    }
}
