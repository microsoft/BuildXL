// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that a path specifier in an import or exportdeclaration:
    /// - Should only take a string literal for the 'from' clause. This includes double quote, single quote and backtick quote strings (the latter with no interpolation).
    /// - If it is an import declaration, the path specifier needs to be always present
    /// </summary>
    internal sealed class EnforceImportOrExportFromStringLiteralRule : LanguageRule
    {
        private EnforceImportOrExportFromStringLiteralRule()
        { }

        public static EnforceImportOrExportFromStringLiteralRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceImportOrExportFromStringLiteralRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckModuleSpecifierIsStringLiteral,
                TypeScript.Net.Types.SyntaxKind.ImportDeclaration,
                TypeScript.Net.Types.SyntaxKind.ExportDeclaration);
        }

        private static void CheckModuleSpecifierIsStringLiteral(INode node, DiagnosticContext context)
        {
            // Import declaration should have a literal as import path
            // TypeScript spec import specifier should be a string literal, but parser actually allows arbitrary expression there.
            // This rule makes sure that only `import * as x from "foo"` are allowed (or 'foo' or `foo`) an any other expressions after `from` are prohibited.
            IExpression moduleSpecifier;
            var exportDeclaration = node.As<IExportDeclaration>();
            if (exportDeclaration != null)
            {
                // The module specifier can be null in an export declaration (e.g. export {name};)
                if (exportDeclaration.ModuleSpecifier == null)
                {
                    return;
                }

                moduleSpecifier = exportDeclaration.ModuleSpecifier;
            }
            else
            {
                moduleSpecifier = node.As<IImportDeclaration>()?.ModuleSpecifier;
            }

            var literalExpression = moduleSpecifier.As<IStringLiteral>();

            if (literalExpression == null || literalExpression.LiteralKind == LiteralExpressionKind.None)
            {
                context.Logger.ReportImportModuleSpecifierIsNotAStringLiteral(
                    context.LoggingContext,
                    node.LocationForLogging(context.SourceFile),
                    moduleSpecifier.GetFormattedText());
            }
        }
    }
}
