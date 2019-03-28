// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Rule for inline importFile(...), importFrom(...) in spec files
    /// </summary>
    internal sealed class EnforceWellShapedInlineImportRule : EnforceWellShapedInlineImportBase
    {
        private EnforceWellShapedInlineImportRule()
            : base()
        {
        }

        public static EnforceWellShapedInlineImportRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceWellShapedInlineImportRule();
            result.Initialize(context);
            return result;
        }

        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        /// <summary>
        /// Validates that the module specifier of the inline import is a string literal.
        /// </summary>
        protected override void ValidateImportFrom(IExpression argument, ILiteralExpression stringLiteral, DiagnosticContext context)
        {
            if (stringLiteral == null || stringLiteral.As<IStringLiteral>()?.LiteralKind == LiteralExpressionKind.None)
            {
                // Must pass a string literal to importFrom
                context.Logger.ReportImportFromNotPassedAStringLiteral(
                    context.LoggingContext,
                    argument.LocationForLogging(context.SourceFile),
                    argument.GetFormattedText());
            }

            // V2 validation
            if (context.Workspace?.SpecBelongsToImplicitSemanticsModule(context.SourceFile.GetAbsolutePath(context.PathTable)) ?? false)
            {
                string text = stringLiteral?.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    if (!ImportPathHelpers.IsPackageName(text))
                    {
                        // add error because importFrom must be passed package names
                        context.Logger.ReportImportFromV2Package(
                            context.LoggingContext,
                            argument.LocationForLogging(context.SourceFile),
                            argument.GetFormattedText());
                    }
                    else if (text.IndexOfAny(ImportPathHelpers.InvalidPackageChars) != -1)
                    {
                        context.Logger.ReportNamedImportInConfigOrPackageLikePath(
                            context.LoggingContext,
                            argument.LocationForLogging(context.SourceFile),
                            Names.InlineImportFunction,
                            argument.GetFormattedText());
                    }
                }
            }
        }

        /// <summary>
        /// Validates that importFile is not used within a spec file
        /// </summary>
        protected override void DoValidateImportFile(IExpression argument, ILiteralExpression stringLiteral, DiagnosticContext context)
        {
            // Cannot use importFile in spec
            context.Logger.ReportImportFileInSpec(
                context.LoggingContext,
                argument.LocationForLogging(context.SourceFile));
        }
    }
}
