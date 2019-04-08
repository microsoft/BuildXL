// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that only const-binding is used at the top (i.e., namespace) level.
    /// </summary>
    internal sealed class EnforceConstBindingOnTopLevel : LanguagePolicyRule
    {
        private EnforceConstBindingOnTopLevel()
        { }

        public static EnforceConstBindingOnTopLevel CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceConstBindingOnTopLevel();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            // This rule assumes that AST is bound and Parent is presented.
            context.RegisterSyntaxNodeAction(
                this,
                AnalyzeVariableStatement,
                TypeScript.Net.Types.SyntaxKind.VariableStatement);
        }

        private static void AnalyzeVariableStatement(INode node, DiagnosticContext context)
        {
            var statement = node.Cast<IVariableStatement>();
            if (statement.IsTopLevelOrNamespaceLevelDeclaration() && !NodeUtilities.IsConst(statement))
            {
                context.Logger.ReportOnlyConstBindingOnNamespaceLevel(
                    context.LoggingContext,
                    statement.LocationForLogging(context.SourceFile));
            }
        }
    }
}
