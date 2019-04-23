// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Linq;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks template declarations are well shaped
    /// </summary>
    /// <remarks>
    /// This is a V2 rule that only runs when the semantic model is available
    /// </remarks>
    internal sealed class EnforceTemplateDeclarationRule : LanguageRule
    {
        private EnforceTemplateDeclarationRule()
        { }

        public static EnforceTemplateDeclarationRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceTemplateDeclarationRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckWellShapedTemplate,
                TypeScript.Net.Types.SyntaxKind.VariableDeclarationList);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckWellShapedTemplate(INode node, DiagnosticContext context)
        {
            var varDeclarationList = node.Cast<IVariableDeclarationList>();

            // If none of the declarations is a 'template', there is nothing to do
            if (!varDeclarationList.Declarations.Any(declaration => declaration.IsTemplateDeclaration()))
            {
                return;
            }

            // Template declarations are not allowed in non top-level declarations
            if (!node.IsTopLevelOrNamespaceLevelDeclaration())
            {
                context.Logger.TemplateDeclarationShouldBeTopLevel(
                    context.LoggingContext,
                    varDeclarationList.LocationForLogging(context.SourceFile));
                return;
            }

            // Template should be alone in the declaration
            if (varDeclarationList.Declarations.Count > 1)
            {
                context.Logger.TemplateDeclarationShouldBeAloneInTheStatement(
                    context.LoggingContext,
                    varDeclarationList.LocationForLogging(context.SourceFile));
                return;
            }

            var templateStatement = varDeclarationList.Parent as IVariableStatement;

            Contract.Assert(templateStatement != null);

            // The template statement should be the first (non injected) statement in the container
            var container = templateStatement.Parent.Cast<IStatementsContainer>();

            if (container.Statements.Elements.First(element => !element.IsInjectedForDScript() && element.Kind != TypeScript.Net.Types.SyntaxKind.ImportDeclaration) != templateStatement)
            {
                context.Logger.TemplateDeclarationShouldBeTheFirstStatement(
                    context.LoggingContext,
                    templateStatement.LocationForLogging(context.SourceFile));
                return;
            }

            // Template should have the right flags
            if ((templateStatement.Flags & NodeFlags.Export) == NodeFlags.None ||
                (templateStatement.Flags & NodeFlags.Ambient) == NodeFlags.None ||
                (varDeclarationList.Flags & NodeFlags.Const) == NodeFlags.None)
            {
                context.Logger.TemplateDeclarationShouldBeConstExportAmbient(
                    context.LoggingContext,
                    templateStatement.LocationForLogging(context.SourceFile));
                return;
            }

            // Template should have an initializer
            var templateDeclaration = varDeclarationList.Declarations[0];
            if (templateDeclaration.Initializer == null)
            {
                context.Logger.TemplateDeclarationShouldHaveInitializer(
                    context.LoggingContext,
                    templateStatement.LocationForLogging(context.SourceFile));
            }
        }
    }
}
