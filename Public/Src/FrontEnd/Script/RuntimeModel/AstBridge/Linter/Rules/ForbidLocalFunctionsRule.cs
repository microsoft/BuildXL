// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that local functions are not allowed.
    /// </summary>
    internal sealed class ForbidLocalFunctionsRule : LanguagePolicyRule
    {
        private ForbidLocalFunctionsRule()
        { }

        public static ForbidLocalFunctionsRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidLocalFunctionsRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckFunctionDeclaration,
                TypeScript.Net.Types.SyntaxKind.FunctionDeclaration);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckFunctionDeclaration(INode node, DiagnosticContext context)
        {
            // This rule assumes that binding is completed.
            if (!node.IsTopLevelOrNamespaceLevelDeclaration())
            {
                context.Logger.ReportLocalFunctionsAreNotSupported(context.LoggingContext, node.LocationForLogging(context.SourceFile));
            }
        }
    }
}
