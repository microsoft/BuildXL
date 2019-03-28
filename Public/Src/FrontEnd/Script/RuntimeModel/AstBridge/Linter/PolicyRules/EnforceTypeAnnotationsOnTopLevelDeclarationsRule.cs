// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Rule that enforces type annotations on top level declarations. 'any' type is also forbidden.
    /// </summary>
    internal sealed class EnforceTypeAnnotationsOnTopLevelDeclarationsRule : PolicyRule
    {
        /// <nodoc/>
        public override string Name => "RequireTypeAnnotationsOnDeclarations";

        /// <nodoc/>
        public override string Description => "Type annotations are required for all top level declarations ('any' is not allowed either).";

        public override void Initialize(AnalysisContext context)
        {
            // This rule assumes that AST is bound and Parent is presented.
            context.RegisterSyntaxNodeAction(
                this,
                AnalyzeVariableStatement,
                TypeScript.Net.Types.SyntaxKind.VariableStatement);
        }

        private void AnalyzeVariableStatement(INode node, DiagnosticContext context)
        {
            var variableStatement = node.Cast<IVariableStatement>();

            // Don't have to analyze non-top level declarations.
            if (!variableStatement.IsTopLevelOrNamespaceLevelDeclaration())
            {
                return;
            }

            var declarations = variableStatement.DeclarationList.Declarations;
            foreach (var declaration in declarations)
            {
                if (declaration.Type == null)
                {
                    context.Logger.ReportMissingTypeAnnotationOnTopLevelDeclaration(
                        context.LoggingContext,
                        declaration.LocationForLogging(context.SourceFile),
                        declaration.Name.GetText(),
                        Name);
                }
                else if (declaration.Type.Kind == TypeScript.Net.Types.SyntaxKind.AnyKeyword)
                {
                    context.Logger.ReportNotAllowedTypeAnyOnTopLevelDeclaration(
                        context.LoggingContext,
                        declaration.LocationForLogging(context.SourceFile),
                        declaration.Name.GetText(),
                        Name);
                }
            }
        }
    }
}
