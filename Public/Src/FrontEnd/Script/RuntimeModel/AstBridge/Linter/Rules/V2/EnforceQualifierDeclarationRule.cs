// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Workspaces;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks qualifier declarations are well shaped, including extra qualifier-specific type enforcements.
    /// </summary>
    /// <remarks>
    /// This is a V2 rule that only runs when the semantic model is available
    /// </remarks>
    internal sealed class EnforceQualifierDeclarationRule : LanguageRule
    {
        private EnforceQualifierDeclarationRule()
        { }

        public static EnforceQualifierDeclarationRule CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceQualifierDeclarationRule();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckWellShapedQualifier,
                TypeScript.Net.Types.SyntaxKind.VariableDeclarationList);
        }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.SpecFile;

        private static void CheckWellShapedQualifier(INode node, DiagnosticContext context)
        {
            var varDeclarationList = node.Cast<IVariableDeclarationList>();

            // If none of the declarations is a 'qualifier', there is nothing to do
            if (!varDeclarationList.Declarations.Any(e => IsQualifierDeclaration(e)))
            {
                return;
            }

            // When a declaration is not top level or namespace level, export nor declare modifiers can show up
            // So this means we could allow a 'qualifier' declaration inside a function, but for consistency,
            // we make it fail with the right error message
            if (!node.IsTopLevelOrNamespaceLevelDeclaration())
            {
                context.Logger.QualifierDeclarationShouldBeTopLevel(
                    context.LoggingContext,
                    varDeclarationList.LocationForLogging(context.SourceFile));
                return;
            }

            if (varDeclarationList.Declarations.Count > 1)
            {
                context.Logger.QualifierDeclarationShouldBeAloneInTheStatement(
                    context.LoggingContext,
                    varDeclarationList.LocationForLogging(context.SourceFile));
                return;
            }

            var qualifierDeclaration = varDeclarationList.Declarations[0];
            var qualifierStatement = varDeclarationList.Parent as IVariableStatement;

            Contract.Assert(qualifierStatement != null);

            if ((qualifierStatement.Flags & NodeFlags.Export) == NodeFlags.None ||
                (qualifierStatement.Flags & NodeFlags.Ambient) == NodeFlags.None ||
                (varDeclarationList.Flags & NodeFlags.Const) == NodeFlags.None)
            {
                context.Logger.QualifierDeclarationShouldBeConstExportAmbient(
                    context.LoggingContext,
                    qualifierStatement.LocationForLogging(context.SourceFile));
                return;
            }

            ValidateQualifierDeclarationType(qualifierDeclaration, context.SemanticModel, context);
        }

        private static void ValidateQualifierDeclarationType(IVariableDeclaration qualifierDeclaration, ISemanticModel semanticModel, DiagnosticContext context)
        {
            var type = qualifierDeclaration.Type;

            if (type == null)
            {
                context.Logger.QualifierTypeShouldBePresent(
                    context.LoggingContext,
                    qualifierDeclaration.LocationForLogging(context.SourceFile));
                return;
            }

            // The type can be a type literal, with restrictions
            var typeLiteral = type.As<ITypeLiteralNode>();
            if (typeLiteral != null)
            {
                CheckAllowedTypeLiteral(typeLiteral, semanticModel, context);
                return;
            }

            // Or the type can be a reference type, with restrictions
            CheckAllowedTypeReference(type, semanticModel, context);
        }

        private static void CheckAllowedTypeLiteral(ITypeLiteralNode typeLiteral, ISemanticModel semanticModel, DiagnosticContext context)
        {
            if (typeLiteral.Members == null)
            {
                return;
            }

            foreach (var typeLiteralMember in typeLiteral.Members)
            {
                CheckAllowedTypeLiteralMember(typeLiteralMember, semanticModel, context);
            }
        }

        private static void CheckAllowedTypeLiteralMember(ITypeElement typeElement, ISemanticModel semanticModel, DiagnosticContext context)
        {
            var propertySignature = typeElement.As<IPropertySignature>();

            // The propery signature name should always be an identifier
            if (propertySignature?.Name.Kind != TypeScript.Net.Types.SyntaxKind.Identifier)
            {
                context.Logger.QualifierLiteralMemberShouldBeAnIdentifier(
                    context.LoggingContext,
                    propertySignature.LocationForLogging(context.SourceFile));
                return;
            }

            // TODO: This requirements should be relaxed when we fully implement default qualifier keys
            if (propertySignature.QuestionToken.HasValue)
            {
                context.Logger.QualifierOptionalMembersAreNotAllowed(
                    context.LoggingContext,
                    propertySignature.LocationForLogging(context.SourceFile));
                return;
            }

            // If the property signature is string, this represents a value wildcard
            // TODO: Uncomment when we start supporting this at runtime
            // if (propertySignature.Type.Kind == TypeScript.Net.Types.SyntaxKind.StringKeyword)
            // {
            //    return true;
            // }

            // A union type is allowed, as long as it is a StringLiteral type
            var type = semanticModel.GetTypeAtLocation(propertySignature.Type);
            if ((type.Flags & TypeFlags.StringLiteral) == TypeFlags.None)
            {
                var unionType = type.As<IUnionType>();
                if (unionType == null || unionType.Types.Any(t => (t.Flags & TypeFlags.StringLiteral) == TypeFlags.None))
                {
                    context.Logger.QualifierLiteralTypeMemberShouldHaveStringLiteralType(
                        context.LoggingContext,
                        propertySignature.LocationForLogging(context.SourceFile));
                    return;
                }
            }
        }

        private static void CheckAllowedTypeReference(ITypeNode node, ISemanticModel semanticModel, DiagnosticContext context)
        {
            var type = semanticModel.GetTypeAtLocation(node);

            // The resolved type should be an interface
            IInterfaceType interfaceType = type.As<IInterfaceType>();

            if (interfaceType == null)
            {
                // We already checked this is not a type literal at this point
                context.Logger.QualifierTypeShouldBeAnInterfaceOrTypeLiteral(
                    context.LoggingContext,
                    node.LocationForLogging(context.SourceFile));
                return;
            }

            // The resolved type should be the special 'Qualifier' type or directly inherit from it
            if (!IsQualifierTypeOrDirectlyInheritsFromIt(interfaceType, semanticModel))
            {
                context.Logger.QualifierInterfaceTypeShouldBeOrInheritFromQualifier(
                    context.LoggingContext,
                    node.LocationForLogging(context.SourceFile),
                    Names.BaseQualifierType);
                return;
            }

            // All declarations need to be type-literal like
            var resolvedType = interfaceType.As<IResolvedType>();
            Contract.Assert(resolvedType != null);

            if (resolvedType.Properties == null)
            {
                return;
            }

            foreach (var property in resolvedType.Properties)
            {
                CheckResolvedPropertyIsAnAllowedType(property, semanticModel, context);
            }
        }

        private static void CheckResolvedPropertyIsAnAllowedType(ISymbol property, ISemanticModel semanticModel, DiagnosticContext context)
        {
            Contract.Assert(property.ValueDeclaration != null);

            var propertySignature = property.ValueDeclaration.As<IPropertySignature>();
            if (propertySignature == null)
            {
                context.Logger.QualifierLiteralMemberShouldBeAnIdentifier(
                    context.LoggingContext,
                    property.ValueDeclaration.LocationForLogging(context.SourceFile));
                return;
            }

            CheckAllowedTypeLiteralMember(propertySignature, semanticModel, context);
        }

        private static bool IsQualifierDeclaration(IVariableDeclaration declaration)
        {
            var id = declaration.Name.As<IIdentifier>();
            return id != null && id.Text == Names.CurrentQualifier;
        }

        private static bool IsQualifierTypeOrDirectlyInheritsFromIt(IInterfaceType interfaceType, ISemanticModel semanticModel)
        {
            if (interfaceType.Symbol == null)
            {
                return false;
            }

            return interfaceType.Symbol.Name == Names.BaseQualifierType ||
                   // Need to use TypeChecker.GetBaseTypes instead of interfaceType.ResolvedBaseTypes because the base types resolution is lazy.
                   semanticModel.TypeChecker.GetBaseTypes(interfaceType)?.Any(type => type.Symbol?.Name == Names.BaseQualifierType) == true;
        }
    }
}
