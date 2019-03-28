// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Rule that enforces correct structure of a config.dsc file.
    /// </summary>
    internal sealed class EnforceRootConfigStuctureRule : LanguageRule
    {
        private EnforceRootConfigStuctureRule()
        { }

        public static EnforceRootConfigStuctureRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceRootConfigStuctureRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(this, AnalyzeSource,
                TypeScript.Net.Types.SyntaxKind.SourceFile);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.RootConfig;

        private static void AnalyzeSource(INode node, DiagnosticContext context)
        {
            ConfigurationConverter.ValidateRootConfiguration(context.SourceFile, context.Logger, context.LoggingContext);
        }
    }
}
