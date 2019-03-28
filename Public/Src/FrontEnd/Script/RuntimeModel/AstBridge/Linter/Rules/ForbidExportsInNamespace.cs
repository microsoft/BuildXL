// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;
using TS = TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Export declarations are not permitted inside namespaces. This is the TS behavior.
    /// </summary>
    internal sealed class ForbidExportsInNamespace : LanguagePolicyRule
    {
        private ForbidExportsInNamespace()
        { }

        public static ForbidExportsInNamespace CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidExportsInNamespace();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            // TODO: change to registering the export clause and walk up the parent when that becomes available
            context.RegisterSyntaxNodeAction(
                this,
                CheckExportIsNotInNamespace,
                TS.SyntaxKind.ModuleDeclaration);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckExportIsNotInNamespace(INode node, DiagnosticContext context)
        {
            var moduleDeclaration = node.As<IModuleDeclaration>();

            if (moduleDeclaration?.Body == null)
            {
                return;
            }

            foreach (var statement in moduleDeclaration.Body.Statements)
            {
                if (statement.Kind == TS.SyntaxKind.ExportDeclaration)
                {
                    context.Logger.ReportExportsAreNotAllowedInsideNamespaces(context.LoggingContext, statement.LocationForLogging(context.SourceFile));
                }
            }
        }
    }
}
