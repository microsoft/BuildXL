// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using TypeScript.Net.Types;
using TS = TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Blocks build logic constructs in project files
    /// </summary>
    /// <remarks>
    /// The rule prevents interface, type, enum and function declarations in project files. It also
    /// blocks exporting values with function type.
    /// TODO: blocking exported lambdas might make sense for project files, but consider that other values
    /// may be exported with types *containing* lambdas. Should we recursively go through those and make sure there are
    /// not functions? At this point the rule starts to sound a little bit weird. Reconsider this.
    /// </remarks>
    internal sealed class ForbidLogicInProjectsRule : LanguagePolicyRule
    {
        private ForbidLogicInProjectsRule()
        { }

        public static ForbidLogicInProjectsRule CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidLogicInProjectsRule();
            result.Initialize(context);
            return result;
        }

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                ForbidBuildLogicInProjects,
                TS.SyntaxKind.InterfaceDeclaration,
                TS.SyntaxKind.TypeAliasDeclaration,
                TS.SyntaxKind.EnumDeclaration,
                TS.SyntaxKind.FunctionDeclaration);

            context.RegisterSyntaxNodeAction(this, ForbidExportingLambdas, TS.SyntaxKind.VariableStatement);
        }

        private static void ForbidBuildLogicInProjects(INode node, DiagnosticContext context)
        {
            if (context.SourceFile.IsProjectFileExtension())
            {
                context.Logger.ReportNoBuildLogicInProjects(
                    context.LoggingContext,
                    node.LocationForLogging(context.SourceFile),
                    Names.DotBpExtension,
                    Names.DotBxtExtension);
            }
        }

        private static void ForbidExportingLambdas(INode node, DiagnosticContext context)
        {
            var statement = node.Cast<IVariableStatement>();

            // If it is a top-level exported variable declaration in a project file, then the type must not be a lambda
            if ((statement.Flags & NodeFlags.Export) != NodeFlags.None &&
               context.SourceFile.IsProjectFileExtension() &&
               statement.IsTopLevelOrNamespaceLevelDeclaration())
            {
                foreach (var variableDeclaration in statement.DeclarationList.Declarations.AsStructEnumerable())
                {
                    var initializer = variableDeclaration.Initializer;
                    if (initializer == null)
                    {
                        // There is a lint rule that prevents this from happening, but it might not have run yet
                        continue;
                    }

                    var type = context.Workspace.GetSemanticModel().GetTypeAtLocation(variableDeclaration.Initializer);
                    if (type.Symbol?.Flags == SymbolFlags.Function)
                    {
                        context.Logger.ReportNoExportedLambdasInProjects(
                           context.LoggingContext,
                           node.LocationForLogging(context.SourceFile),
                           Names.DotBpExtension,
                           Names.DotBxtExtension);
                    }
                }
            }
        }
    }
}
