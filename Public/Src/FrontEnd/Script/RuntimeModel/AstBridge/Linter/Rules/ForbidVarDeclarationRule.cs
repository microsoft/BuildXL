// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that variable declarations are not there. Only 'let' and 'const' are allowed.
    /// </summary>
    internal sealed class ForbidVarDeclarationRule : LanguageRule
    {
        private ForbidVarDeclarationRule()
        { }

        public static ForbidVarDeclarationRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidVarDeclarationRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckVariableDeclarationIsNotAllowed,
                TypeScript.Net.Types.SyntaxKind.VariableDeclarationList);
        }

        private static void CheckVariableDeclarationIsNotAllowed(INode node, DiagnosticContext context)
        {
            var declarationList = node.As<IVariableDeclarationList>();
            var hasVar = (declarationList.Flags & NodeFlags.BlockScoped) == 0;

            if (hasVar)
            {
                context.Logger.ReportVarDeclarationNotAllowed(context.LoggingContext, node.LocationForLogging(context.SourceFile));
            }
        }
    }
}
