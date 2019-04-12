// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using TypeScript.Net.Types;

namespace TypeScript.Net.Printing
{
    /// <summary>
    /// A node walker that does not skip null values and does not inline array fields.
    /// This allows comparing the structure of two nodes with the same kind.
    /// </summary>
    public static class NodeWalkerEx
    {
        /// <nodoc/>
        public static IEnumerable<NodeOrNodesOrNull> GetChildNodes(NodeOrNodesOrNull node)
        {
            switch (node.Type)
            {
                case NodeOrNodesOrNullType.Node:
                    foreach (var n in GetChildNodes(node.Node))
                    {
                        yield return n;
                    }

                    break;

                case NodeOrNodesOrNullType.Nodes:
                    foreach (var n in node.Nodes.AsStructEnumerable())
                    {
                        yield return new NodeOrNodesOrNull(n);
                    }

                    break;

                case NodeOrNodesOrNullType.Null:
                    break;
            }
        }

        /// <nodoc/>
        public static IEnumerable<NodeOrNodesOrNull> GetChildNodes(INode node)
        {
            var l = new List<NodeOrNodesOrNull>();
            ForEachChild<object>(node, child =>
            {
                l.Add(child);
                return null;
            });

            return l;
        }

        /// <nodoc/>
        public static IEnumerable<NodeOrNodesOrNull> GetDescendantNodes(INode node, bool includeSelf = false)
        {
            if (includeSelf)
            {
                yield return new NodeOrNodesOrNull(node);
            }

            foreach (var directChild in GetChildNodes(node))
            {
                yield return directChild;
                if (directChild.Type == NodeOrNodesOrNullType.Node)
                {
                    foreach (var childDescendant in GetDescendantNodes(directChild.Node))
                    {
                        yield return childDescendant;
                    }
                }
                else if (directChild.Type == NodeOrNodesOrNullType.Nodes)
                {
                    foreach (var childChild in directChild.Nodes.AsStructEnumerable())
                    {
                        foreach (var d2 in GetDescendantNodes(childChild, true))
                        {
                            yield return d2;
                        }
                    }
                }
            }
        }

        private static T VisitNode<T>(Func<NodeOrNodesOrNull, T> cbNode, INode node)
        {
            if (node != null)
            {
                return cbNode(new NodeOrNodesOrNull(node));
            }

            return default(T);
        }

        private static T VisitNodeArray<T>(Func<NodeOrNodesOrNull, T> cbNodes, INodeArray<INode> nodes)
        {
            if (nodes != null)
            {
                return cbNodes(new NodeOrNodesOrNull(nodes));
            }

            return default(T);
        }

        private static T VisitEachNode<T>(Func<NodeOrNodesOrNull, T> cbNode, INodeArray<INode> nodes) where T : class
        {
            if (nodes != null)
            {
                foreach (var node in nodes.AsStructEnumerable())
                {
                    var result = cbNode(new NodeOrNodesOrNull(node));
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return default(T);
        }

        private static T ForEachChild<T>(INode node, Func<NodeOrNodesOrNull, T> cbNode, Func<NodeOrNodesOrNull, T> cbNodeArray = null) where T : class
        {
            if (node == null)
            {
                return default(T);
            }

            // The visitXXX functions could be written as local functions that close over the cbNode and cbNodeArray
            // callback parameters, but that causes a closure allocation for each invocation with noticeable effects
            // on performance.
            Func<Func<NodeOrNodesOrNull, T>, INodeArray<INode>, T> visitNodes = cbNodeArray != null
                ? (Func<Func<NodeOrNodesOrNull, T>, INodeArray<INode>, T>)VisitNodeArray
                : VisitEachNode;

            var cbNodes = cbNodeArray ?? cbNode;
            switch (node.Kind)
            {
                case SyntaxKind.QualifiedName:
                    {
                        var concreteNode = node.Cast<IQualifiedName>();
                        return VisitNode(cbNode, concreteNode.Left) ??
                                VisitNode(cbNode, concreteNode.Right);
                    }

                case SyntaxKind.TypeParameter:
                    {
                        var concreteNode = node.Cast<ITypeParameterDeclaration>();
                        return VisitNode(cbNode, concreteNode.Name) ??
                            VisitNode(cbNode, concreteNode.Constraint) ??
                            VisitNode(cbNode, concreteNode.Expression);
                    }

                case SyntaxKind.ShorthandPropertyAssignment:
                    {
                        var concreteNode = node.Cast<IShorthandPropertyAssignment>();
                        return visitNodes(cbNodes, node.Decorators) ??
                                                    visitNodes(cbNodes, node.Modifiers) ??
                                                    VisitNode(cbNode, concreteNode.Name) ??
                                                    VisitNode(cbNode, concreteNode.QuestionToken.ValueOrDefault) ??
                                                    VisitNode(cbNode, concreteNode.EqualsToken.ValueOrDefault) ??
                                                    VisitNode(cbNode, concreteNode.ObjectAssignmentInitializer);
                    }

                case SyntaxKind.Parameter:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.PropertyAssignment:
                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.BindingElement:
                    {
                        var concreteNode = node.Cast<IVariableLikeDeclaration>();
                        return visitNodes(cbNodes, node.Decorators) ??
                           visitNodes(cbNodes, node.Modifiers) ??
                           VisitNode(cbNode, concreteNode.PropertyName) ??
                           VisitNode(cbNode, concreteNode.DotDotDotToken.ValueOrDefault) ??
                           VisitNode(cbNode, concreteNode.Name) ??
                           VisitNode(cbNode, concreteNode.QuestionToken.ValueOrDefault) ??
                           VisitNode(cbNode, concreteNode.Type) ??
                           VisitNode(cbNode, concreteNode.Initializer);
                    }

                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                case SyntaxKind.CallSignature:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.IndexSignature:
                    {
                        var concreteNode = node.Cast<ISignatureDeclaration>();
                        return visitNodes(cbNodes, node.Decorators) ??
                           visitNodes(cbNodes, node.Modifiers) ??
                           visitNodes(cbNodes, concreteNode.TypeParameters) ??
                           visitNodes(cbNodes, concreteNode.Parameters) ??
                           VisitNode(cbNode, concreteNode.Type);
                    }

                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.ArrowFunction:
                    {
                        var concreteNode = node.Cast<IFunctionLikeDeclaration>();
                        return visitNodes(cbNodes, node.Decorators) ??
                           visitNodes(cbNodes, node.Modifiers) ??
                           VisitNode(cbNode, concreteNode.AsteriskToken.ValueOrDefault) ??
                           VisitNode(cbNode, concreteNode.Name) ??
                           VisitNode(cbNode, concreteNode.QuestionToken.ValueOrDefault) ??
                           visitNodes(cbNodes, concreteNode.TypeParameters) ??
                           visitNodes(cbNodes, concreteNode.Parameters) ??
                           VisitNode(cbNode, concreteNode.Type) ??
                           VisitNode(cbNode, node.As<IArrowFunction>()?.EqualsGreaterThanToken) ??
                           VisitNode(cbNode, concreteNode.Body);
                    }

                case SyntaxKind.TypeReference:
                    {
                        var concreteNode = node.Cast<ITypeReferenceNode>();
                        return VisitNode(cbNode, concreteNode.TypeName) ??
                               visitNodes(cbNodes, concreteNode.TypeArguments);
                    }

                case SyntaxKind.TypePredicate:
                    {
                        var concreteNode = node.Cast<ITypePredicateNode>();
                        return VisitNode(cbNode, concreteNode.ParameterName) ??
                           VisitNode(cbNode, concreteNode.Type);
                    }

                case SyntaxKind.TypeQuery:
                    return VisitNode(cbNode, node.Cast<ITypeQueryNode>().ExprName);
                case SyntaxKind.TypeLiteral:
                    return visitNodes(cbNodes, node.Cast<ITypeLiteralNode>().Members);
                case SyntaxKind.ArrayType:
                    return VisitNode(cbNode, node.Cast<IArrayTypeNode>().ElementType);
                case SyntaxKind.TupleType:
                    return visitNodes(cbNodes, node.Cast<ITupleTypeNode>().ElementTypes);
                case SyntaxKind.UnionType:
                case SyntaxKind.IntersectionType:
                    return visitNodes(cbNodes, node.Cast<IUnionOrIntersectionTypeNode>().Types);
                case SyntaxKind.ParenthesizedType:
                    return VisitNode(cbNode, node.Cast<IParenthesizedTypeNode>().Type);
                case SyntaxKind.ObjectBindingPattern:
                case SyntaxKind.ArrayBindingPattern:
                    return visitNodes(cbNodes, node.Cast<IBindingPattern>().Elements);
                case SyntaxKind.ArrayLiteralExpression:
                    return visitNodes(cbNodes, node.Cast<IArrayLiteralExpression>().Elements);
                case SyntaxKind.ObjectLiteralExpression:
                    return visitNodes(cbNodes, node.Cast<IObjectLiteralExpression>().Properties);
                case SyntaxKind.PropertyAccessExpression:
                    {
                        var concreteNode = node.Cast<IPropertyAccessExpression>();
                        return VisitNode(cbNode, concreteNode.Expression) ??
                           VisitNode(cbNode, concreteNode.DotToken) ??
                           VisitNode(cbNode, concreteNode.Name);
                    }

                case SyntaxKind.ElementAccessExpression:
                    {
                        var concreteNode = node.Cast<IPropertyAccessExpression>();
                        return VisitNode(cbNode, concreteNode.Expression) ??
                           VisitNode(cbNode, node.Cast<IElementAccessExpression>().ArgumentExpression);
                    }

                case SyntaxKind.CallExpression:
                case SyntaxKind.NewExpression:
                    {
                        var concreteNode = node.Cast<ICallExpression>();
                        return VisitNode(cbNode, concreteNode.Expression) ??
                           visitNodes(cbNodes, concreteNode.TypeArguments) ??
                           visitNodes(cbNodes, concreteNode.Arguments);
                    }

                case SyntaxKind.TaggedTemplateExpression:
                    {
                        var concreteNode = node.Cast<ITaggedTemplateExpression>();
                        return VisitNode(cbNode, concreteNode.Tag) ??
                           VisitNode(cbNode, concreteNode.TemplateExpression);
                    }

                case SyntaxKind.TypeAssertionExpression:
                    {
                        var concreteNode = node.Cast<ITypeAssertion>();
                        return VisitNode(cbNode, concreteNode.Type) ??
                           VisitNode(cbNode, concreteNode.Expression);
                    }

                case SyntaxKind.ParenthesizedExpression:
                    return VisitNode(cbNode, node.Cast<IParenthesizedExpression>().Expression);
                case SyntaxKind.DeleteExpression:
                    return VisitNode(cbNode, node.Cast<IDeleteExpression>().Expression);
                case SyntaxKind.TypeOfExpression:
                    return VisitNode(cbNode, node.Cast<ITypeOfExpression>().Expression);
                case SyntaxKind.VoidExpression:
                    return VisitNode(cbNode, node.Cast<IVoidExpression>().Expression);
                case SyntaxKind.PrefixUnaryExpression:
                    return VisitNode(cbNode, node.Cast<IPrefixUnaryExpression>().Operand);
                case SyntaxKind.YieldExpression:
                    {
                        var concreteNode = node.Cast<IYieldExpression>();
                        return VisitNode(cbNode, concreteNode.AsteriskToken) ??
                           VisitNode(cbNode, concreteNode.Expression);
                    }

                case SyntaxKind.AwaitExpression:
                    return VisitNode(cbNode, node.Cast<IAwaitExpression>().Expression);
                case SyntaxKind.PostfixUnaryExpression:
                    return VisitNode(cbNode, node.Cast<IPostfixUnaryExpression>().Operand);
                case SyntaxKind.BinaryExpression:
                    {
                        var concreteNode = node.Cast<IBinaryExpression>();
                        return VisitNode(cbNode, concreteNode.Left) ??
                           VisitNode(cbNode, concreteNode.OperatorToken) ??
                           VisitNode(cbNode, concreteNode.Right);
                    }

                case SyntaxKind.AsExpression:
                    {
                        var concreteNode = node.Cast<IAsExpression>();
                        return VisitNode(cbNode, concreteNode.Expression) ??
                           VisitNode(cbNode, concreteNode.Type);
                    }

                case SyntaxKind.ConditionalExpression:
                    {
                        var concreteNode = node.Cast<IConditionalExpression>();
                        return VisitNode(cbNode, concreteNode.Condition) ??
                           VisitNode(cbNode, concreteNode.QuestionToken) ??
                           VisitNode(cbNode, concreteNode.WhenTrue) ??
                           VisitNode(cbNode, concreteNode.ColonToken) ??
                           VisitNode(cbNode, concreteNode.WhenFalse);
                    }
                    
                case SyntaxKind.SwitchExpression:
                    {
                        var concreteNode = node.Cast<ISwitchExpression>();
                        return VisitNode(cbNode, concreteNode.Expression) ??
                            visitNodes(cbNode, concreteNode.Clauses);
                    }
                case SyntaxKind.SwitchExpressionClause:
                    {
                        var concreteNode = node.Cast<ISwitchExpressionClause>();
                        return (concreteNode.IsDefaultFallthrough ? null : VisitNode(cbNode, concreteNode.Match)) ??
                           VisitNode(cbNode, concreteNode.Expression);
                    }

                case SyntaxKind.SpreadElementExpression:
                    return VisitNode(cbNode, node.Cast<ISpreadElementExpression>().Expression);
                case SyntaxKind.Block:
                case SyntaxKind.ModuleBlock:
                    return visitNodes(cbNodes, node.Cast<IBlock>().Statements);
                case SyntaxKind.SourceFile:
                    {
                        var concreteNode = node.Cast<ISourceFile>();
                        return visitNodes(cbNodes, concreteNode.Statements) ??
                           VisitNode(cbNode, concreteNode.EndOfFileToken);
                    }

                case SyntaxKind.VariableStatement:
                    {
                        return visitNodes(cbNodes, node.Decorators) ??
                           visitNodes(cbNodes, node.Modifiers) ??
                           VisitNode(cbNode, node.Cast<IVariableStatement>().DeclarationList);
                    }

                case SyntaxKind.VariableDeclarationList:
                    return visitNodes(cbNodes, node.Cast<IVariableDeclarationList>().Declarations);
                case SyntaxKind.ExpressionStatement:
                    return VisitNode(cbNode, node.Cast<IExpressionStatement>().Expression);
                case SyntaxKind.IfStatement:
                    {
                        var concreteNode = node.Cast<IIfStatement>();
                        return VisitNode(cbNode, concreteNode.Expression) ??
                           VisitNode(cbNode, concreteNode.ThenStatement) ??
                           VisitNode(cbNode, concreteNode.ElseStatement.ValueOrDefault);
                    }

                case SyntaxKind.DoStatement:
                    {
                        var concreteNode = node.Cast<IDoStatement>();
                        return VisitNode(cbNode, concreteNode.Statement) ??
                           VisitNode(cbNode, concreteNode.Expression);
                    }

                case SyntaxKind.WhileStatement:
                    {
                        var concreteNode = node.Cast<IWhileStatement>();
                        return VisitNode(cbNode, concreteNode.Expression) ??
                           VisitNode(cbNode, concreteNode.Statement);
                    }

                case SyntaxKind.ForStatement:
                    {
                        var concreteNode = node.Cast<IForStatement>();
                        return VisitNode(cbNode, concreteNode.Initializer) ??
                           VisitNode(cbNode, concreteNode.Condition) ??
                           VisitNode(cbNode, concreteNode.Incrementor) ??
                           VisitNode(cbNode, concreteNode.Statement);
                    }

                case SyntaxKind.ForInStatement:
                    {
                        var concreteNode = node.Cast<IForInStatement>();
                        return VisitNode(cbNode, concreteNode.Initializer) ??
                           VisitNode(cbNode, concreteNode.Expression) ??
                           VisitNode(cbNode, concreteNode.Statement);
                    }

                case SyntaxKind.ForOfStatement:
                    {
                        var concreteNode = node.Cast<IForOfStatement>();
                        return VisitNode(cbNode, concreteNode.Initializer) ??
                           VisitNode(cbNode, concreteNode.Expression) ??
                           VisitNode(cbNode, concreteNode.Statement);
                    }

                case SyntaxKind.ContinueStatement:
                case SyntaxKind.BreakStatement:
                    return VisitNode(cbNode, node.Cast<IBreakOrContinueStatement>().Label);
                case SyntaxKind.ReturnStatement:
                    return VisitNode(cbNode, node.Cast<IReturnStatement>().Expression);
                case SyntaxKind.WithStatement:
                    {
                        var concreteNode = node.Cast<IWithStatement>();
                        return VisitNode(cbNode, concreteNode.Expression) ??
                           VisitNode(cbNode, concreteNode.Statement);
                    }

                case SyntaxKind.SwitchStatement:
                    {
                        var concreteNode = node.Cast<ISwitchStatement>();
                        return VisitNode(cbNode, concreteNode.Expression) ??
                           VisitNode(cbNode, concreteNode.CaseBlock);
                    }

                case SyntaxKind.CaseBlock:
                    return visitNodes(cbNodes, node.Cast<ICaseBlock>().Clauses);
                case SyntaxKind.CaseClause:
                    {
                        var concreteNode = node.Cast<ICaseClause>();
                        return VisitNode(cbNode, concreteNode.Expression) ??
                             visitNodes(cbNodes, concreteNode.Statements);
                    }

                case SyntaxKind.DefaultClause:
                    return visitNodes(cbNodes, node.Cast<IDefaultClause>().Statements);
                case SyntaxKind.LabeledStatement:
                    {
                        var concreteNode = node.Cast<ILabeledStatement>();
                        return VisitNode(cbNode, concreteNode.Label) ??
                           VisitNode(cbNode, concreteNode.Statement);
                    }

                case SyntaxKind.ThrowStatement:
                    return VisitNode(cbNode, node.Cast<IThrowStatement>().Expression);
                case SyntaxKind.TryStatement:
                    {
                        var concreteNode = node.Cast<ITryStatement>();
                        return VisitNode(cbNode, concreteNode.TryBlock) ??
                           VisitNode(cbNode, concreteNode.CatchClause) ??
                           VisitNode(cbNode, concreteNode.FinallyBlock);
                    }

                case SyntaxKind.CatchClause:
                    {
                        var concreteNode = node.Cast<ICatchClause>();
                        return VisitNode(cbNode, concreteNode.VariableDeclaration) ??
                           VisitNode(cbNode, concreteNode.Block);
                    }

                case SyntaxKind.Decorator:
                    return VisitNode(cbNode, node.Cast<IDecorator>().Expression);
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                    {
                        var concreteNode = node.Cast<IClassLikeDeclaration>();
                        return visitNodes(cbNodes, node.Decorators) ??
                           visitNodes(cbNodes, node.Modifiers) ??
                           VisitNode(cbNode, concreteNode.Name) ??
                           visitNodes(cbNodes, concreteNode.TypeParameters) ??
                           visitNodes(cbNodes, concreteNode.HeritageClauses) ??
                           visitNodes(cbNodes, concreteNode.Members);
                    }

                case SyntaxKind.InterfaceDeclaration:
                    {
                        var concreteNode = node.Cast<IInterfaceDeclaration>();
                        return visitNodes(cbNodes, node.Decorators) ??
                           visitNodes(cbNodes, node.Modifiers) ??
                           VisitNode(cbNode, concreteNode.Name) ??
                           visitNodes(cbNodes, concreteNode.TypeParameters) ??
                           visitNodes(cbNodes, concreteNode.HeritageClauses) ??
                           visitNodes(cbNodes, concreteNode.Members);
                    }

                case SyntaxKind.TypeAliasDeclaration:
                    {
                        var concreteNode = node.Cast<ITypeAliasDeclaration>();
                        return visitNodes(cbNodes, node.Decorators) ??
                           visitNodes(cbNodes, node.Modifiers) ??
                           VisitNode(cbNode, concreteNode.Name) ??
                           visitNodes(cbNodes, concreteNode.TypeParameters) ??
                           VisitNode(cbNode, concreteNode.Type);
                    }

                case SyntaxKind.EnumDeclaration:
                    {
                        var concreteNode = node.Cast<IEnumDeclaration>();
                        return visitNodes(cbNodes, node.Decorators) ??
                           visitNodes(cbNodes, node.Modifiers) ??
                           VisitNode(cbNode, concreteNode.Name) ??
                           visitNodes(cbNodes, concreteNode.Members);
                    }

                case SyntaxKind.EnumMember:
                    {
                        var concreteNode = node.Cast<IEnumMember>();
                        return visitNodes(cbNodes, node.Decorators) ??
                           VisitNode(cbNode, concreteNode.Name) ??
                           VisitNode(cbNode, concreteNode.Initializer.ValueOrDefault);
                    }

                case SyntaxKind.ModuleDeclaration:
                    {
                        var concreteNode = node.Cast<IModuleDeclaration>();
                        return visitNodes(cbNodes, node.Decorators) ??
                           visitNodes(cbNodes, node.Modifiers) ??
                           VisitNode(cbNode, concreteNode.Name) ??
                           VisitNode(cbNode, concreteNode.Body);
                    }

                case SyntaxKind.ImportEqualsDeclaration:
                    {
                        var concreteNode = node.Cast<IImportEqualsDeclaration>();
                        return visitNodes(cbNodes, node.Decorators) ??
                           visitNodes(cbNodes, node.Modifiers) ??
                           VisitNode(cbNode, concreteNode.Name) ??
                           VisitNode(cbNode, concreteNode.ModuleReference);
                    }

                case SyntaxKind.ImportDeclaration:
                    {
                        var concreteNode = node.Cast<IImportDeclaration>();
                        return visitNodes(cbNodes, node.Decorators) ??
                           visitNodes(cbNodes, node.Modifiers) ??
                           VisitNode(cbNode, concreteNode.ImportClause) ??
                           VisitNode(cbNode, concreteNode.ModuleSpecifier);
                    }

                case SyntaxKind.ImportClause:
                    {
                        var concreteNode = node.Cast<IImportClause>();
                        return VisitNode(cbNode, concreteNode.Name) ??
                           VisitNode(cbNode, concreteNode.NamedBindings);
                    }

                case SyntaxKind.NamespaceImport:
                    return VisitNode(cbNode, node.Cast<INamespaceImport>().Name);
                case SyntaxKind.NamedImports:
                    return visitNodes(cbNodes, node.Cast<INamedImports>().Elements);
                case SyntaxKind.NamedExports:
                    return visitNodes(cbNodes, node.Cast<INamedExports>().Elements);
                case SyntaxKind.ExportDeclaration:
                    {
                        var concreteNode = node.Cast<IExportDeclaration>();
                        return visitNodes(cbNodes, node.Decorators) ??
                               visitNodes(cbNodes, node.Modifiers) ??
                               VisitNode(cbNode, concreteNode.ExportClause) ??
                               VisitNode(cbNode, concreteNode.ModuleSpecifier);
                    }

                case SyntaxKind.ImportSpecifier:
                    {
                        var concreteNode = node.Cast<IImportSpecifier>();
                        return VisitNode(cbNode, concreteNode.PropertyName) ??
                           VisitNode(cbNode, concreteNode.Name);
                    }

                case SyntaxKind.ExportSpecifier:
                    {
                        var concreteNode = node.Cast<IExportSpecifier>();
                        return VisitNode(cbNode, concreteNode.PropertyName) ??
                           VisitNode(cbNode, concreteNode.Name);
                    }

                case SyntaxKind.ExportAssignment:
                    {
                        return visitNodes(cbNodes, node.Decorators) ??
                           visitNodes(cbNodes, node.Modifiers) ??
                           VisitNode(cbNode, node.Cast<IExportAssignment>().Expression);
                    }

                case SyntaxKind.TemplateExpression:
                    {
                        var concreteNode = node.Cast<ITemplateExpression>();
                        return VisitNode(cbNode, concreteNode.Head) ??
                        visitNodes(cbNodes, concreteNode.TemplateSpans);
                    }

                case SyntaxKind.TemplateSpan:
                    {
                        var concreteNode = node.Cast<ITemplateSpan>();
                        return VisitNode(cbNode, concreteNode.Expression) ??
                        VisitNode(cbNode, concreteNode.Literal);
                    }

                case SyntaxKind.ComputedPropertyName:
                    return VisitNode(cbNode, node.Cast<IComputedPropertyName>().Expression);
                case SyntaxKind.HeritageClause:
                    return visitNodes(cbNodes, node.Cast<IHeritageClause>().Types);
                case SyntaxKind.ExpressionWithTypeArguments:
                    {
                        var concreteNode = node.Cast<IExpressionWithTypeArguments>();
                        return VisitNode(cbNode, concreteNode.Expression) ??
                           visitNodes(cbNodes, concreteNode.TypeArguments);
                    }

                case SyntaxKind.ExternalModuleReference:
                    return VisitNode(cbNode, node.Cast<IExternalModuleReference>().Expression);
                case SyntaxKind.MissingDeclaration:
                    return visitNodes(cbNodes, node.Decorators);
                case SyntaxKind.StringLiteralType:
                    // DScript-specific: decorators on string literal types
                    return visitNodes(cbNodes, node.Decorators);
            }

            return default(T);
        }
    }
}
