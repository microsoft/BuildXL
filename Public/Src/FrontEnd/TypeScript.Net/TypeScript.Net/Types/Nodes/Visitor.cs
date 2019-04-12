// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Types
{
    // This interface is generated using Roslyn.

    /// <nodoc />
    public interface INodeVisitor
    {
        /// <nodoc />
        void VisitTokenNode(TokenNodeBase node);

        /// <nodoc />
        void VisitModifier(Modifier node);

        /// <nodoc />
        void VisitIdentifier(IIdentifier node);

        /// <nodoc />
        void VisitQualifiedName(QualifiedName node);

        /// <nodoc />
        void VisitComputedPropertyName(ComputedPropertyName node);

        /// <nodoc />
        void VisitDecorator(Decorator node);

        /// <nodoc />
        void VisitTypeParameterDeclaration(TypeParameterDeclaration node);

        /// <nodoc />
        void VisitCallSignatureDeclarationOrConstructSignatureDeclaration(CallSignatureDeclarationOrConstructSignatureDeclaration node);

        /// <nodoc />
        void VisitVariableDeclaration(VariableDeclaration node);

        /// <nodoc />
        void VisitVariableDeclarationList(VariableDeclarationList node);

        /// <nodoc />
        void VisitParameterDeclaration(ParameterDeclaration node);

        /// <nodoc />
        void VisitBindingElement(BindingElement node);

        /// <nodoc />
        void VisitPropertySignature(PropertySignature node);

        /// <nodoc />
        void VisitPropertyDeclaration(PropertyDeclaration node);

        /// <nodoc />
        void VisitPropertyAssignment(PropertyAssignment node);

        /// <nodoc />
        void VisitShorthandPropertyAssignment(ShorthandPropertyAssignment node);

        /// <nodoc />
        void VisitBindingPattern(BindingPattern node);

        /// <nodoc />
        void VisitFunctionDeclaration(FunctionDeclaration node);

        /// <nodoc />
        void VisitMethodSignature(MethodSignature node);

        /// <nodoc />
        void VisitMethodDeclaration(MethodDeclaration node);

        /// <nodoc />
        void VisitConstructorDeclaration(ConstructorDeclaration node);

        /// <nodoc />
        void VisitSemicolonClassElement(SemicolonClassElement node);

        /// <nodoc />
        void VisitAccessorDeclaration(AccessorDeclaration node);

        /// <nodoc />
        void VisitIndexSignatureDeclaration(IndexSignatureDeclaration node);

        /// <nodoc />
        void VisitTypeNode(TypeNode node);

        /// <nodoc />
        void VisitThisTypeNode(ThisTypeNode node);

        /// <nodoc />
        void VisitFunctionOrConstructorTypeNode(FunctionOrConstructorTypeNode node);

        /// <nodoc />
        void VisitTypeReferenceNode(TypeReferenceNode node);

        /// <nodoc />
        void VisitTypePredicateNode(TypePredicateNode node);

        /// <nodoc />
        void VisitTypeQueryNode(TypeQueryNode node);

        /// <nodoc />
        void VisitTypeLiteralNode(TypeLiteralNode node);

        /// <nodoc />
        void VisitArrayTypeNode(ArrayTypeNode node);

        /// <nodoc />
        void VisitTupleTypeNode(TupleTypeNode node);

        /// <nodoc />
        void VisitUnionOrIntersectionTypeNode(UnionOrIntersectionTypeNode node);

        /// <nodoc />
        void VisitParenthesizedTypeNode(ParenthesizedTypeNode node);

        /// <nodoc />
        void VisitStringLiteralTypeNode(StringLiteralTypeNode node);

        /// <nodoc />
        void VisitExpression(Expression node);

        /// <nodoc />
        void VisitPrefixUnaryExpression(PrefixUnaryExpression node);

        /// <nodoc />
        void VisitPostfixUnaryExpression(PostfixUnaryExpression node);

        /// <nodoc />
        void VisitPrimaryExpression(PrimaryExpression node);

        /// <nodoc />
        void VisitDeleteExpression(DeleteExpression node);

        /// <nodoc />
        void VisitTypeOfExpression(TypeOfExpression node);

        /// <nodoc />
        void VisitVoidExpression(VoidExpression node);

        /// <nodoc />
        void VisitYieldExpression(YieldExpression node);

        /// <nodoc />
        void VisitBinaryExpression(BinaryExpression node);

        /// <nodoc />
        void VisitConditionalExpression(ConditionalExpression node);

        /// <nodoc />
        void VisitSwitchExpression(SwitchExpression node);

        /// <nodoc />
        void VisitSwitchExpressionClause(SwitchExpressionClause node);

        /// <nodoc />
        void VisitFunctionExpression(FunctionExpression node);

        /// <nodoc />
        void VisitArrowFunction(ArrowFunction node);

        /// <nodoc />
        void VisitLiteralExpression(LiteralExpression node);

        /// <nodoc />
        void VisitTemplateLiteralFragment(ITemplateLiteralFragment node);

        /// <nodoc />
        void VisitTemplateExpression(TemplateExpression node);

        /// <nodoc />
        void VisitTemplateSpan(TemplateSpan node);

        /// <nodoc />
        void VisitParenthesizedExpression(ParenthesizedExpression node);

        /// <nodoc />
        void VisitArrayLiteralExpression(ArrayLiteralExpression node);

        /// <nodoc />
        void VisitSpreadElementExpression(SpreadElementExpression node);

        /// <nodoc />
        void VisitObjectLiteralExpression(ObjectLiteralExpression node);

        /// <nodoc />
        void VisitPropertyAccessExpression(PropertyAccessExpression node);

        /// <nodoc />
        void VisitElementAccessExpression(ElementAccessExpression node);

        /// <nodoc />
        void VisitCallExpression(CallExpression node);

        /// <nodoc />
        void VisitExpressionWithTypeArguments(ExpressionWithTypeArguments node);

        /// <nodoc />
        void VisitNewExpression(NewExpression node);

        /// <nodoc />
        void VisitTaggedTemplateExpression(TaggedTemplateExpression node);

        /// <nodoc />
        void VisitAsExpression(AsExpression node);

        /// <nodoc />
        void VisitTypeAssertion(TypeAssertion node);

        /// <nodoc />
        void VisitStatement(Statement node);

        /// <nodoc />
        void VisitEmptyStatement(EmptyStatement node);

        /// <nodoc />
        void VisitBlankLineStatement(BlankLineStatement node);

        /// <nodoc />
        void VisitBlock(Block node);

        /// <nodoc />
        void VisitVariableStatement(VariableStatement node);

        /// <nodoc />
        void VisitExpressionStatement(ExpressionStatement node);

        /// <nodoc />
        void VisitIfStatement(IfStatement node);

        /// <nodoc />
        void VisitDoStatement(DoStatement node);

        /// <nodoc />
        void VisitWhileStatement(WhileStatement node);

        /// <nodoc />
        void VisitForStatement(ForStatement node);

        /// <nodoc />
        void VisitForInStatement(ForInStatement node);

        /// <nodoc />
        void VisitForOfStatement(ForOfStatement node);

        /// <nodoc />
        void VisitBreakOrContinueStatement(BreakOrContinueStatement node);

        /// <nodoc />
        void VisitReturnStatement(ReturnStatement node);

        /// <nodoc />
        void VisitWithStatement(WithStatement node);

        /// <nodoc />
        void VisitSwitchStatement(SwitchStatement node);

        /// <nodoc />
        void VisitCaseBlock(CaseBlock node);

        /// <nodoc />
        void VisitCaseClause(CaseClause node);

        /// <nodoc />
        void VisitDefaultClause(DefaultClause node);

        /// <nodoc />
        void VisitLabeledStatement(LabeledStatement node);

        /// <nodoc />
        void VisitThrowStatement(ThrowStatement node);

        /// <nodoc />
        void VisitTryStatement(TryStatement node);

        /// <nodoc />
        void VisitCatchClause(CatchClause node);

        /// <nodoc />
        void VisitClassDeclaration(ClassDeclaration node);

        /// <nodoc />
        void VisitClassExpression(ClassExpression node);

        /// <nodoc />
        void VisitClassElement(ClassElement node);

        /// <nodoc />
        void VisitInterfaceDeclaration(InterfaceDeclaration node);

        /// <nodoc />
        void VisitHeritageClause(HeritageClause node);

        /// <nodoc />
        void VisitTypeAliasDeclaration(TypeAliasDeclaration node);

        /// <nodoc />
        void VisitEnumMember(EnumMember node);

        /// <nodoc />
        void VisitEnumDeclaration(EnumDeclaration node);

        /// <nodoc />
        void VisitModuleDeclaration(ModuleDeclaration node);

        /// <nodoc />
        void VisitModuleBlock(ModuleBlock node);

        /// <nodoc />
        void VisitImportEqualsDeclaration(ImportEqualsDeclaration node);

        /// <nodoc />
        void VisitExternalModuleReference(ExternalModuleReference node);

        /// <nodoc />
        void VisitImportDeclaration(ImportDeclaration node);

        /// <nodoc />
        void VisitImportClause(ImportClause node);

        /// <nodoc />
        void VisitNamespaceImport(NamespaceImport node);

        /// <nodoc />
        void VisitExportDeclaration(ExportDeclaration node);

        /// <nodoc />
        void VisitNamedImports(NamedImports node);

        /// <nodoc />
        void VisitNamedExports(NamedExports node);

        /// <nodoc />
        void VisitImportSpecifier(ImportSpecifier node);

        /// <nodoc />
        void VisitExportSpecifier(ExportSpecifier node);

        /// <nodoc />
        void VisitExportAssignment(ExportAssignment node);

        /// <nodoc />
        void VisitSourceFile(SourceFile node);

        /// <nodoc />
        void VisitCommentExpression(ICommentExpression node);

        /// <nodoc />
        void VisitCommentStatement(CommentStatement node);

        /// <nodoc />
        void VisitPathLikeLiteral(IPathLikeLiteralExpression node);
    }

    /// <nodoc />
    public interface INodeVisitor<TResult>
    {
        /// <nodoc />
        TResult VisitTokenNode(TokenNodeBase node);

        /// <nodoc />
        TResult VisitModifier(Modifier node);

        /// <nodoc />
        TResult VisitIdentifier(IIdentifier node);

        /// <nodoc />
        TResult VisitQualifiedName(QualifiedName node);

        /// <nodoc />
        TResult VisitComputedPropertyName(ComputedPropertyName node);

        /// <nodoc />
        TResult VisitDecorator(Decorator node);

        /// <nodoc />
        TResult VisitTypeParameterDeclaration(TypeParameterDeclaration node);

        /// <nodoc />
        TResult VisitCallSignatureDeclarationOrConstructSignatureDeclaration(CallSignatureDeclarationOrConstructSignatureDeclaration node);

        /// <nodoc />
        TResult VisitVariableDeclaration(VariableDeclaration node);

        /// <nodoc />
        TResult VisitVariableDeclarationList(VariableDeclarationList node);

        /// <nodoc />
        TResult VisitParameterDeclaration(ParameterDeclaration node);

        /// <nodoc />
        TResult VisitBindingElement(BindingElement node);

        /// <nodoc />
        TResult VisitPropertySignature(PropertySignature node);

        /// <nodoc />
        TResult VisitPropertyDeclaration(PropertyDeclaration node);

        /// <nodoc />
        TResult VisitPropertyAssignment(PropertyAssignment node);

        /// <nodoc />
        TResult VisitShorthandPropertyAssignment(ShorthandPropertyAssignment node);

        /// <nodoc />
        TResult VisitBindingPattern(BindingPattern node);

        /// <nodoc />
        TResult VisitFunctionDeclaration(FunctionDeclaration node);

        /// <nodoc />
        TResult VisitMethodSignature(MethodSignature node);

        /// <nodoc />
        TResult VisitMethodDeclaration(MethodDeclaration node);

        /// <nodoc />
        TResult VisitConstructorDeclaration(ConstructorDeclaration node);

        /// <nodoc />
        TResult VisitSemicolonClassElement(SemicolonClassElement node);

        /// <nodoc />
        TResult VisitAccessorDeclaration(AccessorDeclaration node);

        /// <nodoc />
        TResult VisitIndexSignatureDeclaration(IndexSignatureDeclaration node);

        /// <nodoc />
        TResult VisitTypeNode(TypeNode node);

        /// <nodoc />
        TResult VisitThisTypeNode(ThisTypeNode node);

        /// <nodoc />
        TResult VisitFunctionOrConstructorTypeNode(FunctionOrConstructorTypeNode node);

        /// <nodoc />
        TResult VisitTypeReferenceNode(TypeReferenceNode node);

        /// <nodoc />
        TResult VisitTypePredicateNode(TypePredicateNode node);

        /// <nodoc />
        TResult VisitTypeQueryNode(TypeQueryNode node);

        /// <nodoc />
        TResult VisitTypeLiteralNode(TypeLiteralNode node);

        /// <nodoc />
        TResult VisitArrayTypeNode(ArrayTypeNode node);

        /// <nodoc />
        TResult VisitTupleTypeNode(TupleTypeNode node);

        /// <nodoc />
        TResult VisitUnionOrIntersectionTypeNode(UnionOrIntersectionTypeNode node);

        /// <nodoc />
        TResult VisitParenthesizedTypeNode(ParenthesizedTypeNode node);

        /// <nodoc />
        TResult VisitStringLiteralTypeNode(StringLiteralTypeNode node);

        /// <nodoc />
        TResult VisitExpression(Expression node);

        /// <nodoc />
        TResult VisitPrefixUnaryExpression(PrefixUnaryExpression node);

        /// <nodoc />
        TResult VisitPostfixUnaryExpression(PostfixUnaryExpression node);

        /// <nodoc />
        TResult VisitPrimaryExpression(PrimaryExpression node);

        /// <nodoc />
        TResult VisitDeleteExpression(DeleteExpression node);

        /// <nodoc />
        TResult VisitTypeOfExpression(TypeOfExpression node);

        /// <nodoc />
        TResult VisitVoidExpression(VoidExpression node);

        /// <nodoc />
        TResult VisitYieldExpression(YieldExpression node);

        /// <nodoc />
        TResult VisitBinaryExpression(BinaryExpression node);

        /// <nodoc />
        TResult VisitConditionalExpression(ConditionalExpression node);

        /// <nodoc />
        TResult VisitSwitchExpression(SwitchExpression node);

        /// <nodoc />
        TResult VisitSwitchExpressionClause(SwitchExpressionClause node);

        /// <nodoc />
        TResult VisitFunctionExpression(FunctionExpression node);

        /// <nodoc />
        TResult VisitArrowFunction(ArrowFunction node);

        /// <nodoc />
        TResult VisitLiteralExpression(LiteralExpression node);

        /// <nodoc />
        TResult VisitTemplateLiteralFragment(ITemplateLiteralFragment node);

        /// <nodoc />
        TResult VisitTemplateExpression(TemplateExpression node);

        /// <nodoc />
        TResult VisitTemplateSpan(TemplateSpan node);

        /// <nodoc />
        TResult VisitParenthesizedExpression(ParenthesizedExpression node);

        /// <nodoc />
        TResult VisitArrayLiteralExpression(ArrayLiteralExpression node);

        /// <nodoc />
        TResult VisitSpreadElementExpression(SpreadElementExpression node);

        /// <nodoc />
        TResult VisitObjectLiteralExpression(ObjectLiteralExpression node);

        /// <nodoc />
        TResult VisitPropertyAccessExpression(PropertyAccessExpression node);

        /// <nodoc />
        TResult VisitElementAccessExpression(ElementAccessExpression node);

        /// <nodoc />
        TResult VisitCallExpression(CallExpression node);

        /// <nodoc />
        TResult VisitExpressionWithTypeArguments(ExpressionWithTypeArguments node);

        /// <nodoc />
        TResult VisitNewExpression(NewExpression node);

        /// <nodoc />
        TResult VisitTaggedTemplateExpression(TaggedTemplateExpression node);

        /// <nodoc />
        TResult VisitAsExpression(AsExpression node);

        /// <nodoc />
        TResult VisitTypeAssertion(TypeAssertion node);

        /// <nodoc />
        TResult VisitStatement(Statement node);

        /// <nodoc />
        TResult VisitEmptyStatement(EmptyStatement node);

        /// <nodoc />
        TResult VisitBlankLineStatement(BlankLineStatement node);

        /// <nodoc />
        TResult VisitBlock(Block node);

        /// <nodoc />
        TResult VisitVariableStatement(VariableStatement node);

        /// <nodoc />
        TResult VisitExpressionStatement(ExpressionStatement node);

        /// <nodoc />
        TResult VisitIfStatement(IfStatement node);

        /// <nodoc />
        TResult VisitDoStatement(DoStatement node);

        /// <nodoc />
        TResult VisitWhileStatement(WhileStatement node);

        /// <nodoc />
        TResult VisitForStatement(ForStatement node);

        /// <nodoc />
        TResult VisitForInStatement(ForInStatement node);

        /// <nodoc />
        TResult VisitForOfStatement(ForOfStatement node);

        /// <nodoc />
        TResult VisitBreakOrContinueStatement(BreakOrContinueStatement node);

        /// <nodoc />
        TResult VisitReturnStatement(ReturnStatement node);

        /// <nodoc />
        TResult VisitWithStatement(WithStatement node);

        /// <nodoc />
        TResult VisitSwitchStatement(SwitchStatement node);

        /// <nodoc />
        TResult VisitCaseBlock(CaseBlock node);

        /// <nodoc />
        TResult VisitCaseClause(CaseClause node);

        /// <nodoc />
        TResult VisitDefaultClause(DefaultClause node);

        /// <nodoc />
        TResult VisitLabeledStatement(LabeledStatement node);

        /// <nodoc />
        TResult VisitThrowStatement(ThrowStatement node);

        /// <nodoc />
        TResult VisitTryStatement(TryStatement node);

        /// <nodoc />
        TResult VisitCatchClause(CatchClause node);

        /// <nodoc />
        TResult VisitClassDeclaration(ClassDeclaration node);

        /// <nodoc />
        TResult VisitClassExpression(ClassExpression node);

        /// <nodoc />
        TResult VisitClassElement(ClassElement node);

        /// <nodoc />
        TResult VisitInterfaceDeclaration(InterfaceDeclaration node);

        /// <nodoc />
        TResult VisitHeritageClause(HeritageClause node);

        /// <nodoc />
        TResult VisitTypeAliasDeclaration(TypeAliasDeclaration node);

        /// <nodoc />
        TResult VisitEnumMember(EnumMember node);

        /// <nodoc />
        TResult VisitEnumDeclaration(EnumDeclaration node);

        /// <nodoc />
        TResult VisitModuleDeclaration(ModuleDeclaration node);

        /// <nodoc />
        TResult VisitModuleBlock(ModuleBlock node);

        /// <nodoc />
        TResult VisitImportEqualsDeclaration(ImportEqualsDeclaration node);

        /// <nodoc />
        TResult VisitExternalModuleReference(ExternalModuleReference node);

        /// <nodoc />
        TResult VisitImportDeclaration(ImportDeclaration node);

        /// <nodoc />
        TResult VisitImportClause(ImportClause node);

        /// <nodoc />
        TResult VisitNamespaceImport(NamespaceImport node);

        /// <nodoc />
        TResult VisitExportDeclaration(ExportDeclaration node);

        /// <nodoc />
        TResult VisitNamedImports(NamedImports node);

        /// <nodoc />
        TResult VisitNamedExports(NamedExports node);

        /// <nodoc />
        TResult VisitImportSpecifier(ImportSpecifier node);

        /// <nodoc />
        TResult VisitExportSpecifier(ExportSpecifier node);

        /// <nodoc />
        TResult VisitExportAssignment(ExportAssignment node);

        /// <nodoc />
        TResult VisitSourceFile(SourceFile node);

        /// <nodoc />
        TResult VisitCommentExpression(ICommentExpression node);

        /// <nodoc />
        TResult VisitCommentStatement(CommentStatement node);

        /// <nodoc />
        TResult VisitPathLikeLiteral(IPathLikeLiteralExpression node);
    }

    /// <summary>
    /// Role-interface that simplifies implementation of the visitors.
    /// </summary>
    public interface IVisitableNode : INode
    {
        /// <nodoc />
        void Accept(INodeVisitor visitor);

        /// <nodoc />
        TResult Accept<TResult>(INodeVisitor<TResult> visitor);
    }

    partial class NodeBase<TExtraState> : IVisitableNode
    {
        /// <nodoc />
        internal abstract void Accept(INodeVisitor visitor);

        /// <nodoc />
        internal abstract TResult Accept<TResult>(INodeVisitor<TResult> visitor);

        /// <nodoc />
        void IVisitableNode.Accept(INodeVisitor visitor)
        {
            Accept(visitor);
        }

        /// <nodoc />
        TResult IVisitableNode.Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return Accept<TResult>(visitor);
        }
    }

    partial class Node
    { }

    partial class NodeWithContextualType
    {
    }

    partial class DotTokenNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTokenNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTokenNode(this);
        }
    }

    partial class TokenNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTokenNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTokenNode(this);
        }
    }

    partial class Modifier
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitModifier(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitModifier(this);
        }
    }

    partial class Identifier : IVisitableNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitIdentifier(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitIdentifier(this);
        }
    }

    partial class QualifiedName
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitQualifiedName(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitQualifiedName(this);
        }
    }

    partial class ComputedPropertyName
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitComputedPropertyName(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitComputedPropertyName(this);
        }
    }

    partial class Decorator
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitDecorator(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitDecorator(this);
        }
    }

    partial class TypeParameterDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTypeParameterDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTypeParameterDeclaration(this);
        }
    }

    partial class CallSignatureDeclarationOrConstructSignatureDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitCallSignatureDeclarationOrConstructSignatureDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitCallSignatureDeclarationOrConstructSignatureDeclaration(this);
        }
    }

    partial class CommentAsLiteralElement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitCommentExpression(CommentExpression);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitCommentExpression(CommentExpression);
        }
    }

    partial class CommentAsTypeElement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitCommentExpression(CommentExpression);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitCommentExpression(CommentExpression);
        }
    }

    partial class CommentAsEnumMember
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitCommentExpression(CommentExpression);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitCommentExpression(CommentExpression);
        }
    }

    partial class VariableDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitVariableDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitVariableDeclaration(this);
        }
    }

    partial class VariableDeclarationList
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitVariableDeclarationList(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitVariableDeclarationList(this);
        }
    }

    partial class ParameterDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitParameterDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitParameterDeclaration(this);
        }
    }

    partial class BindingElement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitBindingElement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitBindingElement(this);
        }
    }

    partial class PropertySignature
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitPropertySignature(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitPropertySignature(this);
        }
    }

    partial class PropertyDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitPropertyDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitPropertyDeclaration(this);
        }
    }

    partial class PropertyAssignment
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitPropertyAssignment(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitPropertyAssignment(this);
        }
    }

    partial class ShorthandPropertyAssignment
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitShorthandPropertyAssignment(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitShorthandPropertyAssignment(this);
        }
    }

    partial class BindingPattern
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitBindingPattern(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitBindingPattern(this);
        }
    }

    partial class FunctionDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitFunctionDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitFunctionDeclaration(this);
        }
    }

    partial class MethodSignature
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitMethodSignature(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitMethodSignature(this);
        }
    }

    partial class MethodDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitMethodDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitMethodDeclaration(this);
        }
    }

    partial class ConstructorDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitConstructorDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitConstructorDeclaration(this);
        }
    }

    partial class SemicolonClassElement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitSemicolonClassElement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitSemicolonClassElement(this);
        }
    }

    partial class AccessorDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitAccessorDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitAccessorDeclaration(this);
        }
    }

    partial class IndexSignatureDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitIndexSignatureDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitIndexSignatureDeclaration(this);
        }
    }

    partial class TypeNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTypeNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTypeNode(this);
        }
    }

    partial class ThisTypeNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitThisTypeNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitThisTypeNode(this);
        }
    }

    partial class FunctionOrConstructorTypeNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitFunctionOrConstructorTypeNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitFunctionOrConstructorTypeNode(this);
        }
    }

    partial class TypeReferenceNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTypeReferenceNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTypeReferenceNode(this);
        }
    }

    partial class TypePredicateNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTypePredicateNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTypePredicateNode(this);
        }
    }

    partial class TypeQueryNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTypeQueryNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTypeQueryNode(this);
        }
    }

    partial class TypeLiteralNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTypeLiteralNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTypeLiteralNode(this);
        }
    }

    partial class ArrayTypeNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitArrayTypeNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitArrayTypeNode(this);
        }
    }

    partial class TupleTypeNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTupleTypeNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTupleTypeNode(this);
        }
    }

    partial class UnionOrIntersectionTypeNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitUnionOrIntersectionTypeNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitUnionOrIntersectionTypeNode(this);
        }
    }

    partial class ParenthesizedTypeNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitParenthesizedTypeNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitParenthesizedTypeNode(this);
        }
    }

    partial class StringLiteralTypeNode
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitStringLiteralTypeNode(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitStringLiteralTypeNode(this);
        }
    }

    partial class Expression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitExpression(this);
        }
    }

    partial class PrefixUnaryExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitPrefixUnaryExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitPrefixUnaryExpression(this);
        }
    }

    partial class PostfixUnaryExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitPostfixUnaryExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitPostfixUnaryExpression(this);
        }
    }

    partial class PrimaryExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitPrimaryExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitPrimaryExpression(this);
        }
    }

    partial class DeleteExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitDeleteExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitDeleteExpression(this);
        }
    }

    partial class TypeOfExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTypeOfExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTypeOfExpression(this);
        }
    }

    partial class VoidExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitVoidExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitVoidExpression(this);
        }
    }

    partial class YieldExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitYieldExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitYieldExpression(this);
        }
    }

    partial class BinaryExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitBinaryExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitBinaryExpression(this);
        }
    }

    partial class ConditionalExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitConditionalExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitConditionalExpression(this);
        }
    }

    partial class SwitchExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitSwitchExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitSwitchExpression(this);
        }
    }

    partial class SwitchExpressionClause
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitSwitchExpressionClause(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitSwitchExpressionClause(this);
        }
    }

    partial class FunctionExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitFunctionExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitFunctionExpression(this);
        }
    }

    partial class ArrowFunction
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitArrowFunction(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitArrowFunction(this);
        }
    }

    partial class LiteralExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitLiteralExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitLiteralExpression(this);
        }
    }

    partial class TemplateLiteralFragment
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTemplateLiteralFragment(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTemplateLiteralFragment(this);
        }
    }

    partial class TemplateExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTemplateExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTemplateExpression(this);
        }
    }

    partial class TemplateSpan
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTemplateSpan(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTemplateSpan(this);
        }
    }

    partial class ParenthesizedExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitParenthesizedExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitParenthesizedExpression(this);
        }
    }

    partial class ArrayLiteralExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitArrayLiteralExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitArrayLiteralExpression(this);
        }
    }

    partial class SpreadElementExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitSpreadElementExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitSpreadElementExpression(this);
        }
    }

    partial class ObjectLiteralExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitObjectLiteralExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitObjectLiteralExpression(this);
        }
    }

    partial class PropertyAccessExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitPropertyAccessExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitPropertyAccessExpression(this);
        }
    }

    partial class ElementAccessExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitElementAccessExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitElementAccessExpression(this);
        }
    }

    partial class CallExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitCallExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitCallExpression(this);
        }
    }

    partial class ExpressionWithTypeArguments
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitExpressionWithTypeArguments(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitExpressionWithTypeArguments(this);
        }
    }

    partial class NewExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitNewExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitNewExpression(this);
        }
    }

    partial class TaggedTemplateExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTaggedTemplateExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTaggedTemplateExpression(this);
        }
    }

    partial class AsExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitAsExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitAsExpression(this);
        }
    }

    partial class TypeAssertion
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTypeAssertion(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTypeAssertion(this);
        }
    }

    partial class Statement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitStatement(this);
        }
    }

    partial class EmptyStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitEmptyStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitEmptyStatement(this);
        }
    }

    partial class SingleLineCommentExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitCommentExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitCommentExpression(this);
        }
    }

    partial class MultiLineCommentExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitCommentExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitCommentExpression(this);
        }
    }

    partial class CommentStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitCommentStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitCommentStatement(this);
        }
    }

    partial class BlankLineStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitBlankLineStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitBlankLineStatement(this);
        }
    }

    partial class Block
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitBlock(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitBlock(this);
        }
    }

    partial class VariableStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitVariableStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitVariableStatement(this);
        }
    }

    partial class ExpressionStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitExpressionStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitExpressionStatement(this);
        }
    }

    partial class IfStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitIfStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitIfStatement(this);
        }
    }

    partial class DoStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitDoStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitDoStatement(this);
        }
    }

    partial class WhileStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitWhileStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitWhileStatement(this);
        }
    }

    partial class ForStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitForStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitForStatement(this);
        }
    }

    partial class ForInStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitForInStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitForInStatement(this);
        }
    }

    partial class ForOfStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitForOfStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitForOfStatement(this);
        }
    }

    partial class BreakOrContinueStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitBreakOrContinueStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitBreakOrContinueStatement(this);
        }
    }

    partial class ReturnStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitReturnStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitReturnStatement(this);
        }
    }

    partial class WithStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitWithStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitWithStatement(this);
        }
    }

    partial class SwitchStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitSwitchStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitSwitchStatement(this);
        }
    }

    partial class CaseBlock
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitCaseBlock(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitCaseBlock(this);
        }
    }

    partial class CaseClause
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitCaseClause(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitCaseClause(this);
        }
    }

    partial class DefaultClause
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitDefaultClause(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitDefaultClause(this);
        }
    }

    partial class LabeledStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitLabeledStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitLabeledStatement(this);
        }
    }

    partial class ThrowStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitThrowStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitThrowStatement(this);
        }
    }

    partial class TryStatement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTryStatement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTryStatement(this);
        }
    }

    partial class CatchClause
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitCatchClause(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitCatchClause(this);
        }
    }

    partial class ClassDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitClassDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitClassDeclaration(this);
        }
    }

    partial class ClassExpression
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitClassExpression(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitClassExpression(this);
        }
    }

    partial class ClassElement
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitClassElement(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitClassElement(this);
        }
    }

    partial class InterfaceDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitInterfaceDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitInterfaceDeclaration(this);
        }
    }

    partial class HeritageClause
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitHeritageClause(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitHeritageClause(this);
        }
    }

    partial class TypeAliasDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTypeAliasDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTypeAliasDeclaration(this);
        }
    }

    partial class EnumMember
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitEnumMember(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitEnumMember(this);
        }
    }

    partial class EnumDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitEnumDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitEnumDeclaration(this);
        }
    }

    partial class ModuleDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitModuleDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitModuleDeclaration(this);
        }
    }

    partial class ModuleBlock
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitModuleBlock(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitModuleBlock(this);
        }
    }

    partial class ImportEqualsDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitImportEqualsDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitImportEqualsDeclaration(this);
        }
    }

    partial class ExternalModuleReference
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitExternalModuleReference(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitExternalModuleReference(this);
        }
    }

    partial class ImportDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitImportDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitImportDeclaration(this);
        }
    }

    partial class ImportClause
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitImportClause(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitImportClause(this);
        }
    }

    partial class NamespaceImport
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitNamespaceImport(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitNamespaceImport(this);
        }
    }

    partial class ExportDeclaration
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitExportDeclaration(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitExportDeclaration(this);
        }
    }

    partial class NamedImports
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitNamedImports(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitNamedImports(this);
        }
    }

    partial class NamedExports
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitNamedExports(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitNamedExports(this);
        }
    }

    partial class ImportSpecifier
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitImportSpecifier(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitImportSpecifier(this);
        }
    }

    partial class ExportSpecifier
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitExportSpecifier(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitExportSpecifier(this);
        }
    }

    partial class ExportAssignment
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitExportAssignment(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitExportAssignment(this);
        }
    }

    partial class SourceFile
    {
        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitSourceFile(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitSourceFile(this);
        }
    }
}
