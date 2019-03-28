// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Workspaces.Core;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Blocks project-like imports and exports in V2 modules
    /// </summary>
    internal sealed class ForbidProjectLikeImportsAndExportsOnV2SpecsRule : LanguageRule
    {
        private ForbidProjectLikeImportsAndExportsOnV2SpecsRule()
        {
        }

        public static ForbidProjectLikeImportsAndExportsOnV2SpecsRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidProjectLikeImportsAndExportsOnV2SpecsRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckProjectLikeImportsOrExportsNotInV2Specs,
                TypeScript.Net.Types.SyntaxKind.ImportDeclaration,
                TypeScript.Net.Types.SyntaxKind.ExportDeclaration);

            context.RegisterSyntaxNodeAction(
                this,
                CheckProjectLikeImportFromIsNotInV2Specs,
                TypeScript.Net.Types.SyntaxKind.CallExpression);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckProjectLikeImportFromIsNotInV2Specs(INode node, DiagnosticContext context)
        {
            var callExpression = node.Cast<ICallExpression>();

            // If the project belongs to a module with implicit references and the expression is an importFrom, then the specifier cannot be
            // project-like
            if (context.Workspace.SpecBelongsToImplicitSemanticsModule(context.SourceFile.GetAbsolutePath(context.PathTable)) &&
                callExpression.IsImportFrom())
            {
                var moduleSpecifier = callExpression.Arguments[0].Cast<IStringLiteral>();
                if (ModuleReferenceResolver.IsValidModuleReference(moduleSpecifier) &&
                    !ModuleReferenceResolver.IsModuleReference(moduleSpecifier))
                {
                    context.Logger.ReportProjectLikeImportOrExportNotAllowedInModuleWithImplicitSemantics(
                        context.LoggingContext,
                        node.LocationForLogging(context.SourceFile),
                        callExpression.Arguments[0].GetFormattedText());
                }
            }
        }

        private static void CheckProjectLikeImportsOrExportsNotInV2Specs(INode node, DiagnosticContext context)
        {
            IExpression specifier = node.GetModuleSpecifier();
            var literalExpression = specifier?.As<IStringLiteral>();

            // There is a lint rule that enforces this, but it might have not run yet
            if (literalExpression == null || literalExpression.LiteralKind == LiteralExpressionKind.None)
            {
                return;
            }

            // If the spec belongs to a module with implicit references, then project-like imports are not allowed
            if (context.Workspace.SpecBelongsToImplicitSemanticsModule(context.SourceFile.GetAbsolutePath(context.PathTable))
                && !ModuleReferenceResolver.IsModuleReference(literalExpression))
            {
                context.Logger.ReportProjectLikeImportOrExportNotAllowedInModuleWithImplicitSemantics(
                    context.LoggingContext,
                    node.LocationForLogging(context.SourceFile),
                    specifier.GetFormattedText());
            }
        }
    }
}
