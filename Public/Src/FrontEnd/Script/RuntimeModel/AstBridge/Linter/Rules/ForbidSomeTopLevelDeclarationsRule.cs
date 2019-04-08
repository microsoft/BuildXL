// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using TypeScript.Net.Types;
using TS = TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// DScript adds additional restriction on set of constructs that could be used directly inside namespace declaration and top level
    /// Following list of constructs are permitted:
    /// - Type Declarations(interfaces, type aliases).
    /// - functions
    /// - let/const bindings
    /// - IIFEs
    ///
    /// Here is a set of constructs that should be explicitely prohibited to use:
    /// - Functions applications without let/const bindings, like 'Console.writeLine("foo");'
    /// - loops
    /// - if blocks
    /// </summary>
    internal sealed class ForbidSomeTopLevelDeclarationsRule : LanguageRule
    {
        private ForbidSomeTopLevelDeclarationsRule()
        { }

        public static ForbidSomeTopLevelDeclarationsRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidSomeTopLevelDeclarationsRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                ValidateExpression,
                TS.SyntaxKind.ExpressionStatement);

            context.RegisterSyntaxNodeAction(
                this,
                ValidateStatement,
                TS.SyntaxKind.ForStatement,
                TS.SyntaxKind.ForOfStatement,
                TS.SyntaxKind.DoStatement,
                TS.SyntaxKind.WhileStatement,
                TS.SyntaxKind.IfStatement,
                TS.SyntaxKind.SwitchStatement,
                TS.SyntaxKind.TryStatement);
        }

        private static void ValidateStatement(INode node, DiagnosticContext context)
        {
            if (!node.IsTopLevelOrNamespaceLevelDeclaration())
            {
                // Doing nothing if the declaration is not a namespace or top level declaration.
                return;
            }

            context.Logger.ReportOnlyTypeAndFunctionDeclarationsAndConstBindingsAreAllowedTopLevel(
                    context.LoggingContext,
                    node.LocationForLogging(context.SourceFile),
                    node.Kind.ToString());
        }

        private static void ValidateExpression(INode node, DiagnosticContext context)
        {
            if (!node.IsTopLevelOrNamespaceLevelDeclaration())
            {
                // Doing nothing if the declaration is not a namespace or top level declaration.
                return;
            }

            var expression = node.As<IExpressionStatement>()?.Expression;

            // If it is not an expression node, then it is for sure not allowed.
            // Many of the registered node kinds are not expressions, but in this case As<IExpressionStatemt>() just return null and we're good.
            if (expression == null)
            {
                context.Logger.ReportOnlyTypeAndFunctionDeclarationsAndConstBindingsAreAllowedTopLevel(
                    context.LoggingContext,
                    node.LocationForLogging(context.SourceFile),
                    node.Kind.ToString());
            }
            else
            {
                // The only case for an expression to be allowed, is when it is a config or package configuration call
                // There is another rule that enforces that those functions are used in appropriate file types.
                if (expression.Kind != TS.SyntaxKind.CallExpression ||
                    !IsAllowedTopLevelFunctionCall(expression.Cast<ICallExpression>()))
                {
                    context.Logger.ReportFunctionApplicationsWithoutConstLetBindingAreNotAllowedTopLevel(
                        context.LoggingContext,
                        expression.LocationForLogging(context.SourceFile));
                }
            }
        }

        /// <summary>
        /// Return true iff callExpression is a call to 'config' ('configure' as well for compat reasons), 'package' or 'qualifierSpace' the only allowed top-level function calls.
        /// </summary>
        private static bool IsAllowedTopLevelFunctionCall(ICallExpression callExpression)
        {
            // Call expression should be at the top level to be considered valid.
            // Namespace level calls to predefine functions are invalid.

            // Need to check parent's parent, because for ICallExpression Parent is ExpressionStaement but not a SourceFile directly.
            if (callExpression.Parent.IsTopLevelDeclaration())
            {
                var expressionName = callExpression.Expression.As<IIdentifier>();
                if (expressionName != null)
                {
                    return expressionName.Text == Names.ConfigurationFunctionCall ||
                            expressionName.Text == Names.LegacyModuleConfigurationFunctionCall ||
                            expressionName.Text == Names.ModuleConfigurationFunctionCall;
                }
            }

            return false;
        }
    }
}
