// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that symbol types is not present. Symbol types are not supported in DScript.
    /// </summary>
    internal sealed class ForbidSymbolTypeRule : LanguagePolicyRule
    {
        private ForbidSymbolTypeRule()
        { }

        public static ForbidSymbolTypeRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidSymbolTypeRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckSymbolKeywordIsNotAllowed,
                TypeScript.Net.Types.SyntaxKind.SymbolKeyword);
        }

        private static void CheckSymbolKeywordIsNotAllowed(INode node, DiagnosticContext context)
        {
            // Symbol keyword is not allowed
            context.Logger.ReportNotSupportedSymbolKeyword(context.LoggingContext, node.LocationForLogging(context.SourceFile));
        }
    }
}
