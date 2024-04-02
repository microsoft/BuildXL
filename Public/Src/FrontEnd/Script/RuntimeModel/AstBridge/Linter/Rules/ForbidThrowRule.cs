// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that 'throw' is never used.
    /// </summary>
    internal sealed class ForbidThrowRule : LanguageRule
    {
        private ForbidThrowRule()
        { }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.All;

        /// <nodoc />
        public static ForbidThrowRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidThrowRule();
            result.Initialize(context);
            return result;
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                LogThrowIsNotAllowed,
                TypeScript.Net.Types.SyntaxKind.ThrowKeyword,
                TypeScript.Net.Types.SyntaxKind.ThrowStatement);
        }


        private static void LogThrowIsNotAllowed(INode node, DiagnosticContext context)
        {
            context.Logger.ReportThrowNotAllowed(context.LoggingContext, node.LocationForLogging(context.SourceFile));
        }
    }
}
