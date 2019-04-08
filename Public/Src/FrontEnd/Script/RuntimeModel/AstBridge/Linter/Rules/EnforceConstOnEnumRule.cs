// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that 'const' modifier is enforced for enums.
    /// This is a behavior that is not aligned with the latest language decisions and should be changed.
    /// But for now, it reflects the same behavior the Coco-based parser has
    /// </summary>
    internal sealed class EnforceConstOnEnumRule : LanguagePolicyRule
    {
        private EnforceConstOnEnumRule()
        { }

        public static EnforceConstOnEnumRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceConstOnEnumRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckOnlyConstEnumsAreAllowed,
                TypeScript.Net.Types.SyntaxKind.EnumDeclaration);
        }

        private static void CheckOnlyConstEnumsAreAllowed(INode node, DiagnosticContext context)
        {
            // TODO: this behavior should be replaced with new enum syntax (non-const syntax declaration with const behavior)
            var enumDeclaration = node.Cast<EnumDeclaration>();

            if (!enumDeclaration.IsConstEnumDeclaration())
            {
                context.Logger.ReportNotSupportedNonConstEnums(context.LoggingContext, node.LocationForLogging(context.SourceFile));
            }
        }
    }
}
