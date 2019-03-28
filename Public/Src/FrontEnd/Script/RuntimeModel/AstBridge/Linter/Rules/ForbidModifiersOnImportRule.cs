// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that import declarations don't have modifiers.
    /// </summary>
    /// <remarks>This includes the case of 'export import', and in that case we emit a warning since it's a legacy feature.</remarks>
    internal sealed class ForbidModifiersOnImportRule : LanguageRule
    {
        private ForbidModifiersOnImportRule()
        { }

        public static ForbidModifiersOnImportRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidModifiersOnImportRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckModifiersOnImportsAreNotAllowed,
                TypeScript.Net.Types.SyntaxKind.ImportDeclaration);
        }

        private static void CheckModifiersOnImportsAreNotAllowed(INode node, DiagnosticContext context)
        {
            var importDeclaration = node.As<IImportDeclaration>();
            if (importDeclaration.Modifiers == null || importDeclaration.Modifiers.Count == 0)
            {
                return;
            }

            foreach (var modifier in importDeclaration.Modifiers)
            {
                if (modifier.Kind == TypeScript.Net.Types.SyntaxKind.ExportKeyword)
                {
                    // This is a warning
                    context.Logger.ReportNotSupportedExportImport(context.LoggingContext, modifier.LocationForLogging(context.SourceFile));
                }
                else
                {
                    context.Logger.ReportNotSupportedModifiersOnImport(context.LoggingContext, modifier.LocationForLogging(context.SourceFile), modifier.GetFormattedText());
                }
            }
        }
    }
}
