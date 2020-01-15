// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that:
    /// - Only p`, f` and d` expressions are allowed interpolation expressions. We don't support string interpolation yet.
    /// </summary>
    internal sealed class EnforceValidInterpolationRule : LanguageRule
    {
        private EnforceValidInterpolationRule()
        {
        }

        public static EnforceValidInterpolationRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceValidInterpolationRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckStringInterpolationIsNotAllowed,
                TypeScript.Net.Types.SyntaxKind.TaggedTemplateExpression);
        }

        private static void CheckStringInterpolationIsNotAllowed(INode node, DiagnosticContext context)
        {
            // We only support p`, f` and d` interpolation.
            (var interpolationKind, ILiteralExpression literal, _, _) = node.Cast<ITaggedTemplateExpression>();

            if (interpolationKind == InterpolationKind.Unknown)
            {
                string interpolationFunction = literal?.Text ?? node.ToDisplayString();
                context.Logger.ReportNotSupportedInterpolation(context.LoggingContext, node.LocationForLogging(context.SourceFile), interpolationFunction);
            }
        }
    }
}
