// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Rule that enforces that:
    /// - 'any' is not allowed as a type
    /// - All functions need to declare a return type
    /// - All top level exported assignments from object literals (e.g. let a = {foo: "foo"};) require the variable to have a type annotations
    ///
    /// As a result, the type checker has better chances to be able to infer the types of all top level values.
    /// This is still not perfect (when we have a real type checker it should be way easier), but it prevents common pitfalls.
    /// </summary>
    internal sealed class EnforceSomeTypeSanityRule : PolicyRule
    {
        /// <nodoc/>
        public override string Name => "EnforceSomeTypeSanity";

        /// <nodoc/>
        public override string Description => "Enforces that functions declare return values and make sure object literal assignments on exported top level values are typed.";

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(this, EnforceFunctionDeclareReturnType, TypeScript.Net.Types.SyntaxKind.FunctionDeclaration);

            context.RegisterSyntaxNodeAction(
                this,
                AnalyzeVariableStatement,
                TypeScript.Net.Types.SyntaxKind.VariableStatement);
        }

        private void EnforceFunctionDeclareReturnType(INode node, DiagnosticContext context)
        {
            var functionDeclaration = node.As<IFunctionDeclaration>();
            if (functionDeclaration.Type == null || functionDeclaration.Type.Kind == TypeScript.Net.Types.SyntaxKind.AnyKeyword)
            {
                context.Logger.ReportFunctionShouldDeclareReturnType(
                    context.LoggingContext,
                    functionDeclaration.LocationForLogging(context.SourceFile),
                    functionDeclaration.Name.GetFormattedText(),
                    Name);
            }
        }

        private void AnalyzeVariableStatement(INode node, DiagnosticContext context)
        {
            var variableStatement = node.Cast<IVariableStatement>();

            // Don't need to check whether a statement was declared at the namespace level,
            // because we'll skip non-exported variable declarations.
            // And export keyword is applicable only for top level variables!

            // We only care about exported statements
            if ((variableStatement.Flags & NodeFlags.Export) == 0)
            {
                return;
            }

            var declarations = variableStatement.DeclarationList.Declarations;
            foreach (var declaration in declarations)
            {
                // 'any' is not allowed for top level variables
                if (declaration.Type?.Kind == TypeScript.Net.Types.SyntaxKind.AnyKeyword
                    || NodeWalker.ForEachChildRecursively<Bool>(declaration.Type, n => n?.Kind == TypeScript.Net.Types.SyntaxKind.AnyKeyword))
                {
                    context.Logger.ReportNotAllowedTypeAnyOnTopLevelDeclaration(
                        context.LoggingContext,
                        declaration.LocationForLogging(context.SourceFile),
                        declaration.Name.GetFormattedText(),
                        Name);
                }

                // If the variable has an initializer (there is another lint rule that enforces variable initialization,
                // but that can't be assumed here) that is an object literal, then there should be a non-any type annotation
                if (declaration.Initializer?.Kind == TypeScript.Net.Types.SyntaxKind.ObjectLiteralExpression)
                {
                    if (declaration.Type == null)
                    {
                        context.Logger.ReportMissingTypeAnnotationOnTopLevelDeclaration(
                            context.LoggingContext,
                            declaration.LocationForLogging(context.SourceFile),
                            declaration.Name.GetFormattedText(),
                            Name);
                    }
                    else if (declaration.Type.Kind == TypeScript.Net.Types.SyntaxKind.AnyKeyword)
                    {
                        context.Logger.ReportNotAllowedTypeAnyOnTopLevelDeclaration(
                            context.LoggingContext,
                            declaration.LocationForLogging(context.SourceFile),
                            declaration.Name.GetFormattedText(),
                            Name);
                    }
                }
            }
        }
    }
}
