// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that:
    /// - For loops have a single initializer which has to be initialized (e.g. for (let x = 1; ...)
    /// - For incrementor has to be an assignment expression of the form 'identifier = expression', 'identifier += expression' or 'identifier -= expression'
    /// </summary>
    internal sealed class EnforceSimplifiedForRule : LanguageRule
    {
        private static readonly TypeScript.Net.Types.SyntaxKind[] s_assignmentTokens = new TypeScript.Net.Types.SyntaxKind[]
                                                                               {
                                                                                           TypeScript.Net.Types.SyntaxKind.EqualsToken,
                                                                                           TypeScript.Net.Types.SyntaxKind.PlusEqualsToken,
                                                                                           TypeScript.Net.Types.SyntaxKind.MinusEqualsToken,
                                                                                           TypeScript.Net.Types.SyntaxKind.MinusMinusToken,
                                                                                           TypeScript.Net.Types.SyntaxKind.PlusPlusToken,
                                                                               };

        private EnforceSimplifiedForRule()
        { }

        public static EnforceSimplifiedForRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceSimplifiedForRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckSingleDeclarationInitializer,
                TypeScript.Net.Types.SyntaxKind.ForStatement);

            context.RegisterSyntaxNodeAction(
                this,
                CheckIncrementorIsAssignmentOrPostfixIncrementOrDecrementExpression,
                TypeScript.Net.Types.SyntaxKind.ForStatement);
        }

        private static void CheckSingleDeclarationInitializer(INode node, DiagnosticContext context)
        {
            // 'For' statements must have an initializer with a single declaration that has to be initialized
            var forStatement = node.Cast<ForStatement>();
            var variableDeclarationList = forStatement.Initializer?.AsVariableDeclarationList();
            if (variableDeclarationList == null || variableDeclarationList.Declarations.Count != 1)
            {
                var targetNode = (INode)variableDeclarationList ?? forStatement;
                context.Logger.ReportInvalidForVarDeclarationInitializer(context.LoggingContext, targetNode.LocationForLogging(context.SourceFile));
                return;
            }

            // The only declaration that is there
            var declaration = variableDeclarationList.Declarations[0];

            if (declaration.Initializer == null)
            {
                context.Logger.ReportVariableMustBeInitialized(context.LoggingContext, declaration.LocationForLogging(context.SourceFile), declaration.Name.GetText());
            }
        }

        // Incrementor has to be an assignment expression
        private static void CheckIncrementorIsAssignmentOrPostfixIncrementOrDecrementExpression(INode node, DiagnosticContext context)
        {
            var incrementor = node.Cast<ForStatement>().Incrementor;
            var binaryIncrementor = incrementor.As<IBinaryExpression>();
            if (binaryIncrementor == null)
            {
                var unaryIncrementor = incrementor.As<IPostfixUnaryExpression>();
                if (unaryIncrementor == null || !s_assignmentTokens.Contains(unaryIncrementor.Operator))
                {
                    context.Logger.ReportForIncrementorMustBeAssignmentOrPostfixIncrementOrDecrement(context.LoggingContext, (incrementor ?? node).LocationForLogging(context.SourceFile));
                }
            }
            else if (!s_assignmentTokens.Contains(binaryIncrementor.OperatorToken.Kind))
            {
                context.Logger.ReportForIncrementorMustBeAssignmentOrPostfixIncrementOrDecrement(context.LoggingContext, incrementor.LocationForLogging(context.SourceFile));
            }
        }
    }
}
