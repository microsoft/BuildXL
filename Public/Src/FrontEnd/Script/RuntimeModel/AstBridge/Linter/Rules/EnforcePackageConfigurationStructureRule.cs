// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Rule that enforces correct structure of a config.dsc file.
    /// </summary>
    internal sealed class EnforcePackageConfigurationStructureRule : LanguageRule
    {
        private EnforcePackageConfigurationStructureRule()
        { }

        public static EnforcePackageConfigurationStructureRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforcePackageConfigurationStructureRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(this, AnalyzeSource,
                TypeScript.Net.Types.SyntaxKind.SourceFile);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.PackageConfig;

        private static void AnalyzeSource(INode node, DiagnosticContext context)
        {
            ConfigurationConverter.ValidatePackageConfiguration(context.SourceFile, context.Logger, context.LoggingContext);
        }
    }
}
