// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that classes, class expressions and new expressions are not used in DScript codebase.
    /// </summary>
    /// <remarks>
    /// TypeScript has following features:
    /// 1. Class declarations:
    /// <code>class Foo extends Bar { }</code>
    /// 2. Class expressions:
    /// <code>let x = class {x: number; }</code>
    /// 3. New expressions for instantiating class instances
    /// <code>let x = new Foo();</code>
    ///
    /// All those features are not supported in DScript and this rule enforces this decision.
    /// </remarks>
    internal sealed class ForbidClassRule : LanguageRule
    {
        private ForbidClassRule()
        { }

        public static ForbidClassRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidClassRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(this, CheckClassDeclaration, TypeScript.Net.Types.SyntaxKind.ClassDeclaration);
            context.RegisterSyntaxNodeAction(this, CheckClassExpression, TypeScript.Net.Types.SyntaxKind.ClassExpression);
            context.RegisterSyntaxNodeAction(this, CheckNewExpression, TypeScript.Net.Types.SyntaxKind.NewExpression);
        }

        private static void CheckClassDeclaration(INode node, DiagnosticContext context)
        {
            context.Logger.ReportNotSupportedClassDeclaration(context.LoggingContext, node.LocationForLogging(context.SourceFile));
        }

        private static void CheckClassExpression(INode node, DiagnosticContext context)
        {
            context.Logger.ReportNotSupportedClassExpression(context.LoggingContext, node.LocationForLogging(context.SourceFile));
        }

        private static void CheckNewExpression(INode node, DiagnosticContext context)
        {
            context.Logger.ReportNotSupportedNewExpression(context.LoggingContext, node.LocationForLogging(context.SourceFile));
        }
    }
}
