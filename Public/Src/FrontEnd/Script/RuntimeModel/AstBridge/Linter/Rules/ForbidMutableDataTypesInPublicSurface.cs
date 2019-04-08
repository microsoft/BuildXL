// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that mutable data types are used only as implementation details and are not used
    /// as top-level constants or in exported functions.
    /// </summary>
    internal sealed class ForbidMutableDataTypesInPublicSurface : LanguagePolicyRule
    {
        private ForbidMutableDataTypesInPublicSurface()
        { }

        /// <nodoc />
        public static ForbidMutableDataTypesInPublicSurface CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidMutableDataTypesInPublicSurface();
            result.Initialize(context);
            return result;
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(this, EnforceFunctionDeclareReturnType, TypeScript.Net.Types.SyntaxKind.FunctionDeclaration);
            context.RegisterSyntaxNodeAction(
                this,
                AnalyzeVariableStatement,
                TypeScript.Net.Types.SyntaxKind.VariableStatement);
        }

        private static void EnforceFunctionDeclareReturnType(INode node, DiagnosticContext context)
        {
            var functionDeclaration = node.As<IFunctionDeclaration>();
            if (functionDeclaration.IsExported())
            {
                if (functionDeclaration?.IsReturnTypeMutable(context.SemanticModel) == true ||
                    functionDeclaration?.HasMutableParameterType(context.SemanticModel) == true)
                {
                    context.Logger.ReportNoMutableDeclarationsAtExposedFunctions(
                        context.LoggingContext,
                        functionDeclaration.LocationForLogging(context.SourceFile));
                }
            }
        }

        private static void AnalyzeVariableStatement(INode node, DiagnosticContext context)
        {
            var statement = node.Cast<IVariableStatement>();
            if (statement.IsTopLevelOrNamespaceLevelDeclaration())
            {
                foreach (var declaration in (statement.DeclarationList?.Declarations ?? NodeArray.Empty<IVariableDeclaration>()).AsStructEnumerable())
                {
                    var type = context.SemanticModel.GetTypeAtLocation(declaration);
                    if (type != null && type.IsMutable())
                    {
                        context.Logger.ReportNoMutableDeclarationsAtTopLevel(
                            context.LoggingContext,
                            statement.LocationForLogging(context.SourceFile));
                    }
                }
            }
        }
    }
}
