// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace TypeScript.Net.Types.Nodes
{
    /// <summary>
    /// Bare bones visitor for TypeScript.Net AST.
    /// </summary>
    public class DfsVisitor
    {
        #region Base

        /// <nodoc />
        public virtual void VisitIdentifier(IIdentifier node)
        {
        }

        #endregion

        /// <nodoc />
        public virtual void VisitFunctionLikeDeclaration(IFunctionLikeDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Name != null)
            {
                VisitPropertyName(node.Name);
            }

            VisitNodes(node.TypeParameters, VisitTypeParameterDeclaration);
            VisitNodes(node.Parameters, VisitParameterDeclaration);
            VisitTypeDispatch(node.Type);

            if (node.Body != null)
            {
                VisitConciseBody(node.Body);
            }
        }

        /// <nodoc />
        public virtual void VisitPropertyName(PropertyName node)
        {
            if (node == null)
            {
                return;
            }

            switch (node.Kind)
            {
                case SyntaxKind.Identifier:
                    VisitIdentifier(node.Cast<IIdentifier>());
                    break;
                case SyntaxKind.ComputedPropertyName:
                    VisitComputedPropertyName(node.Cast<IComputedPropertyName>());
                    break;
                default:
                    VisitLiteralExpressionDispatch(node.Cast<ILiteralExpression>());
                    break;
            }
        }

        /// <nodoc />
        public virtual void VisitEntityName(EntityName node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Kind == SyntaxKind.Identifier)
            {
                VisitIdentifier(node.Cast<IIdentifier>());
            }
            else
            {
                VisitQualifiedName(node.Cast<IQualifiedName>());
            }
        }

        /// <nodoc />
        public virtual void VisitQualifiedName(IQualifiedName node)
        {
            if (node == null)
            {
                return;
            }

            VisitEntityName(node.Left);
            VisitIdentifier(node.Right);
        }

        /// <nodoc />
        public virtual void VisitComputedPropertyName(IComputedPropertyName node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitNodes<T>(INodeArray<T> nodes, Action<T> visit)
        {
            if (nodes == null)
            {
                return;
            }

            foreach (var node in nodes)
            {
                visit(node);
            }
        }

        /// <nodoc />
        public virtual void VisitOptional<T>(Optional<T> node, Action<T> visit)
            where T : INode
        {
            if (node.HasValue)
            {
                visit(node.Value);
            }
        }

        /// <nodoc />
        public virtual void VisitSourceFile(ISourceFile node)
        {
            if (node == null)
            {
                return;
            }

            VisitStatementContainer(node);
            VisitNode(node);
        }

        /// <nodoc />
        public virtual void VisitNode(INode node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes(node.Decorators, VisitDecorator);
        }

        /// <nodoc />
        public virtual void VisitToken(ITokenNode node)
        {
        }

        /// <nodoc />
        public virtual void VisitDecorator(IDecorator node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitModifier(IModifier node)
        {
        }

        #region Dispatch

        /// <nodoc />
        public virtual void VisitNodeDispatch(INode node)
        {
            if (node == null)
            {
                return;
            }

            // Expression
            switch (node.Kind)
            {
                case SyntaxKind.SourceFile:
                    VisitSourceFile(node.Cast<ISourceFile>());
                    break;

                // IDeclarationStatement (small optimization to avoid extra dispatch)
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.ExportAssignment:
                case SyntaxKind.ImportDeclaration:
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.ExportDeclaration:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                    VisitDeclarationStatementDispatch(node.Cast<IDeclarationStatement>());
                    break;

                    // IStatement
                // ILiteralExpression (small optimization to avoid extra dispatch)
                case SyntaxKind.StringLiteral:
                case SyntaxKind.TemplateExpression:
                    VisitLiteralExpressionDispatch(node.Cast<ILiteralExpression>());
                    break;

                    // IExpression
                case SyntaxKind.ArrayLiteralExpression:
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.AsExpression:
                case SyntaxKind.AwaitExpression:
                case SyntaxKind.BinaryExpression:
                case SyntaxKind.CallExpression:
                case SyntaxKind.ClassExpression:
                case SyntaxKind.MultiLineCommentTrivia:
                case SyntaxKind.SingleLineCommentTrivia:
                case SyntaxKind.ConditionalExpression:
                case SyntaxKind.SwitchExpression:
                case SyntaxKind.SwitchExpressionClause:
                case SyntaxKind.DeleteExpression:
                case SyntaxKind.ElementAccessExpression:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.Identifier:
                case SyntaxKind.PostfixUnaryExpression:
                case SyntaxKind.PrefixUnaryExpression:
                case SyntaxKind.NewExpression:
                case SyntaxKind.ObjectLiteralExpression:
                case SyntaxKind.OmittedExpression:
                case SyntaxKind.ParenthesizedExpression:
                case SyntaxKind.PropertyAccessExpression:
                case SyntaxKind.SpreadElementExpression:
                case SyntaxKind.TypeAssertionExpression:
                case SyntaxKind.TypeOfExpression:
                case SyntaxKind.VoidExpression:
                case SyntaxKind.YieldExpression:
                case SyntaxKind.TaggedTemplateExpression:
                    VisitExpressionDispatch(node.Cast<IExpression>());
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        /// <nodoc />
        public virtual void VisitExpressionDispatch(IExpression node)
        {
            if (node == null)
            {
                return;
            }

            var uOrB = node as UnaryExpressionOrBinaryExpression;
            if (uOrB != null)
            {
                // Unwrap unary/binary wrapper
                node = (IExpression)uOrB.Node;
            }

            switch (node.Kind)
            {
                case SyntaxKind.ArrayLiteralExpression:
                    VisitArayLiteralExpression(node.Cast<IArrayLiteralExpression>());
                    break;
                case SyntaxKind.ArrowFunction:
                    VisitArrowFunction(node.Cast<IArrowFunction>());
                    break;
                case SyntaxKind.AsExpression:
                    VisitAsExpression(node.Cast<IAsExpression>());
                    break;
                case SyntaxKind.AwaitExpression:
                    VisitAwaitExpression(node.Cast<IAwaitExpression>());
                    break;
                case SyntaxKind.BinaryExpression:
                    VisitBinaryExpression(node.Cast<IBinaryExpression>());
                    break;
                case SyntaxKind.CallExpression:
                    VisitCallExpression(node.Cast<ICallExpression>());
                    break;
                case SyntaxKind.ClassExpression:
                    VisitClassExpression(node.Cast<IClassExpression>());
                    break;
                case SyntaxKind.MultiLineCommentTrivia:
                    VisitMultiLineCommentExpression(node.Cast<IMultiLineCommentExpression>());
                    break;
                case SyntaxKind.SingleLineCommentTrivia:
                    VisitSingleLineCommentExpression(node.Cast<ISingleLineCommentExpression>());
                    break;
                case SyntaxKind.ConditionalExpression:
                    VisitConditionalExpression(node.Cast<IConditionalExpression>());
                    break;
                case SyntaxKind.SwitchExpression:
                    VisitSwitchExpression(node.Cast<ISwitchExpression>());
                    break;
                case SyntaxKind.SwitchExpressionClause:
                    VisitSwitchExpressionClause(node.Cast<ISwitchExpressionClause>());
                    break;
                case SyntaxKind.DeleteExpression:
                    VisitDeleteExpression(node.Cast<IDeleteExpression>());
                    break;
                case SyntaxKind.ElementAccessExpression:
                    VisitElementAccessExpression(node.Cast<IElementAccessExpression>());
                    break;
                case SyntaxKind.FunctionExpression:
                    VisitFunctionExpression(node.Cast<IFunctionExpression>());
                    break;
                case SyntaxKind.Identifier:
                    VisitIdentifier(node.Cast<IIdentifier>());
                    break;
                case SyntaxKind.PostfixUnaryExpression:
                    VisitPostfixUnaryExpression(node.Cast<IPostfixUnaryExpression>());
                    break;
                case SyntaxKind.PrefixUnaryExpression:
                    VisitPrefixUnaryExpression(node.Cast<IPrefixUnaryExpression>());
                    break;
                case SyntaxKind.NewExpression:
                    VisitNewExpression(node.Cast<INewExpression>());
                    break;
                case SyntaxKind.ObjectLiteralExpression:
                    VisitObjectLiteralExpression(node.Cast<IObjectLiteralExpression>());
                    break;
                case SyntaxKind.OmittedExpression:
                    VisitOmittedExpression(node.Cast<IOmittedExpression>());
                    break;
                case SyntaxKind.ParenthesizedExpression:
                    VisitParenthesizedExpression(node.Cast<IParenthesizedExpression>());
                    break;
                case SyntaxKind.PropertyAccessExpression:
                    VisitPropertyAccessExpression(node.Cast<IPropertyAccessExpression>());
                    break;
                case SyntaxKind.SpreadElementExpression:
                    VisitSpreadElementExpression(node.Cast<ISpreadElementExpression>());
                    break;
                case SyntaxKind.TypeAssertionExpression:
                    VisitTypeAssertionExpression(node.Cast<ITypeAssertion>());
                    break;
                case SyntaxKind.TypeOfExpression:
                    VisitTypeOfExpression(node.Cast<ITypeOfExpression>());
                    break;
                case SyntaxKind.VoidExpression:
                    VisitVoidExpression(node.Cast<IVoidExpression>());
                    break;
                case SyntaxKind.YieldExpression:
                    VisitYieldExpression(node.Cast<IYieldExpression>());
                    break;
                case SyntaxKind.TaggedTemplateExpression:
                    VisitTaggedTemplateExpression(node.Cast<ITaggedTemplateExpression>());
                    break;
                case SyntaxKind.TemplateExpression:
                    VisitTemplateExpression(node.Cast<ITemplateExpression>());
                    break;
                case SyntaxKind.StringLiteral:
                case SyntaxKind.NumericLiteral:
                case SyntaxKind.NoSubstitutionTemplateLiteral:
                    VisitLiteralExpressionDispatch(node.Cast<ILiteralExpression>());
                    break;
                case SyntaxKind.ThisKeyword:
                case SyntaxKind.SuperKeyword:
                case SyntaxKind.NullKeyword:
                case SyntaxKind.FalseKeyword:
                case SyntaxKind.TrueKeyword:
                    VisitPrimaryExpression(node.Cast<IPrimaryExpression>());
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        /// <nodoc />
        public virtual void VisitLiteralLike(ILiteralLikeNode node)
        {
        }

        /// <nodoc />
        public virtual void VisitLiteralExpressionDispatch(ILiteralExpression node)
        {
            if (node == null)
            {
                return;
            }

            switch (node.Kind)
            {
                case SyntaxKind.StringLiteral:
                case SyntaxKind.NoSubstitutionTemplateLiteral:
                    VisitStringLiteral(node.TryCast<IStringLiteral>() ?? node.TryCast<LiteralExpression>());
                    break;
                case SyntaxKind.NumericLiteral:
                    VisitNumericLiteral(node.Cast<IStringLiteral>());
                    break;
                case SyntaxKind.TemplateExpression:
                    VisitTemplateExpression(node.Cast<ITemplateExpression>());
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        /// <nodoc />
        public virtual void VisitPrimaryExpression(IPrimaryExpression node)
        {
        }

        /// <nodoc />
        public virtual void VisitStatementDispatch(IStatement node)
        {
            if (node == null)
            {
                return;
            }

            switch (node.Kind)
            {
                case SyntaxKind.BlankLineStatement:
                    VisitBlankLineStatement(node.Cast<IBlankLineStatement>());
                    break;
                case SyntaxKind.Block:
                    VisitBlock(node.Cast<IBlock>());
                    break;
                case SyntaxKind.SingleLineCommentTrivia:
                case SyntaxKind.MultiLineCommentTrivia:
                    VisitCommentStatement(node.Cast<ICommentStatement>());
                    break;
                case SyntaxKind.DebuggerStatement:
                    VisitDebuggerStatement(node.Cast<IDebuggerStatement>());
                    break;
                case SyntaxKind.EmptyStatement:
                    VisitEmptyStatement(node.Cast<IEmptyStatement>());
                    break;
                case SyntaxKind.ExpressionStatement:
                    VisitExpressionStatement(node.Cast<IExpressionStatement>());
                    break;
                case SyntaxKind.IfStatement:
                    VisitIfStatement(node.Cast<IIfStatement>());
                    break;
                case SyntaxKind.LabeledStatement:
                    VisitLabeledStatement(node.Cast<ILabeledStatement>());
                    break;
                case SyntaxKind.ModuleBlock:
                    VisitModuleBlock(node.Cast<IModuleBlock>());
                    break;
                case SyntaxKind.SwitchStatement:
                    VisitSwitchStatement(node.Cast<ISwitchStatement>());
                    break;
                case SyntaxKind.ThrowStatement:
                    VisitThrowStatement(node.Cast<IThrowStatement>());
                    break;
                case SyntaxKind.TryStatement:
                    VisitTryStatement(node.Cast<ITryStatement>());
                    break;
                case SyntaxKind.VariableStatement:
                    VisitVariableStatement(node.Cast<IVariableStatement>());
                    break;
                case SyntaxKind.WithStatement:
                    VisitWithStatement(node.Cast<IWithStatement>());
                    break;
                case SyntaxKind.BreakStatement:
                    VisitBreakStatement(node.Cast<IBreakOrContinueStatement>());
                    break;
                case SyntaxKind.ContinueStatement:
                    VisitContinueStatement(node.Cast<IBreakOrContinueStatement>());
                    break;
                case SyntaxKind.DoStatement:
                    VisitDoStatement(node.Cast<IDoStatement>());
                    break;
                case SyntaxKind.ForInStatement:
                    VisitForInStatement(node.Cast<IForInStatement>());
                    break;
                case SyntaxKind.ForOfStatement:
                    VisitForOfStatement(node.Cast<IForOfStatement>());
                    break;
                case SyntaxKind.ForStatement:
                    VisitForStatement(node.Cast<IForStatement>());
                    break;
                case SyntaxKind.ReturnStatement:
                    VisitReturnStatement(node.Cast<IReturnStatement>());
                    break;
                case SyntaxKind.WhileStatement:
                    VisitWhileStatement(node.Cast<IWhileStatement>());
                    break;

                case SyntaxKind.ImportDeclaration:
                    VisitImportDeclaration(node.Cast<IImportDeclaration>());
                    break;

                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.ExportAssignment:
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.ExportDeclaration:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                    VisitDeclarationStatementDispatch(node.Cast<IDeclarationStatement>());
                    break;

                default:
                    throw new NotImplementedException();
            }
        }

        /// <nodoc />
        public virtual void VisitDeclarationStatementDispatch(IDeclarationStatement node)
        {
            if (node == null)
            {
                return;
            }

            switch (node.Kind)
            {
                case SyntaxKind.ClassDeclaration:
                    VisitClassDeclaration(node.Cast<IClassDeclaration>());
                    break;
                case SyntaxKind.EnumDeclaration:
                    VisitEnumDeclaration(node.Cast<IEnumDeclaration>());
                    break;
                case SyntaxKind.ExportAssignment:
                    VisitExportAssignment(node.Cast<IExportAssignment>());
                    break;
                case SyntaxKind.ImportEqualsDeclaration:
                    VisitImportEqualsDeclaration(node.Cast<IImportEqualsDeclaration>());
                    break;
                case SyntaxKind.ExportDeclaration:
                    VisitExportDeclaration(node.Cast<IExportDeclaration>());
                    break;
                case SyntaxKind.FunctionDeclaration:
                    VisitFunctionDeclaration(node.Cast<IFunctionDeclaration>());
                    break;
                case SyntaxKind.InterfaceDeclaration:
                    VisitInterfaceDeclaration(node.Cast<IInterfaceDeclaration>());
                    break;
                case SyntaxKind.ModuleDeclaration:
                    VisitModuleDeclaration(node.Cast<IModuleDeclaration>());
                    break;
                case SyntaxKind.TypeAliasDeclaration:
                    VisitTypeAliasDeclaration(node.Cast<ITypeAliasDeclaration>());
                    break;
            }
        }

        /// <nodoc />
        public virtual void VisitClassElementDispatch(IClassElement node)
        {
            if (node == null)
            {
                return;
            }

            switch (node.Kind)
            {
                case SyntaxKind.Constructor:
                    VisitConstructorDeclaration(node.Cast<IConstructorDeclaration>());
                    break;
                case SyntaxKind.IndexSignature:
                    VisitIndexSignatureDeclaration(node.Cast<IIndexSignatureDeclaration>());
                    break;
                case SyntaxKind.MethodDeclaration:
                    VisitMethodDeclaration(node.Cast<IMethodDeclaration>());
                    break;
                case SyntaxKind.MissingDeclaration:
                    VisitMissingDeclaration(node.Cast<IMissingDeclaration>());
                    break;
                case SyntaxKind.SemicolonClassElement:
                    VisitSemicolonClassElement(node.Cast<ISemicolonClassElement>());
                    break;
                case SyntaxKind.GetAccessor:
                    VisitGetAccessDeclaration(node.Cast<IGetAccessorDeclaration>());
                    break;
                case SyntaxKind.SetAccessor:
                    VisitSetAccessDeclaration(node.Cast<ISetAccessorDeclaration>());
                    break;
                case SyntaxKind.PropertyAccessExpression:
                    VisitPropertyAccessExpression(node.Cast<IPropertyAccessExpression>());
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        /// <nodoc />
        public virtual void VisitTypeDispatch(ITypeNode node)
        {
            if (node == null)
            {
                return;
            }

            switch (node.Kind)
            {
                case SyntaxKind.ArrayType:
                    VisitArrayType(node.Cast<IArrayTypeNode>());
                    break;
                case SyntaxKind.ExpressionWithTypeArguments:
                    VisitExpressionWithTypeArguments(node.Cast<IExpressionWithTypeArguments>());
                    break;
                case SyntaxKind.ParenthesizedType:
                    VisitParenthesizedType(node.Cast<IParenthesizedTypeNode>());
                    break;
                case SyntaxKind.StringLiteralType:
                    VisitStringLiteralType(node.Cast<IStringLiteralTypeNode>());
                    break;
                case SyntaxKind.ThisType:
                    VisitThisType(node.Cast<IThisTypeNode>());
                    break;
                case SyntaxKind.TupleType:
                    VisitTupleType(node.Cast<ITupleTypeNode>());
                    break;
                case SyntaxKind.TypeLiteral:
                    VisitTypeLiteral(node.Cast<ITypeLiteralNode>());
                    break;
                case SyntaxKind.TypePredicate:
                    VisitTypePredicate(node.Cast<ITypePredicateNode>());
                    break;
                case SyntaxKind.TypeQuery:
                    VisitTypeQuery(node.Cast<ITypeQueryNode>());
                    break;
                case SyntaxKind.TypeReference:
                    VisitTypeReference(node.Cast<ITypeReferenceNode>());
                    break;
                case SyntaxKind.ConstructorType:
                    VisitFunctionOrConstructorTypeNode(node.Cast<IFunctionOrConstructorTypeNode>());
                    break;
                case SyntaxKind.FunctionType:
                    VisitFunctionOrConstructorTypeNode(node.Cast<IFunctionOrConstructorTypeNode>());
                    break;
                case SyntaxKind.IntersectionType:
                    VisitIntersectionType(node.Cast<IIntersectionTypeNode>());
                    break;
                case SyntaxKind.UnionType:
                    VisitUnionType(node.Cast<IUnionTypeNode>());
                    break;
                case SyntaxKind.AnyKeyword:
                case SyntaxKind.StringKeyword:
                case SyntaxKind.NumberKeyword:
                case SyntaxKind.VoidKeyword:
                case SyntaxKind.BooleanKeyword:
                    VisitKeywordType(node.Cast<ITypeNode>());
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        /// <nodoc />
        public virtual void VisitTypeElementDispatch(ITypeElement node)
        {
            if (node == null)
            {
                return;
            }

            switch (node.Kind)
            {
                case SyntaxKind.CallSignature:
                    VisitCallSignatureDeclaration(node.Cast<ICallSignatureDeclaration>());
                    break;
                case SyntaxKind.ConstructSignature:
                    VisitConstructSignatureDeclaration(node.Cast<IConstructSignatureDeclaration>());
                    break;
                case SyntaxKind.IndexSignature:
                    VisitIndexSignatureDeclaration(node.Cast<IIndexSignatureDeclaration>());
                    break;
                case SyntaxKind.MethodSignature:
                    VisitMethodSignature(node.Cast<IMethodSignature>());
                    break;
                case SyntaxKind.MissingDeclaration:
                    VisitMissingDeclaration(node.Cast<IMissingDeclaration>());
                    break;
                case SyntaxKind.PropertySignature:
                    VisitPropertySignature(node.Cast<IPropertySignature>());
                    break;
                case SyntaxKind.SingleLineCommentTrivia:
                case SyntaxKind.MultiLineCommentTrivia:
                    VisitCommentAsTypeElement(node.Cast<CommentAsTypeElement>());
                    break;
            }
        }

        #endregion

        #region Expressions

        /// <nodoc />
        public virtual void VisitArayLiteralExpression(IArrayLiteralExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes(node.Elements, VisitExpressionDispatch);
        }

        /// <nodoc />
        public virtual void VisitArrowFunction(IArrowFunction node)
        {
            if (node == null)
            {
                return;
            }

            VisitFunctionLikeDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitAsExpression(IAsExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitTypeDispatch(node.Type);
        }

        /// <nodoc />
        public virtual void VisitAwaitExpression(IAwaitExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitBinaryExpression(IBinaryExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Left);
            VisitExpressionDispatch(node.Right);
        }

        /// <nodoc />
        public virtual void VisitCallExpression(ICallExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitNodes(node.TypeArguments, VisitTypeDispatch);
            VisitNodes(node.Arguments, VisitExpressionDispatch);
        }

        /// <nodoc />
        public virtual void VisitClassExpression(IClassExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitClassLikeDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitCommentExpression(ICommentExpression node)
        {
        }

        /// <nodoc />
        public virtual void VisitMultiLineCommentExpression(IMultiLineCommentExpression node)
        {
        }

        /// <nodoc />
        public virtual void VisitSingleLineCommentExpression(ISingleLineCommentExpression node)
        {
        }

        /// <nodoc />
        public virtual void VisitConditionalExpression(IConditionalExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Condition);
            VisitExpressionDispatch(node.WhenTrue);
            VisitExpressionDispatch(node.WhenFalse);
        }

        /// <nodoc />
        public virtual void VisitSwitchExpression(ISwitchExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            foreach (var clause in node.Clauses)
            {
                VisitExpressionDispatch(clause);
            }
        }
        
        /// <nodoc />
        public virtual void VisitSwitchExpressionClause(ISwitchExpressionClause node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Match);
            VisitExpressionDispatch(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitDeleteExpression(IDeleteExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitElementAccessExpression(IElementAccessExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitExpressionDispatch(node.ArgumentExpression);
        }

        /// <nodoc />
        public virtual void VisitFunctionExpression(IFunctionExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitFunctionLikeDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitPostfixUnaryExpression(IPostfixUnaryExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Operand);
        }

        /// <nodoc />
        public virtual void VisitPrefixUnaryExpression(IPrefixUnaryExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Operand);
        }

        /// <nodoc />
        public virtual void VisitNewExpression(INewExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitCallExpression(node);
        }

        /// <nodoc />
        public virtual void VisitObjectLiteralExpression(IObjectLiteralExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes(node.Properties, VisitObjectLiteralElement);
        }

        /// <nodoc />
        public virtual void VisitObjectLiteralElement(IObjectLiteralElement node)
        {
            if (node == null)
            {
                return;
            }

            switch (node.Kind)
            {
                case SyntaxKind.GetAccessor:
                    VisitGetAccessorDeclaration(node.Cast<IGetAccessorDeclaration>());
                    break;
                case SyntaxKind.SetAccessor:
                    VisitSetAccessorDeclaration(node.Cast<ISetAccessorDeclaration>());
                    break;
                case SyntaxKind.MethodDeclaration:
                    VisitMethodDeclaration(node.Cast<IMethodDeclaration>());
                    break;
                case SyntaxKind.MissingDeclaration:
                    VisitMissingDeclaration(node.Cast<IMissingDeclaration>());
                    break;
                case SyntaxKind.PropertyAssignment:
                    VisitPropertyAssignment(node.Cast<IPropertyAssignment>());
                    break;
                case SyntaxKind.ShorthandPropertyAssignment:
                    VisitShorthandPropertyAssignment(node.Cast<IShorthandPropertyAssignment>());
                    break;
                case SyntaxKind.MultiLineCommentTrivia:
                case SyntaxKind.SingleLineCommentTrivia:
                    VisitCommentAsLiteralElement(node.Cast<CommentAsLiteralElement>());
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        /// <nodoc />
        public virtual void VisitGetAccessorDeclaration(IGetAccessorDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitAccessorDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitSetAccessorDeclaration(ISetAccessorDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitAccessorDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitAccessorDeclaration(IAccessorDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitFunctionLikeDeclaration(node);
            VisitClassElement(node);
        }

        /// <nodoc />
        public virtual void VisitIndexSignatureDeclaration(IIndexSignatureDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitFunctionLikeDeclaration(node);
            VisitClassElement(node);
        }

        /// <nodoc />
        public virtual void VisitMethodDeclaration(IMethodDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitFunctionLikeDeclaration(node);
            VisitClassElement(node);
        }

        /// <nodoc />
        public virtual void VisitMissingDeclaration(IMissingDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitClassElement(node);
        }

        /// <nodoc />
        public virtual void VisitSemicolonClassElement(ISemicolonClassElement node)
        {
            if (node == null)
            {
                return;
            }

            VisitClassElement(node);
        }

        /// <nodoc />
        public virtual void VisitGetAccessDeclaration(IGetAccessorDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitAccessorDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitSetAccessDeclaration(ISetAccessorDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitAccessorDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitPropertyAssignment(IPropertyAssignment node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifier(node.Name);
            VisitExpressionDispatch(node.Initializer);
        }

        /// <nodoc />
        public virtual void VisitShorthandPropertyAssignment(IShorthandPropertyAssignment node)
        {
            if (node == null)
            {
                return;
            }

            VisitPropertyName(node.Name);
            VisitExpressionDispatch(node.ObjectAssignmentInitializer);
        }

        /// <nodoc />
        public virtual void VisitCommentAsLiteralElement(CommentAsLiteralElement node)
        {
            if (node == null)
            {
                return;
            }

            VisitCommentExpression(node.CommentExpression);
        }

        /// <nodoc />
        public virtual void VisitOmittedExpression(IOmittedExpression node)
        {
        }

        /// <nodoc />
        public virtual void VisitParenthesizedExpression(IParenthesizedExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitPropertyAccessExpression(IPropertyAccessExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitIdentifier(node.Name);
            VisitDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitSpreadElementExpression(ISpreadElementExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitTypeAssertionExpression(ITypeAssertion node)
        {
            if (node == null)
            {
                return;
            }

            VisitTypeDispatch(node.Type);
            VisitExpressionDispatch(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitTypeOfExpression(ITypeOfExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitVoidExpression(IVoidExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitYieldExpression(IYieldExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
        }

        /// <nodoc />
        public virtual void VisitTaggedTemplateExpression(ITaggedTemplateExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Tag);
            VisitLiteralExpressionDispatch(node.Template);
        }

        /// <nodoc />
        public virtual void VisitStringLiteral(IStringLiteral node)
        {
            if (node == null)
            {
                return;
            }

            VisitLiteralLike(node);
        }

        /// <nodoc />
        public virtual void VisitNumericLiteral(ILiteralExpression node)
        {
        }

        /// <nodoc />
        public virtual void VisitTemplateExpression(ITemplateExpression node)
        {
            if (node == null)
            {
                return;
            }

            VisitTemplateLiteralFragment(node.Head);
            VisitNodes<ITemplateSpan>(node.TemplateSpans, VisitTemplateSpan);
        }

        /// <nodoc />
        public virtual void VisitTemplateLiteralFragment(ITemplateLiteralFragment node)
        {
            if (node == null)
            {
                return;
            }

            VisitLiteralLike(node);
        }

        /// <nodoc />
        public virtual void VisitTemplateSpan(ITemplateSpan node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitTemplateLiteralFragment(node.Literal);
        }

        #endregion

        #region Statements

        /// <nodoc />
        public virtual void VisitStatement(IStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitNode(node);
        }

        /// <nodoc />
        public virtual void VisitStatementContainer(IStatementsContainer node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes(node.Statements, VisitStatementDispatch);
        }

        /// <nodoc />
        public virtual void VisitBlankLineStatement(IBlankLineStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitBlock(IBlock node)
        {
            if (node == null)
            {
                return;
            }

            VisitStatementContainer(node);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitCommentStatement(ICommentStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitCommentExpression(node.CommentExpression);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitDebuggerStatement(IDebuggerStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitEmptyStatement(IEmptyStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitIfStatement(IIfStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitStatementDispatch(node.ThenStatement);
            if (node.ElseStatement.HasValue)
            {
                VisitStatementDispatch(node.ElseStatement.Value);
            }

            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitLabeledStatement(ILabeledStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifier(node.Label);
            VisitStatementDispatch(node.Statement);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitModuleBlock(IModuleBlock node)
        {
            if (node == null)
            {
                return;
            }

            VisitStatementContainer(node);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitSwitchStatement(ISwitchStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitCaseBlock(node.CaseBlock);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitCaseBlock(ICaseBlock node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes(node.Clauses, VisitCaseClauseOrDefaultClause);
        }

        /// <nodoc />
        public virtual void VisitCaseClauseOrDefaultClause(CaseClauseOrDefaultClause node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Kind == SyntaxKind.CaseClause)
            {
                VisitCaseClause(node.Cast<ICaseClause>());
            }
            else
            {
                VisitDefaultClause(node.Cast<IDefaultClause>());
            }
        }

        /// <nodoc />
        public virtual void VisitCaseClause(ICaseClause node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitStatementContainer(node);
        }

        /// <nodoc />
        public virtual void VisitDefaultClause(IDefaultClause node)
        {
            if (node == null)
            {
                return;
            }

            VisitStatementContainer(node);
        }

        /// <nodoc />
        public virtual void VisitThrowStatement(IThrowStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitTryStatement(ITryStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitBlock(node.TryBlock);
            VisitCatchClause(node.CatchClause);
            VisitBlock(node.FinallyBlock);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitCatchClause(ICatchClause node)
        {
            if (node == null)
            {
                return;
            }

            VisitVariableDeclaration(node.VariableDeclaration);
            VisitBlock(node.Block);
        }

        /// <nodoc />
        public virtual void VisitVariableDeclaration(IVariableDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifierOrBindingPattern(node.Name);
            VisitTypeDispatch(node.Type);
            VisitExpressionDispatch(node.Initializer);
            VisitDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitVariableStatement(IVariableStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitVariableDeclarationList(node.DeclarationList);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitVariableDeclarationList(IVariableDeclarationList node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes<IVariableDeclaration>(node.Declarations, VisitVariableDeclaration);
            VisitNode(node);
        }

        /// <nodoc />
        public virtual void VisitWithStatement(IWithStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitStatementDispatch(node.Statement);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitBreakStatement(IBreakOrContinueStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitBreakOrContinueStatement(node);
        }

        /// <nodoc />
        public virtual void VisitContinueStatement(IBreakOrContinueStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitBreakOrContinueStatement(node);
        }

        /// <nodoc />
        public virtual void VisitBreakOrContinueStatement(IBreakOrContinueStatement node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Label != null)
            {
                VisitIdentifier(node.Label);
            }

            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitDoStatement(IDoStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitIterationStatement(node);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitForInStatement(IForInStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitVariableDeclarationListOrExpression(node.Initializer);
            VisitExpressionDispatch(node.Expression);
            VisitIterationStatement(node);
        }

        /// <nodoc />
        public virtual void VisitForOfStatement(IForOfStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitVariableDeclarationListOrExpression(node.Initializer);
            VisitExpressionDispatch(node.Expression);
            VisitIterationStatement(node);
        }

        /// <nodoc />
        public virtual void VisitForStatement(IForStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitVariableDeclarationListOrExpression(node.Initializer);
            VisitExpressionDispatch(node.Condition);
            VisitExpressionDispatch(node.Incrementor);
            VisitIterationStatement(node);
        }

        /// <nodoc />
        public virtual void VisitVariableDeclarationListOrExpression(VariableDeclarationListOrExpression node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Kind == SyntaxKind.VariableDeclarationList)
            {
                VisitVariableDeclarationList(node.Cast<IVariableDeclarationList>());
            }
            else
            {
                VisitExpressionDispatch(node.Cast<IExpression>());
            }
        }

        /// <nodoc />
        public virtual void VisitIterationStatement(IIterationStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitStatementDispatch(node.Statement);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitReturnStatement(IReturnStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionStatement(node);
        }

        /// <nodoc />
        public virtual void VisitExpressionStatement(IExpressionStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitWhileStatement(IWhileStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitIterationStatement(node);
        }

        #endregion

        #region DeclarationStatements

        /// <nodoc />
        public virtual void VisitDeclaration(IDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitNode(node);
        }

        /// <nodoc />
        public virtual void VisitDeclarationStatement(IDeclarationStatement node)
        {
            if (node == null)
            {
                return;
            }

            VisitDeclaration(node);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitClassDeclaration(IClassDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitClassLikeDeclaration(node);
            VisitDeclarationStatement(node);
        }

        /// <nodoc />
        public virtual void VisitClassLikeDeclaration(IClassLikeDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifier(node.Name);
            VisitNodes(node.TypeParameters, VisitTypeParameterDeclaration);
            VisitNodes(node.HeritageClauses, VisitHeritageClause);
            VisitNodes(node.Members, VisitClassElementDispatch);
            VisitDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitTypeParameterDeclaration(ITypeParameterDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifier(node.Name);
            VisitTypeDispatch(node.Constraint);
            VisitExpressionDispatch(node.Expression);
            VisitDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitHeritageClause(IHeritageClause node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes(node.Types, VisitExpressionWithTypeArguments);
        }

        /// <nodoc />
        public virtual void VisitClassElement(IClassElement node)
        {
            if (node == null)
            {
                return;
            }

            VisitDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitConstructorDeclaration(IConstructorDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitFunctionLikeDeclaration(node);
            VisitClassElement(node);
        }

        /// <nodoc />
        public virtual void VisitConciseBody(ConciseBody node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Kind == SyntaxKind.Block)
            {
                VisitBlock(node.Cast<IBlock>());
            }
            else
            {
                VisitExpressionDispatch(node.Cast<IExpression>());
            }
        }

        /// <nodoc />
        public virtual void VisitParameterDeclaration(IParameterDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifierOrBindingPattern(node.Name);

            if (node.Type != null)
            {
                VisitTypeDispatch(node.Type);
            }

            if (node.Initializer != null)
            {
                VisitExpressionDispatch(node.Initializer);
            }
        }

        /// <nodoc />
        public virtual void VisitEnumDeclaration(IEnumDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifier(node.Name);
            VisitNodes(node.Members, VisitEnumMember);
            VisitDeclarationStatement(node);
        }

        /// <nodoc />
        public virtual void VisitEnumMember(IEnumMember node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Kind == SyntaxKind.SingleLineCommentTrivia || node.Kind == SyntaxKind.MultiLineCommentTrivia)
            {
                var comment = node.Cast<CommentAsEnumMember>();
                VisitCommentExpression(comment.CommentExpression);
            }
            else
            {
                if (node.Name != null)
                {
                    VisitDeclarationName(node.Name);
                }

                VisitOptional(node.Initializer, VisitExpressionDispatch);
            }
        }

        /// <nodoc />
        public virtual void VisitDeclarationName(DeclarationName node)
        {
            if (node == null)
            {
                return;
            }

            switch (node.Kind)
            {
                case SyntaxKind.Identifier:
                    VisitIdentifier(node.Cast<IIdentifier>());
                    break;
                case SyntaxKind.ComputedPropertyName:
                    VisitComputedPropertyName(node.Cast<IComputedPropertyName>());
                    break;
                case SyntaxKind.ArrayBindingPattern:
                    VisitArrayBindingPattern(node.Cast<IArrayBindingPattern>());
                    break;
                case SyntaxKind.ObjectBindingPattern:
                    VisitObjectBindingPattern(node.Cast<IObjectBindingPattern>());
                    break;
                default:
                    VisitLiteralExpressionDispatch(node.Cast<ILiteralExpression>());
                    break;
            }
        }

        /// <nodoc />
        public virtual void VisitBindingPattern(IBindingPattern node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes(node.Elements, VisitBindingElement);
            VisitNode(node);
        }

        /// <nodoc />
        public virtual void VisitArrayBindingPattern(IArrayBindingPattern node)
        {
            if (node == null)
            {
                return;
            }

            VisitBindingPattern(node);
        }

        /// <nodoc />
        public virtual void VisitObjectBindingPattern(IObjectBindingPattern node)
        {
            if (node == null)
            {
                return;
            }

            VisitBindingPattern(node);
        }

        /// <nodoc />
        public virtual void VisitBindingElement(IBindingElement node)
        {
            if (node == null)
            {
                return;
            }

            VisitPropertyName(node.PropertyName);
            VisitIdentifierOrBindingPattern(node.Name);
            VisitExpressionDispatch(node.Initializer);
            VisitDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitIdentifierOrBindingPattern(IdentifierOrBindingPattern node)
        {
            if (node == null)
            {
                return;
            }

            switch (node.Kind)
            {
                case SyntaxKind.Identifier:
                    VisitIdentifier(node.Cast<IIdentifier>());
                    break;
                case SyntaxKind.ObjectBindingPattern:
                    VisitObjectBindingPattern(node.Cast<IObjectBindingPattern>());
                    break;
                case SyntaxKind.ArrayBindingPattern:
                    VisitArrayBindingPattern(node.Cast<IArrayBindingPattern>());
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        /// <nodoc />
        public virtual void VisitExportAssignment(IExportAssignment node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifier(node.Name);
            VisitExpressionDispatch(node.Expression);
            VisitDeclarationStatement(node);
        }

        /// <nodoc />
        public virtual void VisitImportDeclaration(IImportDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitImportClause(node.ImportClause);
            VisitExpressionDispatch(node.ModuleSpecifier);
            VisitStatement(node);
        }

        /// <nodoc />
        public virtual void VisitImportClause(IImportClause node)
        {
            if (node == null)
            {
                return;
            }

            if (node.NamedBindings.Kind == SyntaxKind.NamedImports)
            {
                VisitNamedImports(node.NamedBindings.Cast<INamedImports>());
            }
            else
            {
                VisitNamespaceImport(node.NamedBindings.Cast<INamespaceImport>());
            }
        }

        /// <nodoc />
        public virtual void VisitNamedImports(INamedImports node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes(node.Elements, VisitImportSpecifier);
            VisitNode(node);
        }

        /// <nodoc />
        public virtual void VisitNamespaceImport(INamespaceImport node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifier(node.Name);
            VisitDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitImportEqualsDeclaration(IImportEqualsDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifier(node.Name);
            VisitEntityNameOrExternalModuleReference(node.ModuleReference);
            VisitDeclarationStatement(node);
        }

        /// <nodoc />
        public virtual void VisitEntityNameOrExternalModuleReference(EntityNameOrExternalModuleReference node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Kind == SyntaxKind.ExternalModuleReference)
            {
                VisitExternalModuleReference(node.Cast<IExternalModuleReference>());
            }
            else
            {
                VisitEntityName((EntityName)node);
            }
        }

        /// <nodoc />
        public virtual void VisitExternalModuleReference(IExternalModuleReference node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitNode(node);
        }

        /// <nodoc />
        public virtual void VisitExportDeclaration(IExportDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Name != null)
            {
                VisitIdentifier(node.Name);
            }

            if (node.ExportClause != null)
            {
                VisitNamedExports(node.ExportClause);
            }

            VisitExpressionDispatch(node.ModuleSpecifier);
            VisitDeclarationStatement(node);
        }

        /// <nodoc />
        public virtual void VisitNamedExports(INamedExports node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes(node.Elements, VisitExportSpecifier);
            VisitNode(node);
        }

        /// <nodoc />
        public virtual void VisitExportSpecifier(IExportSpecifier node)
        {
            if (node == null)
            {
                return;
            }

            VisitImportOrExportSpcifier(node);
        }

        /// <nodoc />
        public virtual void VisitImportSpecifier(IImportSpecifier node)
        {
            if (node == null)
            {
                return;
            }

            VisitImportOrExportSpcifier(node);
        }

        /// <nodoc />
        public virtual void VisitImportOrExportSpcifier(IImportOrExportSpecifier node)
        {
            if (node == null)
            {
                return;
            }

            if (node.PropertyName != null)
            {
                VisitIdentifier(node.PropertyName);
            }

            VisitIdentifier(node.Name);
            VisitDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitFunctionDeclaration(IFunctionDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitFunctionLikeDeclaration(node);
            VisitDeclarationStatement(node);
        }

        /// <nodoc />
        public virtual void VisitInterfaceDeclaration(IInterfaceDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifier(node.Name);
            VisitNodes(node.TypeParameters, VisitTypeParameterDeclaration);
            VisitNodes(node.HeritageClauses, VisitHeritageClause);
            VisitNodes(node.Members, VisitTypeElementDispatch);
            VisitDeclarationStatement(node);
        }

        /// <nodoc />
        public virtual void VisitModuleDeclaration(IModuleDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifierOrLiteralExpression(node.Name);
            VisitModuleBody(node.Body);
            VisitDeclarationStatement(node);
        }

        /// <nodoc />
        public virtual void VisitIdentifierOrLiteralExpression(IdentifierOrLiteralExpression node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Kind == SyntaxKind.Identifier)
            {
                VisitIdentifier(node.Cast<IIdentifier>());
            }
            else
            {
                VisitLiteralExpressionDispatch(node.Cast<ILiteralExpression>());
            }
        }

        /// <nodoc />
        public virtual void VisitModuleBody(ModuleBody node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Kind == SyntaxKind.ModuleDeclaration)
            {
                VisitModuleDeclaration(node.Cast<IModuleDeclaration>());
            }
            else
            {
                VisitModuleBlock(node.Cast<IModuleBlock>());
            }
        }

        /// <nodoc />
        public virtual void VisitTypeAliasDeclaration(ITypeAliasDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifier(node.Name);
            VisitNodes(node.TypeParameters, VisitTypeParameterDeclaration);
            VisitTypeDispatch(node.Type);
            VisitDeclarationStatement(node);
        }

        #endregion

        #region Types

        /// <nodoc />
        public virtual void VisitTypeNode(ITypeNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitNode(node);
        }

        /// <nodoc />
        public virtual void VisitCallSignatureDeclaration(ICallSignatureDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitFunctionLikeDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitConstructSignatureDeclaration(IConstructSignatureDeclaration node)
        {
            if (node == null)
            {
                return;
            }

            VisitFunctionLikeDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitMethodSignature(IMethodSignature node)
        {
            if (node == null)
            {
                return;
            }

            VisitPropertyName(node.Name);
            VisitFunctionLikeDeclaration(node);
        }

        /// <nodoc />
        public virtual void VisitPropertySignature(IPropertySignature node)
        {
            if (node == null)
            {
                return;
            }

            VisitPropertyName(node.Name);
            VisitTypeDispatch(node.Type);
            VisitExpressionDispatch(node.Initializer);
        }

        /// <nodoc />
        public virtual void VisitCommentAsTypeElement(CommentAsTypeElement node)
        {
            if (node == null)
            {
                return;
            }

            VisitCommentExpression(node.CommentExpression);
        }

        /// <nodoc />
        public virtual void VisitArrayType(IArrayTypeNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitTypeDispatch(node.ElementType);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitExpressionWithTypeArguments(IExpressionWithTypeArguments node)
        {
            if (node == null)
            {
                return;
            }

            VisitExpressionDispatch(node.Expression);
            VisitNodes(node.TypeArguments, VisitTypeDispatch);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitParenthesizedType(IParenthesizedTypeNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitTypeDispatch(node.Type);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitStringLiteralType(IStringLiteralTypeNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitLiteralLike(node);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitThisType(IThisTypeNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitTupleType(ITupleTypeNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes(node.ElementTypes, VisitTypeDispatch);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitTypeLiteral(ITypeLiteralNode node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Name != null)
            {
                VisitDeclarationName(node.Name);
            }

            VisitNodes(node.Members, VisitTypeElementDispatch);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitTypePredicate(ITypePredicateNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitIdentifierOrThisTypeUnionNode(node.ParameterName);
            VisitTypeDispatch(node.Type);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitIdentifierOrThisTypeUnionNode(IdentifierOrThisTypeUnionNode node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Kind == SyntaxKind.Identifier)
            {
                VisitIdentifier(node.Cast<IIdentifier>());
            }
            else
            {
                VisitThisType(node.Cast<IThisTypeNode>());
            }
        }

        /// <nodoc />
        public virtual void VisitTypeQuery(ITypeQueryNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitEntityName(node.ExprName);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitTypeReference(ITypeReferenceNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitEntityName(node.TypeName);
            VisitNodes(node.TypeArguments, VisitTypeDispatch);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitFunctionOrConstructorTypeNode(IFunctionOrConstructorTypeNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitFunctionLikeDeclaration(node);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitConstructorType(IConstructorTypeNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitFunctionLikeDeclaration(node);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitFunctionType(IFunctionTypeNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitFunctionLikeDeclaration(node);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitIntersectionType(IIntersectionTypeNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes(node.Types, VisitTypeDispatch);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitUnionType(IUnionTypeNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitNodes(node.Types, VisitTypeDispatch);
            VisitTypeNode(node);
        }

        /// <nodoc />
        public virtual void VisitKeywordType(ITypeNode node)
        {
            if (node == null)
            {
                return;
            }

            VisitTypeNode(node);
        }

        #endregion
    }
}
