// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void LogThrowIsNotAllowed(INode node, DiagnosticContext context)
        {
            context.Logger.ReportThrowNotAllowed(context.LoggingContext, node.LocationForLogging(context.SourceFile));
        }
    }
}
