// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Rule for blocking introducing $ as a name
    /// </summary>
    internal sealed class EnforceWellShapedRootNamespace : LanguageRule
    {
        private EnforceWellShapedRootNamespace()
        {
        }

        public static EnforceWellShapedRootNamespace CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceWellShapedRootNamespace();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckDeclarationNameIsNotRootNamespace,
                TypeScript.Net.Types.SyntaxKind.ModuleDeclaration,
                TypeScript.Net.Types.SyntaxKind.EnumDeclaration,
                TypeScript.Net.Types.SyntaxKind.FunctionDeclaration,
                TypeScript.Net.Types.SyntaxKind.InterfaceDeclaration,
                TypeScript.Net.Types.SyntaxKind.TypeAliasDeclaration);

            context.RegisterSyntaxNodeAction(
                this,
                CheckImportSpecifierIsNotRootNamespace,
                TypeScript.Net.Types.SyntaxKind.ImportDeclaration);

            context.RegisterSyntaxNodeAction(
                this,
                CheckExportSpecifierIsNotRootNamespace,
                TypeScript.Net.Types.SyntaxKind.ExportDeclaration);
        }

        private static void CheckImportSpecifierIsNotRootNamespace(INode node, DiagnosticContext context)
        {
            var import = node.Cast<IImportDeclaration>();
            var clause = import.ImportClause;

            if (clause?.NamedBindings == null)
            {
                return;
            }

            // import * as $ from ...
            var namespaceImport = clause.NamedBindings.As<INamespaceImport>();
            if (namespaceImport != null)
            {
                ReportIfNameIsRootNamespace(namespaceImport.Name.Text, namespaceImport, context);
                return;
            }

            // import {A as $} from ...
            var namedImports = clause.NamedBindings.Cast<INamedImports>();
            foreach (var namedImport in namedImports.Elements)
            {
                if (ReportIfNameIsRootNamespace(namedImport.Name.Text, namedImport, context))
                {
                    return;
                }
            }
        }

        private static void CheckExportSpecifierIsNotRootNamespace(INode node, DiagnosticContext context)
        {
            var export = node.Cast<IExportDeclaration>();
            if (export.ExportClause != null)
            {
                // export {a as $}
                foreach (var namedExport in export.ExportClause.Elements)
                {
                    if (ReportIfNameIsRootNamespace(namedExport.Name.Text, namedExport, context))
                    {
                        return;
                    }
                }
            }
        }

        private static void CheckDeclarationNameIsNotRootNamespace(INode node, DiagnosticContext context)
        {
            var declaration = node.Cast<IDeclaration>();

            ReportIfNameIsRootNamespace(declaration.Name.Text, declaration, context);
        }

        private static bool ReportIfNameIsRootNamespace(string name, IDeclaration declaration, DiagnosticContext context)
        {
            if (name == Names.RootNamespace)
            {
                context.Logger.ReportRootNamespaceIsAKeyword(
                    context.LoggingContext, declaration.LocationForLogging(context.SourceFile), name);
                return true;
            }

            return false;
        }
    }
}
