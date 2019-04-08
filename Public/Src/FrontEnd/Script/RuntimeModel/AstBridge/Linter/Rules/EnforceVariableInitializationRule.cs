// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that variable declarations are always initialized.
    /// </summary>
    internal sealed class EnforceVariableInitializationRule : LanguageRule
    {
        private EnforceVariableInitializationRule()
        { }

        public static EnforceVariableInitializationRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceVariableInitializationRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckVariableDeclarationIsInitialized,
                TypeScript.Net.Types.SyntaxKind.VariableStatement);
        }

        private static void CheckVariableDeclarationIsInitialized(INode node, DiagnosticContext context)
        {
            // Variable declarations must be initialized. One exception is being part of a for-of or a for, but in those cases it is not a variable statement.
            // The other exception are ambient declarations
            var varStatement = node.Cast<VariableStatement>();

            // Ambient var declarations are not forced to have initializers
            var flags = varStatement.Flags;
            if ((flags & NodeFlags.Ambient) != 0)
            {
                return;
            }

            foreach (var declaration in varStatement.DeclarationList.Declarations.AsStructEnumerable())
            {
                if (declaration.Initializer == null)
                {
                    context.Logger.ReportVariableMustBeInitialized(context.LoggingContext, declaration.LocationForLogging(context.SourceFile), declaration.Name.GetText());
                }
            }
        }
    }
}
