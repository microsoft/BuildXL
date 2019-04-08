// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that method declarations are not allowed inside object literals
    /// E.g. {func1(x: number): number;} is not allowed
    /// We could support this in the future, but there is support in the interpeter for this yet.
    /// </summary>
    internal sealed class ForbidMethodDeclarationInObjectLiteralRule : LanguageRule
    {
        private ForbidMethodDeclarationInObjectLiteralRule()
        { }

        public static ForbidMethodDeclarationInObjectLiteralRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidMethodDeclarationInObjectLiteralRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckLiteralElementIsNotMethodDeclaration,
                TypeScript.Net.Types.SyntaxKind.ObjectLiteralExpression);
        }

        private static void CheckLiteralElementIsNotMethodDeclaration(INode node, DiagnosticContext context)
        {
            // Method declarations are not allowed inside object literals.
            // E.g. {func1(x: number): number;}
            var objectLiteralExpression = node.Cast<IObjectLiteralExpression>();
            if (objectLiteralExpression.Properties == null)
            {
                return;
            }

            foreach (var literalMember in objectLiteralExpression.Properties)
            {
                if (literalMember.Kind == TypeScript.Net.Types.SyntaxKind.MethodDeclaration)
                {
                    context.Logger.ReportNotSupportedMethodDeclarationInEnumMember(context.LoggingContext, literalMember.LocationForLogging(context.SourceFile), literalMember.GetFormattedText());
                }
            }
        }
    }
}
