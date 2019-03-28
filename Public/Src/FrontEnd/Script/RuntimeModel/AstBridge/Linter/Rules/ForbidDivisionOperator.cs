// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that division is not allowed
    /// </summary>
    internal sealed class ForbidDivisionOperator : LanguageRule
    {
        private ForbidDivisionOperator()
        { }

        public static ForbidDivisionOperator CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidDivisionOperator();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckDivisionIsNotAllowed,
                TypeScript.Net.Types.SyntaxKind.BinaryExpression);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckDivisionIsNotAllowed(INode node, DiagnosticContext context)
        {
            var binaryExpression = node.As<BinaryExpression>();
            if (binaryExpression.OperatorToken.Kind == TypeScript.Net.Types.SyntaxKind.SlashToken)
            {
                context.Logger.ReportDivisionOperatorIsNotSupported(context.LoggingContext, node.LocationForLogging(context.SourceFile));
            }
        }
    }
}
