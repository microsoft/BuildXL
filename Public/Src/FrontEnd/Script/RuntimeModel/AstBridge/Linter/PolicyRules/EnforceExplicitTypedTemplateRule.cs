// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Rule that enforces template declarations to have an explicit type
    /// </summary>
    internal sealed class EnforceExplicitTypedTemplateRule : PolicyRule
    {
        /// <nodoc/>
        public override string Name => "TypedTemplates";

        /// <nodoc/>
        public override string Description => "Enforces template declarations to be explicitly typed.";

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckTypedTemplate,
                TypeScript.Net.Types.SyntaxKind.VariableDeclaration);
        }

        private static void CheckTypedTemplate(INode node, DiagnosticContext context)
        {
            var templateDeclaration = node.Cast<IVariableDeclaration>();

            if (!templateDeclaration.IsTemplateDeclaration())
            {
                return;
            }

            // Template should have a non-any type
            var type = templateDeclaration.Type;
            if (type == null)
            {
                context.Logger.TemplateDeclarationShouldHaveAType(
                    context.LoggingContext,
                    templateDeclaration.LocationForLogging(context.SourceFile));
                return;
            }

            if (type.Kind == TypeScript.Net.Types.SyntaxKind.AnyKeyword)
            {
                context.Logger.TemplateDeclarationShouldNotHaveAnyType(
                    context.LoggingContext,
                    templateDeclaration.LocationForLogging(context.SourceFile));
            }
        }
    }
}
