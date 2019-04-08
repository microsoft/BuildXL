// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that 'null' is not allowed.
    /// </summary>
    internal sealed class ForbidNullRule : LanguageRule
    {
        private ForbidNullRule()
        { }

        public static ForbidNullRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidNullRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckNullIsNotAllowed,
                TypeScript.Net.Types.SyntaxKind.NullKeyword);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckNullIsNotAllowed(INode node, DiagnosticContext context)
        {
            context.Logger.ReportNullNotAllowed(context.LoggingContext, node.LocationForLogging(context.SourceFile));
        }
    }
}
