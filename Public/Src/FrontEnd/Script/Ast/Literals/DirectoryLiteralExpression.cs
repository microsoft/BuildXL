// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Util;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Literals
{
    /// <summary>
    /// Literal that represents a sealed directory.
    /// </summary>
    /// <remarks>
    /// This literal could be created using following syntax:
    /// <code>
    /// let d1 = d`.`;
    /// let d2 = d`${path}/foo`;
    /// </code>
    /// </remarks>
    public class DirectoryLiteralExpression : Expression
    {
        /// <summary>
        /// Path expression that encapsulates a value that was provided in the d``.
        /// </summary>
        public Expression PathExpression { get; }

        /// <summary>
        /// Constructor that takes path literal expression.
        /// </summary>
        public DirectoryLiteralExpression(Expression pathExpression, LineInfo location)
            : base(location)
        {
            Contract.Requires(pathExpression != null);
            PathExpression = pathExpression;
        }

        /// <nodoc />
        public DirectoryLiteralExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            PathExpression = ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            PathExpression.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.DirectoryLiteral;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var exprStr = 
                PathExpression is PathLiteral pathLiteral 
                    ? PathUtil.NormalizePath(ToDebugString(pathLiteral.Value)) 
                    : PathExpression.ToDebugString();
            return I($"{Constants.Names.DirectoryInterpolationFactory}`{exprStr}`");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var evaluatedPathExpression = PathExpression.Eval(context, env, frame);
            if (evaluatedPathExpression.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            try
            {
                var path = Converter.ExpectPath(evaluatedPathExpression, strict: true, context: new ConversionContext(pos: 1));
                return EvaluationResult.Create(DirectoryArtifact.CreateWithZeroPartialSealId(path));
            }
            catch (ConvertException e)
            {
                context.Errors.ReportUnexpectedValueTypeOnConversion(env, e, Location);
                return EvaluationResult.Error;
            }
        }
    }
}
