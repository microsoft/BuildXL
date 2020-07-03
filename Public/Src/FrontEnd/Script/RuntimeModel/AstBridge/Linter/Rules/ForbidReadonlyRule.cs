// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that readonly modifier is not allowed in interface members
    /// </summary>
    internal sealed class ForbidReadonlyRule : LanguagePolicyRule
    {
        private ForbidReadonlyRule()
        { }

        public static ForbidReadonlyRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidReadonlyRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckReadonlyIsNotAllowedInInterfaceMemberDeclaration,
                TypeScript.Net.Types.SyntaxKind.InterfaceDeclaration);
        }

        private static void CheckReadonlyIsNotAllowedInInterfaceMemberDeclaration(INode node, DiagnosticContext context)
        {
            // Explicit readonly modifiers are not supported, all declarations are implicitly readonly.
            // This is a rule that could be a policy rule, leaving it as a non-configurable one for now
            var interfaceDeclaration = node.As<IInterfaceDeclaration>();
            foreach (var interfaceMember in interfaceDeclaration.Members)
            {
                if (interfaceMember.Modifiers?.Any(m => m.Kind == TypeScript.Net.Types.SyntaxKind.ReadonlyKeyword) == true)
                {
                    context.Logger.ReportNotSupportedReadonlyModifier(context.LoggingContext, interfaceMember.LocationForLogging(context.SourceFile));
                }
            }
        }
    }
}
