// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Configuration;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Blocks when a V2 spec references the same module that the spec belongs to.
    /// </summary>
    internal sealed class ForbidModuleSelfReferencingRule : LanguageRule
    {
        private ForbidModuleSelfReferencingRule()
        {
        }

        public static ForbidModuleSelfReferencingRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidModuleSelfReferencingRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckImportOrExportClause,
                TypeScript.Net.Types.SyntaxKind.ImportDeclaration,
                TypeScript.Net.Types.SyntaxKind.ExportDeclaration);

            context.RegisterSyntaxNodeAction(
                this,
                CheckImportFrom,
                TypeScript.Net.Types.SyntaxKind.CallExpression);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckImportFrom(INode node, DiagnosticContext context)
        {
            var callExpression = node.Cast<ICallExpression>();
            if (!callExpression.IsImportFrom())
            {
                return;
            }

            var moduleName = callExpression.Arguments[0].Cast<IStringLiteral>().Text;
            CheckImportedModule(node, moduleName, context);
        }

        private static void CheckImportedModule(INode node, string moduleName, DiagnosticContext context)
        {
            var module = context.Workspace.GetModuleBySpecFileName(context.SourceFile.GetAbsolutePath(context.PathTable));

            if (module.Definition.ResolutionSemantics == NameResolutionSemantics.ImplicitProjectReferences && module.Descriptor.Name == moduleName)
            {
                // The rule is enabled only for V2 modules. For V1 modules it still kind of make sense to import itself, instead of importing different files.
                context.Logger.ReportModuleShouldNotImportItself(
                    context.LoggingContext,
                    node.LocationForLogging(context.SourceFile),
                    moduleName);
            }
        }

        private static void CheckImportOrExportClause(INode node, DiagnosticContext context)
        {
            IExpression specifier = node.GetModuleSpecifier();
            var literalExpression = specifier?.As<IStringLiteral>();

            // There is a lint rule that enforces this, but it might have not run yet
            if (literalExpression == null)
            {
                return;
            }

            CheckImportedModule(node, literalExpression.Text, context);
        }
    }
}
