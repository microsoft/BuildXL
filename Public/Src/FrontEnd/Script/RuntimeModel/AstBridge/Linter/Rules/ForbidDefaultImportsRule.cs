// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that 'import =' is not allowed.
    /// </summary>
    internal sealed class ForbidDefaultImportsRule : LanguagePolicyRule
    {
        private ForbidDefaultImportsRule()
        { }

        public static ForbidDefaultImportsRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidDefaultImportsRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckImportDeclaration,
                TypeScript.Net.Types.SyntaxKind.ImportDeclaration);
        }

        private static void CheckImportDeclaration(INode node, DiagnosticContext context)
        {
            var importDeclaration = (IImportDeclaration)node;

            if (importDeclaration.ImportClause.NamedBindings == null)
            {
                context.Logger.ReportDefaultImportsNotAllowed(context.LoggingContext, node.LocationForLogging(context.SourceFile));
            }
        }
    }
}
