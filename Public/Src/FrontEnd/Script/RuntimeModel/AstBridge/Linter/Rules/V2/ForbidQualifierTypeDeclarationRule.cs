// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Prevents a type declaration with name 'Qualifier'.
    /// </summary>
    /// <remarks>
    /// There is already such declaration in the prelude, so this rule relies on the fact that the prelude is not actually run through the linter
    /// </remarks>
    internal sealed class ForbidQualifierTypeDeclarationRule : LanguageRule
    {
        private ForbidQualifierTypeDeclarationRule()
        {
        }

        public static ForbidQualifierTypeDeclarationRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidQualifierTypeDeclarationRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckQualifierTypeNameIsNotUsed,
                TypeScript.Net.Types.SyntaxKind.InterfaceDeclaration,
                TypeScript.Net.Types.SyntaxKind.TypeAliasDeclaration);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckQualifierTypeNameIsNotUsed(INode node, DiagnosticContext context)
        {
            var interfaceDeclaration = node.As<IInterfaceDeclaration>();
            var name = interfaceDeclaration != null ? interfaceDeclaration.Name.Text : node.As<ITypeAliasDeclaration>().Name.Text;

            if (name == Names.BaseQualifierType)
            {
                context.Logger.ReportQualifierTypeNameIsReserved(
                    context.LoggingContext,
                    node.LocationForLogging(context.SourceFile),
                    Names.BaseQualifierType);
            }
        }
    }
}
