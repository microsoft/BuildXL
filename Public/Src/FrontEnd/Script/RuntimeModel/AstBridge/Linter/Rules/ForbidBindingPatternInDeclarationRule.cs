// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that variable declarations don't use a binding pattern like const {v1, v2} = {v1: "hi", "v2" : "bye"};
    /// </summary>
    /// <remarks>
    /// There is nothing fundamentally wrong with this pattern, it is just that we don't support this easily during AST conversion. So this 
    /// is now blocked but it might be supported in the future.
    /// </remarks>
    internal sealed class ForbidBindingPatternInDeclarationRule : LanguageRule
    {
        private ForbidBindingPatternInDeclarationRule()
        { }

        public static ForbidBindingPatternInDeclarationRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidBindingPatternInDeclarationRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckVariableDeclarationIsNotAllowed,
                TypeScript.Net.Types.SyntaxKind.VariableDeclaration);
        }

        private static void CheckVariableDeclarationIsNotAllowed(INode node, DiagnosticContext context)
        {
            var declaration = node.As<IVariableDeclaration>();

            if (declaration.Name.As<IBindingPattern>() != null)
            {
                context.Logger.ReportBindingPatternInVariableDeclarationIsNowAllowed(context.LoggingContext, node.LocationForLogging(context.SourceFile), declaration.Name.GetText());
            }
        }
    }
}
