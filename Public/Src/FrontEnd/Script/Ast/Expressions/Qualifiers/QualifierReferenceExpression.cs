// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Represents an expression that references a 'qualifier' variable.
    /// </summary>
    public sealed class QualifierReferenceExpression : Expression
    {
        /// <nodoc />
        public QualifierReferenceExpression(LineInfo location)
            : base(location)
        {
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            // Intentionally left blank
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.QualifierReferenceExpression;

        /// <inheritdoc />
        public override string ToDebugString() => Constants.Names.CurrentQualifier;

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            // Do nothing! There is nothing to serialize here.
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            Contract.Assert(env.CurrentFileModule != null, "env.CurrentFileModule != null");

            // LINT rules should prevent a user from using qualifiers in the prelude.
            var currentQualifier = env.CurrentFileModule.Qualifier.Qualifier;
            return EvaluationResult.Create(currentQualifier);
        }
    }
}
