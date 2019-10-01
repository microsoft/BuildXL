// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace TypeScript.Net.Parsing
{
    /// <summary>
    /// Helper class for visiting nodes.
    /// </summary>
    /// <remarks>
    /// In typescript codebase similar functionality resides in parser.ts.
    /// </remarks>
    public static class NodeWalker
    {
        /// <summary>
        /// Invokes a callback for each child of the given node. The 'cbNode' callback is invoked for all child nodes
        /// stored in properties. If a <paramref name="cbNode"/> callback is specified, it is invoked for embedded arrays; otherwise,
        /// embedded arrays are flattened and the <paramref name="cbNode"/> callback is invoked for each element. If a callback returns
        /// a truthy value, iteration stops and that value is returned. Otherwise, undefined is returned.
        /// </summary>
        public static T ForEachChild<T>(INode node, Func<INode /*node*/, T> cbNode) where T : class
        {
            if (node == null)
            {
                return default(T);
            }

            return ForEachChild(node, cbNode, (n, func) => func(n));
        }

        /// <summary>
        /// Invokes a callback for each child of the given node. The <paramref name="cbNode"/> callback is invoked for all child nodes
        /// stored in properties. If a <paramref name="cbNode"/> callback is specified, it is invoked for embedded arrays; otherwise,
        /// embedded arrays are flattened and the 'cbNode' callback is invoked for each element. If a callback returns
        /// a truthy value, iteration stops and that value is returned. Otherwise, undefined is returned.
        /// </summary>
        public static TResult ForEachChild<TResult, TState>(INode node, TState state, Func<INode /*node*/, TState, TResult> cbNode) where TResult : class
        {
            if (node == null)
            {
                return default(TResult);
            }

            // Using pooled list of children to avoid iterator allocation.
            using (var listWrapper = ObjectPools.NodeListPool.GetInstance())
            {
                var list = listWrapper.Instance;
                DoGetChildren(node, list);

                foreach (var child in list)
                {
                    foreach (var c in child)
                    {
                        var result = cbNode(c, state);
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }
            }

            return default(TResult);
        }

        /// <summary>
        /// Invokes a callback for each child of the given node. The 'cbNode' callback is invoked for all child nodes
        /// stored in properties. If a 'cbNodes' callback is specified, it is invoked for embedded arrays; otherwise,
        /// embedded arrays are flattened and the 'cbNode' callback is invoked for each element. If a callback returns
        /// a truthy value, iteration stops and that value is returned. Otherwise, undefined is returned.
        /// </summary>
        public static void ForEachChild<TState>(INode node, TState state, Action<INode /*node*/, TState> cbNode)
        {
            if (node == null)
            {
                return;
            }

            // Using pooled list of children to avoid iterator allocation.
            using (var listWrapper = ObjectPools.NodeListPool.GetInstance())
            {
                var list = listWrapper.Instance;
                DoGetChildren(node, list);

                foreach (var child in list)
                {
                    foreach (var c in child)
                    {
                        cbNode(c, state);
                    }
                }
            }
        }

        /// <summary>
        /// Invokes a callback for each child of the given node. The 'cbNode' callback is invoked for all child nodes
        /// stored in properties. If a 'cbNodes' callback is specified, it is invoked for embedded arrays; otherwise,
        /// embedded arrays are flattened and the 'cbNode' callback is invoked for each element. If a callback returns
        /// a truthy value, iteration stops and that value is returned. Otherwise, undefined is returned.
        /// </summary>
        public static bool ForEachChild(INode node, Func<INode /*node*/, bool> cbNode)
        {
            // Using state explicitely to avoid closure allocation
            return ForEachChild(node, cbNode, (n, cb) => cb(n) ? Bool.True : Bool.False);
        }

        /// <summary>
        /// Invokes a callback for each child of the given node. The 'cbNode' callback is invoked for all child nodes
        /// stored in properties. If a 'cbNodes' callback is specified, it is invoked for embedded arrays; otherwise,
        /// embedded arrays are flattened and the 'cbNode' callback is invoked for each element. If a callback returns
        /// a truthy value, iteration stops and that value is returned. Otherwise, undefined is returned.
        /// </summary>
        public static bool ForEachChild<TState>(INode node, TState state, Func<INode, TState, bool> cbNode)
        {
            // Using state explicitely to avoid closure allocation
            return ForEachChild(node, (state, cbNode), (n, tup) => tup.cbNode(n, tup.state) ? Bool.True : Bool.False);
        }

        /// <summary>
        /// Traverses the tree specified by <paramref name="root"/> recursively.
        /// Returns non-null value provided by the call to <paramref name="func"/>.
        /// </summary>
        public static T ForEachChildRecursively<T>(INode root, Func<INode /*node*/, T> func, bool recurseThroughIdentifiers = false) where T : class
        {
            foreach (var n in TraverseBreadthFirstAndSelf(root, recurseThroughIdentifiers))
            {
                if (ReferenceEquals(n, root))
                {
                    continue;
                }

                var r = func(n);
                if (r != null)
                {
                    return r;
                }
            }

            return default(T);
        }

        /// <summary>
        /// Helper function for traversing recursive data structures in non-recursive fashion.
        /// </summary>
        internal static IEnumerable<INode> TraverseBreadthFirstAndSelf(INode item, Func<INode, ThreadLocalPooledObjectWrapper<List<NodeOrNodeArray>>> childSelector)
        {
            // Using queue to get depth first left to right traversal. Stack would give right to left traversal.
            var queue = new Queue<INode>();

            queue.Enqueue(item);

            while (queue.Count > 0)
            {
                var next = queue.Dequeue();
                yield return next;

                using (var listWrapper = childSelector(next))
                {
                    foreach (var n in listWrapper.Instance)
                    {
                        foreach (var c in n)
                        {
                            queue.Enqueue(c);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Traverses <paramref name="node"/> and its children in depth-first manner
        /// </summary>
        public static void TraverseDepthFirstAndSelfInOrder(INode node, Func<INode, bool> predicate, System.Threading.CancellationToken token, bool recurseThroughIdentifiers = false)
        {
            TraverseDepthFirstAndSelfInOrderLocal(node, recurseThroughIdentifiers);

            bool TraverseDepthFirstAndSelfInOrderLocal(INode localNode, bool recurse)
            {
                bool needToContinue = predicate(localNode);
                if (needToContinue && !token.IsCancellationRequested)
                {
                    using (var listWrapper = GetChildrenFast(localNode, recurse))
                    {
                        foreach (var c in listWrapper.Instance)
                        {
                            foreach (var n in c)
                            {
                                needToContinue = TraverseDepthFirstAndSelfInOrderLocal(n, recurseThroughIdentifiers);
                                if (!needToContinue)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Helper function for traversing recursive data structures in non-recursive fashion.
        /// </summary>
        internal static IEnumerable<INode> TraverseDepthFirstAndSelfInOrder(INode item, Func<INode, ThreadLocalPooledObjectWrapper<List<NodeOrNodeArray>>> childSelector)
        {
            // Using stack to get depth first left to right traversal. Queue would give right to left traversal.
            var stack = new Stack<INode>();

            stack.Push(item);

            while (stack.Count > 0)
            {
                var next = stack.Pop();
                yield return next;

                using (var listWrapper = childSelector(next))
                {
                    foreach (var n in listWrapper.Instance)
                    {
                        foreach (var c in n)
                        {
                            stack.Push(c);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Traverses <paramref name="node"/> and its children in depth-first manner
        /// </summary>
        public static IEnumerable<INode> TraverseBreadthFirstAndSelf(INode node, bool recurseThroughIdentifiers = false)
        {
            return TraverseBreadthFirstAndSelf(node, n => GetChildrenFast(n, recurseThroughIdentifiers));
        }

        /// <summary>
        /// Returns child nodes for specified <paramref name="node"/>.
        /// </summary>
        /// <remarks>
        /// This method returns just immediate children. To traverse all the nodes recursively, use <see cref="TraverseBreadthFirstAndSelf(INode, bool)"/>.
        /// </remarks>
        public static IEnumerable<INode> GetChildren(INode node)
        {
            // Using pooled list of children to avoid iterator allocation.
            using (var listWrapper = ObjectPools.NodeListPool.GetInstance())
            {
                var list = listWrapper.Instance;
                DoGetChildren(node, list);

                foreach (var child in list)
                {
                    foreach (var c in child)
                    {
                        yield return c;
                    }
                }
            }
        }

        /// <summary>
        /// Allocation-free version of <see cref="GetChildren"/> method.
        /// </summary>
        public static ThreadLocalPooledObjectWrapper<List<NodeOrNodeArray>> GetChildrenFast(INode node, bool recurseThroughIdentifiers = false)
        {
            var wrapper = ObjectPools.NodeListPool.GetInstance();
            DoGetChildren(node, wrapper.Instance, recurseThroughIdentifiers);
            return wrapper;
        }

        private static NodeOrNodeArray Node([CanBeNull]INode node)
        {
            return new NodeOrNodeArray(node);
        }

        private static NodeOrNodeArray Nodes([NotNull]INodeArray<INode> nodes)
        {
            return new NodeOrNodeArray(nodes);
        }

        private static void DoGetChildren(INode node, List<NodeOrNodeArray> nodes, bool recurseThroughIdentifiers = false)
        {
            if (node == null)
            {
                return;
            }

            switch (node.Kind)
            {
                case SyntaxKind.QualifiedName:
                    {
                        var concreteNode = node.Cast<IQualifiedName>();
                        nodes.Add(Node(concreteNode.Left));
                        nodes.Add(Node(concreteNode.Right));
                        break;
                    }

                case SyntaxKind.TypeParameter:
                    {
                        var concreteNode = node.Cast<ITypeParameterDeclaration>();
                        nodes.Add(Node(concreteNode.Name));
                        nodes.Add(Node(concreteNode.Constraint));
                        nodes.Add(Node(concreteNode.Expression));
                        break;
                    }

                case SyntaxKind.ShorthandPropertyAssignment:
                    {
                        var concreteNode = node.Cast<IShorthandPropertyAssignment>();
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Node(concreteNode.Name));
                        nodes.Add(Node(concreteNode.QuestionToken.ValueOrDefault));
                        nodes.Add(Node(concreteNode.EqualsToken.ValueOrDefault));
                        nodes.Add(Node(concreteNode.ObjectAssignmentInitializer));
                        break;
                    }

                case SyntaxKind.Parameter:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.PropertyAssignment:
                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.BindingElement:
                    {
                        var concreteNode = node.Cast<IVariableLikeDeclaration>();
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Node(concreteNode.PropertyName));
                        nodes.Add(Node(concreteNode.DotDotDotToken.ValueOrDefault));
                        nodes.Add(Node(concreteNode.Name));
                        nodes.Add(Node(concreteNode.QuestionToken.ValueOrDefault));
                        nodes.Add(Node(concreteNode.Type));
                        nodes.Add(Node(concreteNode.Initializer));
                        break;
                    }

                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                case SyntaxKind.CallSignature:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.IndexSignature:
                    {
                        var concreteNode = node.Cast<ISignatureDeclaration>();
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Nodes(concreteNode.TypeParameters ?? NodeArray.Empty<ITypeParameterDeclaration>()));
                        nodes.Add(Nodes(concreteNode.Parameters));
                        nodes.Add(Node(concreteNode.Type));
                        break;
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
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Node(concreteNode.AsteriskToken.ValueOrDefault));
                        nodes.Add(Node(concreteNode.Name));
                        nodes.Add(Node(concreteNode.QuestionToken.ValueOrDefault));
                        nodes.Add(Nodes(concreteNode.TypeParameters ?? NodeArray.Empty<ITypeParameterDeclaration>()));
                        nodes.Add(Nodes(concreteNode.Parameters));
                        nodes.Add(Node(concreteNode.Type));
                        nodes.Add(Node(node.As<IArrowFunction>()?.EqualsGreaterThanToken));
                        nodes.Add(Node(concreteNode.Body));
                        break;
                    }

                case SyntaxKind.TypeReference:
                    {
                        var concreteNode = node.Cast<ITypeReferenceNode>();
                        nodes.Add(Node(concreteNode.TypeName));
                        nodes.Add(Nodes(concreteNode.TypeArguments ?? NodeArray.Empty<ITypeNode>()));
                        break;
                    }

                case SyntaxKind.TypePredicate:
                    {
                        var concreteNode = node.Cast<ITypePredicateNode>();
                        nodes.Add(Node(concreteNode.ParameterName));
                        nodes.Add(Node(concreteNode.Type));
                        break;
                    }

                case SyntaxKind.TypeQuery:
                    nodes.Add(Node(node.Cast<ITypeQueryNode>().ExprName));
                    break;
                case SyntaxKind.TypeLiteral:
                    nodes.Add(Nodes(node.Cast<ITypeLiteralNode>().Members));
                    break;
                case SyntaxKind.ArrayType:
                    nodes.Add(Node(node.Cast<IArrayTypeNode>().ElementType));
                    break;
                case SyntaxKind.TupleType:
                    nodes.Add(Nodes(node.Cast<ITupleTypeNode>().ElementTypes));
                    break;
                case SyntaxKind.UnionType:
                case SyntaxKind.IntersectionType:
                    nodes.Add(Nodes(node.Cast<IUnionOrIntersectionTypeNode>().Types));
                    break;
                case SyntaxKind.ParenthesizedType:
                    nodes.Add(Node(node.Cast<IParenthesizedTypeNode>().Type));
                    break;
                case SyntaxKind.ObjectBindingPattern:
                case SyntaxKind.ArrayBindingPattern:
                    nodes.Add(Nodes(node.Cast<IBindingPattern>().Elements));
                    break;
                case SyntaxKind.ArrayLiteralExpression:
                    nodes.Add(Nodes(node.Cast<IArrayLiteralExpression>().Elements));
                    break;
                case SyntaxKind.ObjectLiteralExpression:
                    nodes.Add(Nodes(node.Cast<IObjectLiteralExpression>().Properties));
                    break;
                case SyntaxKind.PropertyAccessExpression:
                    {
                        var concreteNode = node.Cast<IPropertyAccessExpression>();
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Node(concreteNode.DotToken));
                        nodes.Add(Node(concreteNode.Name));
                        break;
                    }

                case SyntaxKind.ElementAccessExpression:
                    {
                        var concreteNode = node.Cast<IElementAccessExpression>();
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Node(concreteNode.ArgumentExpression));
                        break;
                    }

                case SyntaxKind.CallExpression:
                case SyntaxKind.NewExpression:
                    {
                        var concreteNode = node.Cast<ICallExpression>();
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Nodes(concreteNode.TypeArguments));
                        nodes.Add(Nodes(concreteNode.Arguments));
                        break;
                    }

                case SyntaxKind.TaggedTemplateExpression:
                    {
                        var concreteNode = node.Cast<ITaggedTemplateExpression>();
                        nodes.Add(Node(concreteNode.Tag));
                        nodes.Add(Node(concreteNode.TemplateExpression));
                        break;
                    }

                case SyntaxKind.TypeAssertionExpression:
                    {
                        var concreteNode = node.Cast<ITypeAssertion>();
                        nodes.Add(Node(concreteNode.Type));
                        nodes.Add(Node(concreteNode.Expression));
                        break;
                    }

                case SyntaxKind.ParenthesizedExpression:
                    nodes.Add(Node(node.Cast<IParenthesizedExpression>().Expression));
                    break;
                case SyntaxKind.DeleteExpression:
                    nodes.Add(Node(node.Cast<IDeleteExpression>().Expression));
                    break;
                case SyntaxKind.TypeOfExpression:
                    nodes.Add(Node(node.Cast<ITypeOfExpression>().Expression));
                    break;
                case SyntaxKind.VoidExpression:
                    nodes.Add(Node(node.Cast<IVoidExpression>().Expression));
                    break;
                case SyntaxKind.PrefixUnaryExpression:
                    nodes.Add(Node(node.Cast<IPrefixUnaryExpression>().Operand));
                    break;
                case SyntaxKind.YieldExpression:
                    {
                        var concreteExpression = node.Cast<IYieldExpression>();
                        nodes.Add(Node(concreteExpression.AsteriskToken));
                        nodes.Add(Node(concreteExpression.Expression));
                        break;
                    }

                case SyntaxKind.AwaitExpression:
                    nodes.Add(Node(node.Cast<IAwaitExpression>().Expression));
                    break;
                case SyntaxKind.PostfixUnaryExpression:
                    nodes.Add(Node(node.Cast<IPostfixUnaryExpression>().Operand));
                    break;
                case SyntaxKind.BinaryExpression:
                    {
                        var concreteNode = node.Cast<IBinaryExpression>();
                        nodes.Add(Node(concreteNode.Left));
                        nodes.Add(Node(concreteNode.OperatorToken));
                        nodes.Add(Node(concreteNode.Right));
                        break;
                    }

                case SyntaxKind.AsExpression:
                    {
                        var concreteNode = node.Cast<IAsExpression>();
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Node(concreteNode.Type));
                        break;
                    }

                case SyntaxKind.ConditionalExpression:
                    {
                        var concreteNode = node.Cast<IConditionalExpression>();
                        nodes.Add(Node(concreteNode.Condition));
                        nodes.Add(Node(concreteNode.QuestionToken));
                        nodes.Add(Node(concreteNode.WhenTrue));
                        nodes.Add(Node(concreteNode.ColonToken));
                        nodes.Add(Node(concreteNode.WhenFalse));
                        break;
                    }

                case SyntaxKind.SwitchExpression:
                    {
                        var concreteNode = node.Cast<ISwitchExpression>();
                        nodes.Add(Node(concreteNode.Expression));
                        foreach (var clause in concreteNode.Clauses)
                        {
                            nodes.Add(Node(clause));
                        }
                        break;
                    }

                case SyntaxKind.SwitchExpressionClause:
                    {
                        var concreteNode = node.Cast<ISwitchExpressionClause>();
                        if (!concreteNode.IsDefaultFallthrough)
                        {
                            nodes.Add(Node(concreteNode.Match));
                        }

                        nodes.Add(Node(concreteNode.Expression));
                        break;
                    }

                case SyntaxKind.SpreadElementExpression:
                    nodes.Add(Node(node.Cast<ISpreadElementExpression>().Expression));
                    break;
                case SyntaxKind.Block:
                case SyntaxKind.ModuleBlock:
                    nodes.Add(Nodes(node.Cast<IBlock>().Statements));
                    break;
                case SyntaxKind.SourceFile:
                    nodes.Add(Nodes(node.Cast<ISourceFile>().Statements));
                    nodes.Add(Node(node.Cast<ISourceFile>().EndOfFileToken));
                    break;
                case SyntaxKind.VariableStatement:
                    nodes.Add(Nodes(node.Decorators));
                    nodes.Add(Nodes(node.Modifiers));
                    nodes.Add(Node(node.Cast<IVariableStatement>().DeclarationList));
                    break;
                case SyntaxKind.VariableDeclarationList:
                    nodes.Add(Nodes(node.Cast<IVariableDeclarationList>().Declarations));
                    break;
                case SyntaxKind.ExpressionStatement:
                    nodes.Add(Node(node.Cast<IExpressionStatement>().Expression));
                    break;
                case SyntaxKind.IfStatement:
                    {
                        var concreteNode = node.Cast<IIfStatement>();
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Node(concreteNode.ThenStatement));
                        nodes.Add(Node(concreteNode.ElseStatement.ValueOrDefault));
                        break;
                    }

                case SyntaxKind.DoStatement:
                    {
                        var concreteNode = node.Cast<IDoStatement>();
                        nodes.Add(Node(concreteNode.Statement));
                        nodes.Add(Node(concreteNode.Expression));
                        break;
                    }

                case SyntaxKind.WhileStatement:
                    {
                        var concreteNode = node.Cast<IWhileStatement>();
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Node(concreteNode.Statement));
                        break;
                    }

                case SyntaxKind.ForStatement:
                    {
                        var concreteNode = node.Cast<IForStatement>();
                        nodes.Add(Node(concreteNode.Initializer));
                        nodes.Add(Node(concreteNode.Condition));
                        nodes.Add(Node(concreteNode.Incrementor));
                        nodes.Add(Node(concreteNode.Statement));
                        break;
                    }

                case SyntaxKind.ForInStatement:
                    {
                        var concreteNode = node.Cast<IForInStatement>();
                        nodes.Add(Node(concreteNode.Initializer));
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Node(concreteNode.Statement));
                        break;
                    }

                case SyntaxKind.ForOfStatement:
                    {
                        var concreteNode = node.Cast<IForOfStatement>();
                        nodes.Add(Node(concreteNode.Initializer));
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Node(concreteNode.Statement));
                        break;
                    }

                case SyntaxKind.ContinueStatement:
                case SyntaxKind.BreakStatement:
                    nodes.Add(Node(node.Cast<IBreakOrContinueStatement>().Label));
                    break;
                case SyntaxKind.ReturnStatement:
                    nodes.Add(Node(node.Cast<IReturnStatement>().Expression));
                    break;
                case SyntaxKind.WithStatement:
                    {
                        var concreteNode = node.Cast<IWithStatement>();
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Node(concreteNode.Statement));
                        break;
                    }

                case SyntaxKind.SwitchStatement:
                    {
                        var concreteNode = node.Cast<ISwitchStatement>();
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Node(concreteNode.CaseBlock));
                        break;
                    }

                case SyntaxKind.CaseBlock:
                    nodes.Add(Nodes(node.Cast<ICaseBlock>().Clauses));
                    break;
                case SyntaxKind.CaseClause:
                    {
                        var concreteNode = node.Cast<ICaseClause>();
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Nodes(concreteNode.Statements));
                        break;
                    }

                case SyntaxKind.DefaultClause:
                    nodes.Add(Nodes(node.Cast<IDefaultClause>().Statements));
                    break;
                case SyntaxKind.LabeledStatement:
                    {
                        var concreteNode = node.Cast<ILabeledStatement>();
                        nodes.Add(Node(concreteNode.Label));
                        nodes.Add(Node(concreteNode.Statement));
                        break;
                    }

                case SyntaxKind.ThrowStatement:
                    nodes.Add(Node(node.Cast<IThrowStatement>().Expression));
                    break;
                case SyntaxKind.TryStatement:
                    {
                        var concreteNode = node.Cast<ITryStatement>();
                        nodes.Add(Node(concreteNode.TryBlock));
                        nodes.Add(Node(concreteNode.CatchClause));
                        nodes.Add(Node(concreteNode.FinallyBlock));
                        break;
                    }

                case SyntaxKind.CatchClause:
                    {
                        var concreteNode = node.Cast<ICatchClause>();
                        nodes.Add(Node(concreteNode.VariableDeclaration));
                        nodes.Add(Node(concreteNode.Block));
                        break;
                    }

                case SyntaxKind.Decorator:
                    nodes.Add(Node(node.Cast<IDecorator>().Expression));
                    break;
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                    {
                        var concreteNode = node.Cast<IClassLikeDeclaration>();
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Node(concreteNode.Name));
                        nodes.Add(Nodes(concreteNode.TypeParameters));
                        nodes.Add(Nodes(concreteNode.HeritageClauses));
                        nodes.Add(Nodes(concreteNode.Members));
                        break;
                    }

                case SyntaxKind.InterfaceDeclaration:
                    {
                        var concreteNode = node.Cast<IInterfaceDeclaration>();
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Node(concreteNode.Name));
                        nodes.Add(Nodes(concreteNode.TypeParameters));
                        nodes.Add(Nodes(concreteNode.HeritageClauses));
                        nodes.Add(Nodes(concreteNode.Members));
                        break;
                    }

                case SyntaxKind.TypeAliasDeclaration:
                    {
                        var concreteNode = node.Cast<ITypeAliasDeclaration>();
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Node(concreteNode.Name));
                        nodes.Add(Nodes(concreteNode.TypeParameters));
                        nodes.Add(Node(concreteNode.Type));
                        break;
                    }

                case SyntaxKind.EnumDeclaration:
                    {
                        var concreteNode = node.Cast<IEnumDeclaration>();
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Node(concreteNode.Name));
                        nodes.Add(Nodes(concreteNode.Members));
                        break;
                    }

                case SyntaxKind.EnumMember:
                    {
                        var concreteNode = node.Cast<IEnumMember>();
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Node(concreteNode.Name));
                        nodes.Add(Node(concreteNode.Initializer.ValueOrDefault));
                        break;
                    }

                case SyntaxKind.ModuleDeclaration:
                    {
                        var concreteNode = node.Cast<IModuleDeclaration>();
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Node(concreteNode.Name));
                        nodes.Add(Node(concreteNode.Body));
                        break;
                    }

                case SyntaxKind.ImportEqualsDeclaration:
                    {
                        var concreteNode = node.Cast<IImportEqualsDeclaration>();
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Node(concreteNode.Name));
                        nodes.Add(Node(concreteNode.ModuleReference));
                        break;
                    }

                case SyntaxKind.ImportDeclaration:
                    {
                        var concreteNode = node.Cast<IImportDeclaration>();
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Node(concreteNode.ImportClause));
                        nodes.Add(Node(concreteNode.ModuleSpecifier));
                        break;
                    }

                case SyntaxKind.ImportClause:
                    {
                        var concreteNode = node.Cast<IImportClause>();
                        nodes.Add(Node(concreteNode.Name));
                        nodes.Add(Node(concreteNode.NamedBindings));
                        break;
                    }

                case SyntaxKind.NamespaceImport:
                    nodes.Add(Node(node.Cast<INamespaceImport>().Name));
                    break;
                case SyntaxKind.NamedImports:
                    nodes.Add(Nodes(node.Cast<INamedImports>().Elements));
                    break;
                case SyntaxKind.NamedExports:
                    nodes.Add(Nodes(node.Cast<INamedExports>().Elements));
                    break;
                case SyntaxKind.ExportDeclaration:
                    {
                        var concreteNode = node.Cast<IExportDeclaration>();
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Node(concreteNode.ExportClause));
                        nodes.Add(Node(concreteNode.ModuleSpecifier));
                        break;
                    }

                case SyntaxKind.ImportSpecifier:
                    {
                        var concreteNode = node.Cast<IImportSpecifier>();
                        nodes.Add(Node(concreteNode.PropertyName));
                        nodes.Add(Node(concreteNode.Name));
                        break;
                    }

                case SyntaxKind.ExportSpecifier:
                    {
                        var concreteNode = node.Cast<IExportSpecifier>();
                        nodes.Add(Node(concreteNode.PropertyName));
                        nodes.Add(Node(concreteNode.Name));
                        break;
                    }

                case SyntaxKind.ExportAssignment:
                    {
                        nodes.Add(Nodes(node.Decorators));
                        nodes.Add(Nodes(node.Modifiers));
                        nodes.Add(Node(node.Cast<IExportAssignment>().Expression));
                        break;
                    }

                case SyntaxKind.TemplateExpression:
                    {
                        var concreteNode = node.Cast<ITemplateExpression>();
                        nodes.Add(Node(concreteNode.Head));
                        nodes.Add(Nodes(concreteNode.TemplateSpans));
                        break;
                    }

                case SyntaxKind.TemplateSpan:
                    {
                        var concreteNode = node.Cast<ITemplateSpan>();
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Node(concreteNode.Literal));
                        break;
                    }

                case SyntaxKind.ComputedPropertyName:
                    nodes.Add(Node(node.Cast<IComputedPropertyName>().Expression));
                    break;
                case SyntaxKind.HeritageClause:
                    nodes.Add(Nodes(node.Cast<IHeritageClause>().Types));
                    break;
                case SyntaxKind.ExpressionWithTypeArguments:
                    {
                        var concreteNode = node.Cast<IExpressionWithTypeArguments>();
                        nodes.Add(Node(concreteNode.Expression));
                        nodes.Add(Nodes(concreteNode.TypeArguments));
                        break;
                    }

                case SyntaxKind.ExternalModuleReference:
                    nodes.Add(Node(node.Cast<IExternalModuleReference>().Expression));
                    break;
                case SyntaxKind.MissingDeclaration:
                    nodes.Add(Nodes(node.Decorators));
                    break;
                case SyntaxKind.Identifier:
                    {
                        if (recurseThroughIdentifiers)
                        {
                            var declarationName = node.TryCast<DelegatingUnionNode>();
                            if (declarationName != null)
                            {
                                nodes.Add(Node(declarationName.Node));
                            }
                        }

                        break;
                    }
                case SyntaxKind.StringLiteralType:
                    // DScript-specific: decorators on string literal types
                    nodes.Add(Nodes(node.Decorators));
                    break;
            }
        }

        /// <summary>
        /// Union type that models Node | Node[].
        /// </summary>
        public readonly struct NodeOrNodeArray
        {
            /// <nodoc />
            public NodeOrNodeArrayEnumerator GetEnumerator()
            {
                return new NodeOrNodeArrayEnumerator(this);
            }

            /// <nodoc />
            public readonly INode Node;

            /// <nodoc />
            public readonly INodeArray<INode> Nodes;

            /// <nodoc />
            public NodeOrNodeArray(INode node)
                : this()
            {
                Node = node;
            }

            /// <nodoc />
            public NodeOrNodeArray(INodeArray<INode> nodes)
                : this()
            {
                Nodes = nodes;
            }

            /// <summary>
            /// Nested iterator to avoid enumerator allocation when the client code uses foreach over the current type instance.
            /// </summary>
            public struct NodeOrNodeArrayEnumerator
            {
                private readonly NodeOrNodeArray m_parent;
                private int m_index;
                private readonly int m_count;

                /// <nodoc />
                public NodeOrNodeArrayEnumerator(NodeOrNodeArray parent)
                {
                    m_parent = parent;
                    m_index = 0;

                    // if both Node and Nodes are null, then the 'count' is 0.
                    // if the parent.Node is not null, then the 'count' is 1.
                    // otherwise the cound is equals to parent.Nodes.Count
                    m_count = parent.Nodes != null ? parent.Nodes.Count : parent.Node != null ? 1 : 0;
                }

                /// <nodoc />
                [CanBeNull]
                public INode Current
                {
                    get { return m_parent.Nodes != null ? m_parent.Nodes[m_index - 1] : m_parent.Node; }
                }

                /// <nodoc />
                public bool MoveNext()
                {
                    return m_index++ < m_count;
                }
            }
        }
    }
}
