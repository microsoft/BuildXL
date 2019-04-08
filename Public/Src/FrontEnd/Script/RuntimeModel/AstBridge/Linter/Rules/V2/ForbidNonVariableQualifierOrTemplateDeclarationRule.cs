// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks neither 'qualifier' nor 'template' is used in declarations other than variable declarations.
    /// </summary>
    /// <remarks>
    /// This rules prevents that if an identifier 'qualifier' or 'template' is introduced, it is always a variable declaration.
    /// That the qualifier variable declaration is well shaped is checked by EnforceQualifierDeclarationRule
    /// That the template variable declaration is well shaped is checked by EnforceTemplateDeclarationRule
    /// </remarks>
    internal sealed class ForbidNonVariableQualifierOrTemplateDeclarationRule : LanguageRule
    {
        private ForbidNonVariableQualifierOrTemplateDeclarationRule()
        { }

        public static ForbidNonVariableQualifierOrTemplateDeclarationRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidNonVariableQualifierOrTemplateDeclarationRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            // Note that type-related declarations are ok. The context of type expressions is always univocally identified.
            // Class declarations are not registered since they are already forbidden by ForbidClassRule
            // Namespace imports and import specifiers are not registered since they can only be top-level, and therefore they will
            // clash with the always present top level qualifier or template declaration
            // Interface, enum and namespace declaration that use 'qualifier' or 'template' will be rejected by the casing rule, but that rule is going to
            // be eventually relaxed, so we include them here anyway
            context.RegisterSyntaxNodeAction(
                this,
                CheckQualifierOrTemplateNameIsNotIntroduced,
                TypeScript.Net.Types.SyntaxKind.InterfaceDeclaration,
                TypeScript.Net.Types.SyntaxKind.EnumDeclaration,
                TypeScript.Net.Types.SyntaxKind.EnumMember,
                TypeScript.Net.Types.SyntaxKind.FunctionDeclaration,
                TypeScript.Net.Types.SyntaxKind.ModuleDeclaration,
                TypeScript.Net.Types.SyntaxKind.Parameter);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckQualifierOrTemplateNameIsNotIntroduced(INode node, DiagnosticContext context)
        {
            var declaration = node.As<IDeclaration>();

            var text = declaration?.Name.Text;
            switch (text)
            {
                case Names.CurrentQualifier:
                    context.Logger.ReportQualifierNameCanOnlyBeUsedInVariableDeclarations(
                        context.LoggingContext,
                        node.LocationForLogging(context.SourceFile),
                        Names.CurrentQualifier);
                    return;
                case Names.Template:
                    context.Logger.ReportTemplateNameCanOnlyBeUsedInVariableDeclarations(
                        context.LoggingContext,
                        node.LocationForLogging(context.SourceFile),
                        Names.Template);
                    break;
            }
        }
    }
}
