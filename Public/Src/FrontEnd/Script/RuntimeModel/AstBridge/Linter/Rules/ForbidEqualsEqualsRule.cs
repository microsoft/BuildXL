// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Operator '==' is not allowed and '===' should be used instead.
    /// </summary>
    internal sealed class ForbidEqualsEqualsRule : LanguageRule
    {
        private ForbidEqualsEqualsRule()
        { }

        public static ForbidEqualsEqualsRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidEqualsEqualsRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckEqualsEqualsIsNotAllowed,
                TypeScript.Net.Types.SyntaxKind.BinaryExpression);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckEqualsEqualsIsNotAllowed(INode node, DiagnosticContext context)
        {
            var binaryExpression = node.Cast<IBinaryExpression>();

            if (binaryExpression.OperatorToken.Kind == TypeScript.Net.Types.SyntaxKind.EqualsEqualsToken)
            {
                context.Logger.ReportNotSupportedNonStrictEquality(context.LoggingContext, binaryExpression.OperatorToken.LocationForLogging(context.SourceFile));
            }
        }
    }
}
