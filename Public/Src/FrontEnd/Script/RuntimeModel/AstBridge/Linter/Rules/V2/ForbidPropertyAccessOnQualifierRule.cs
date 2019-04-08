// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Prevents the current qualifier to be accessed in a qualified way (e.g. A.B.qualifier) .
    /// </summary>
    internal sealed class ForbidPropertyAccessOnQualifierRule : LanguageRule
    {
        private ForbidPropertyAccessOnQualifierRule()
        {
        }

        public static ForbidPropertyAccessOnQualifierRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidPropertyAccessOnQualifierRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckQualifierIsNotAccessWithQualifications,
                TypeScript.Net.Types.SyntaxKind.PropertyAccessExpression);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckQualifierIsNotAccessWithQualifications(INode node, DiagnosticContext context)
        {
            var propertyAccessExpression = node.As<IPropertyAccessExpression>();

            string name = null;
            if (propertyAccessExpression != null)
            {
                name = propertyAccessExpression.Name.Text;
            }

            // TODO: This is weird. It is actually a case where the kind of the node is PropertyAccessExpression but the node
            // cannot be casted to IPropertyAccessExpression (since ConciseBody implements IExpression, but cannot implement something more specific). Revisit!
            var conciseBody = node as ConciseBody;
            if (conciseBody != null)
            {
                name = conciseBody.Expression().As<IPropertyAccessExpression>().Name.Text;
            }

            if (name == Names.CurrentQualifier)
            {
                context.Logger.ReportCurrentQualifierCannotBeAccessedWithQualifications(
                    context.LoggingContext,
                    node.LocationForLogging(context.SourceFile),
                    Names.BaseQualifierType);
            }
        }
    }
}
