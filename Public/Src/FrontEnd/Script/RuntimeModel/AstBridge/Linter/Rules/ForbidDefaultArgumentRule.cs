// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that parameters shouldn't have initializers. Defaults are not supported.
    /// </summary>
    internal sealed class ForbidDefaultArgumentRule : LanguageRule
    {
        private ForbidDefaultArgumentRule()
        { }

        public static ForbidDefaultArgumentRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidDefaultArgumentRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckInitializerIsNull,
                TypeScript.Net.Types.SyntaxKind.Parameter);
        }

        private static void CheckInitializerIsNull(INode node, DiagnosticContext context)
        {
            // Parameters must not have initializers, defaults are not supported
            var parameter = node.Cast<ParameterDeclaration>();

            if (parameter.Initializer != null)
            {
                context.Logger.ReportNotSupportedDefaultArguments(context.LoggingContext, parameter.Initializer.LocationForLogging(context.SourceFile));
            }
        }
    }
}
