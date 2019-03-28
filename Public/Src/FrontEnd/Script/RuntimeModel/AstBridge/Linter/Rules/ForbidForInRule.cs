// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that for-in loops are not present.
    /// </summary>
    internal sealed class ForbidForInRule : LanguageRule
    {
        private ForbidForInRule()
        { }

        public static ForbidForInRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidForInRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckForInStatementsAreNotAllowed,
                TypeScript.Net.Types.SyntaxKind.ForInStatement);
        }

        private static void CheckForInStatementsAreNotAllowed(INode node, DiagnosticContext context)
        {
            // For-in loops are simply not allowed in DScript.
            context.Logger.ReportForInLoopsNotSupported(context.LoggingContext, node.LocationForLogging(context.SourceFile));
        }
    }
}
