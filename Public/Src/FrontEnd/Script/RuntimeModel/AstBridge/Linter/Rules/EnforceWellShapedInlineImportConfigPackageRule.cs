// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Util;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Rule for inline importFrom(...) and importFile(...) in configuration files
    /// </summary>
    internal sealed class EnforceWellShapedInlineImportConfigPackageRule : EnforceWellShapedInlineImportBase
    {
        private static readonly string[] s_invalidPathNames = Names.WellKnownConfigFileNames;

        private EnforceWellShapedInlineImportConfigPackageRule()
            : base()
        {
        }

        public static EnforceWellShapedInlineImportConfigPackageRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceWellShapedInlineImportConfigPackageRule();
            result.Initialize(context);
            return result;
        }

        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.PackageConfig | RuleAnalysisScope.RootConfig | RuleAnalysisScope.BuildListFile;

        /// <summary>
        /// Validates that importFrom is used in package and root config files in V1, where the argument passed to importFrom is not a package name.
        /// </summary>
        protected override void ValidateImportFrom(IExpression argument, ILiteralExpression stringLiteral, DiagnosticContext context)
        {
            string text = stringLiteral?.Text;
            if (!string.IsNullOrEmpty(text))
            {
                if (ImportPathHelpers.IsPackageName(text))
                {
                    // add error because importFrom must be passed file names, not package names
                    context.Logger.ReportNamedImportInConfigOrPackage(
                        context.LoggingContext,
                        argument.LocationForLogging(context.SourceFile),
                        Names.InlineImportFunction,
                        argument.GetFormattedText());
                }

                CheckForFilesThatExposeNothing(text, argument, Names.InlineImportFunction, context);
            }
        }

        /// <summary>
        /// Validates that importFile is always passed a file literal
        /// </summary>
        protected override void DoValidateImportFile(IExpression argument, ILiteralExpression stringLiteral, DiagnosticContext context)
        {
            (var interpolationKind, ILiteralExpression literal, _, _) = argument.As<ITaggedTemplateExpression>();

            if (interpolationKind != InterpolationKind.FileInterpolation || literal == null)
            {
                // Must pass a file path literal to importFile
                context.Logger.ReportImportFileNotPassedAFileLiteral(
                    context.LoggingContext,
                    argument.LocationForLogging(context.SourceFile),
                    argument.GetFormattedText());
            }

            CheckForFilesThatExposeNothing(literal?.Text, argument, Names.InlineImportFileFunction, context);
        }

        /// <summary>
        /// Check that the imported file is not a file that cannot expose anything (e.g. package.config.dsc, config.dsc, etc).
        /// </summary>
        private static void CheckForFilesThatExposeNothing(string text, INode node, string functionName, DiagnosticContext context)
        {
            if (s_invalidPathNames.Any(path => text?.EndsWith(path, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                // add error because these files cannot expose anything
                context.Logger.ReportNamedImportOfConfigPackageModule(
                    context.LoggingContext,
                    node.LocationForLogging(context.SourceFile),
                    functionName,
                    node.GetFormattedText());
            }
        }
    }
}
