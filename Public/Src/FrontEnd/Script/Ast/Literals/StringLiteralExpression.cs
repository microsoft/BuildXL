// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Literals
{
    /// <summary>
    /// Tagged literal (i.e., string interpolated) expression.
    /// </summary>
    /// <remarks>
    /// This literal is created from the following syntax:
    /// <code>
    /// let result = `answer is ${x}`;
    /// </code>
    /// </remarks>
    public class StringLiteralExpression : Expression
    {
        /// <summary>
        /// Expression for <code>String.interpolate</code> invocation.
        /// </summary>
        private ApplyExpression StringInterpolationInvocationExpression { get; }

        /// <summary>
        /// Constructor that takes string invocation expression to String.interpolate.
        /// </summary>
        public StringLiteralExpression(ApplyExpression stringInterpolationInvocationExpression, LineInfo location)
            : base(location)
        {
            Contract.Requires(stringInterpolationInvocationExpression != null);

            StringInterpolationInvocationExpression = stringInterpolationInvocationExpression;
        }

        /// <nodoc />
        public StringLiteralExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            StringInterpolationInvocationExpression = (ApplyExpression)ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            StringInterpolationInvocationExpression.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.StringLiteralExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return StringInterpolationInvocationExpression.ToDebugString();
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return StringInterpolationInvocationExpression.Eval(context, env, frame);
        }
    }
}
