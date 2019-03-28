// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Flags (with a warning for now) that 'import * from' is deprecated
    /// </summary>
    internal sealed class ForbidImportStarRule : LanguagePolicyRule
    {
        private ForbidImportStarRule()
        { }

        public static ForbidImportStarRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidImportStarRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckNoImportStar,
                TypeScript.Net.Types.SyntaxKind.ImportDeclaration);
        }

        private static void CheckNoImportStar(INode node, DiagnosticContext context)
        {
            // Rule that prevents from using <code>import * from 'foo.dsc';</code>.
            var importDeclaration = node.Cast<IImportDeclaration>();
            if (importDeclaration.IsLikeImport)
            {
                context.Logger.ReportImportStarIsObsolete(context.LoggingContext, node.LocationForLogging(context.SourceFile));
            }
        }
    }
}
