// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that for-of has a single initializer (e.g. for (let x of [1,2]) )
    /// And that the initializer is NOT initialized
    /// </summary>
    internal sealed class EnforceSingleDeclarationInForOfRule : LanguageRule
    {
        private EnforceSingleDeclarationInForOfRule()
        { }

        public static EnforceSingleDeclarationInForOfRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceSingleDeclarationInForOfRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(this, CheckSingleDeclarationInitializer,
                TypeScript.Net.Types.SyntaxKind.ForOfStatement);
        }

        private static void CheckSingleDeclarationInitializer(INode node, DiagnosticContext context)
        {
            // For-of must have an initializer with a single declaration, which doesn't have to be initialized
            var forOfStatement = node.Cast<ForOfStatement>();
            var variableDeclarationList = forOfStatement.Initializer.AsVariableDeclarationList();
            if (variableDeclarationList == null || variableDeclarationList.Declarations.Count != 1)
            {
                context.Logger.ReportInvalidForOfVarDeclarationInitializer(context.LoggingContext, variableDeclarationList.LocationForLogging(context.SourceFile));
            }
        }
    }
}
