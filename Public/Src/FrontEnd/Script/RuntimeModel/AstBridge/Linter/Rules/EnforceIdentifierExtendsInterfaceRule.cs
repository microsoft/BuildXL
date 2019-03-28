// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that:
    /// - only 'extends' is allowed, since we don't allow classes
    /// - only an identifier is allowed to extend an interface (e.g. interface T extends T1 {...}). TypeScript allows inlining an object literal to extend an interface.
    /// </summary>
    internal sealed class EnforceIdentifierExtendsInterfaceRule : LanguagePolicyRule
    {
        private EnforceIdentifierExtendsInterfaceRule()
        { }

        public static EnforceIdentifierExtendsInterfaceRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceIdentifierExtendsInterfaceRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(this, CheckIsExtends,
                TypeScript.Net.Types.SyntaxKind.HeritageClause);
            context.RegisterSyntaxNodeAction(this, CheckOnlyAllowIdentifier,
                TypeScript.Net.Types.SyntaxKind.HeritageClause);
        }

        private static void CheckIsExtends(INode node, DiagnosticContext context)
        {
            // DScript only supports 'extends' on interfaces, since classes are not supported
            var heritageClause = node.Cast<IHeritageClause>();

            if (heritageClause.Token != TypeScript.Net.Types.SyntaxKind.ExtendsKeyword)
            {
                context.Logger.ReportOnlyExtendsClauseIsAllowedInHeritageClause(context.LoggingContext, node.LocationForLogging(context.SourceFile));
            }
        }

        private static void CheckOnlyAllowIdentifier(INode node, DiagnosticContext context)
        {
            // DScript only supports dotted identifiers (PropertyAccessExpression) or simple identifier (Identifier) in heritage clauses
            // So something like
            // interface T extends {x: number} { }
            // is not allowed
            var heritageClause = node.Cast<IHeritageClause>();

            foreach (var type in heritageClause.Types)
            {
                var propAccess = type.Expression.As<IPropertyAccessExpression>();
                if (propAccess == null)
                {
                    var ident = type.Expression.As<IIdentifier>();
                    if (ident == null)
                    {
                        context.Logger.ReportInterfacesOnlyExtendedByIdentifiers(context.LoggingContext, node.LocationForLogging(context.SourceFile), type.Expression.GetFormattedText());
                    }
                }
            }
        }
    }
}
