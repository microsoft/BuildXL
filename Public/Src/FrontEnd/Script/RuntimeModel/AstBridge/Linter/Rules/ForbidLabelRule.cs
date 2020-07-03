// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that labeled statements are not allowed.
    /// </summary>
    internal sealed class ForbidLabelRule : LanguageRule
    {
        private ForbidLabelRule()
        { }

        public static ForbidLabelRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidLabelRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckLabelIsNotAllowed,
                TypeScript.Net.Types.SyntaxKind.LabeledStatement);
        }

        private static void CheckLabelIsNotAllowed(INode node, DiagnosticContext context)
        {
            context.Logger.ReportLabelsAreNotAllowed(context.LoggingContext, node.LocationForLogging(context.SourceFile));
        }
    }
}
