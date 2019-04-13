// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using JetBrains.Annotations;
using TypeScript.Net.Extensions;
using TypeScript.Net.Parsing;
using TypeScript.Net.Scanning;
using static BuildXL.Utilities.FormattableStringEx;
using CollectionExtensions = TypeScript.Net.Extensions.CollectionExtensions;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Set of utilities for <see cref="INode"/>.
    /// </summary>
    public static class NodeUtilities
    {
        /// <summary>
        /// Helper function that throws an exception. Useful for expression-based property initialization.
        /// </summary>
        public static Identifier ThrowNotSupportedException()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Returns true if this node contains a parse error anywhere underneath it.
        /// </summary>
        public static bool ContainsParseError(INode node)
        {
            AggregateChildData(node);
            return (node.ParserContextFlags & ParserContextFlags.ThisNodeOrAnySubNodesHasError) != ParserContextFlags.None;
        }

        /// <nodoc/>
        public static bool IsBlockOrCatchScoped(IDeclaration declaration)
        {
            return (GetCombinedNodeFlags(declaration) & NodeFlags.BlockScoped) != NodeFlags.None || IsCatchClauseVariableDeclaration(declaration);
        }

        /// <summary>
        /// Gets the nearest enclosing block scope container that has the provided node
        /// as a descendant, that is not the provided node.
        /// </summary>
        public static INode GetEnclosingBlockScopeContainer(INode node)
        {
            var current = node.Parent;
            while (current != null)
            {
                if (IsFunctionLike(current) != null)
                {
                    return current;
                }

                switch (current.Kind)
                {
                    case SyntaxKind.SourceFile:
                    case SyntaxKind.CaseBlock:
                    case SyntaxKind.CatchClause:
                    case SyntaxKind.ModuleDeclaration:
                    case SyntaxKind.ForStatement:
                    case SyntaxKind.ForInStatement:
                    case SyntaxKind.ForOfStatement:
                        return current;
                    case SyntaxKind.Block:
                        {
                            // block is not considered block-scope container
                            // see comment in binder.bind ts(...), case for SyntaxKind.Block
                            if (IsFunctionLike(current.Parent) == null)
                            {
                                return current;
                            }

                            break;
                        }
                }

                current = current.Parent;
            }

            return null;
        }

        /// <nodoc/>
        public static bool IsCatchClauseVariableDeclaration([CanBeNull] IDeclaration declaration)
        {
            // C# 6 syntax:
            return declaration?.Kind == SyntaxKind.VariableDeclaration && declaration?.Parent?.Kind == SyntaxKind.CatchClause;
        }

        /// <summary>
        /// Return display name of an identifier
        /// Computed property names will just be emitted as "[&lt;expr>]", where &lt;expr> is the source
        /// text of the expression in the computed property.
        /// </summary>
        // TODO:SQ: DeclarationName type for this function param seems unnecessary
        public static string DeclarationNameToString(INode name)
        {
            return GetFullWidth(name) == 0 ? "(Missing)" : GetTextOfNode(name);
        }

        /// <nodoc/>
        public static string GetTextOfNode(INode node, bool includeTrivia = false)
        {
            return Utils.GetSourceTextOfNodeFromSourceFile(NodeStructureExtensions.GetSourceFile(node), node, includeTrivia);
        }

        /// <nodoc/>
        public static bool IsExternalOrCommonJsModule(ISourceFile file)
        {
            return (file.ExternalModuleIndicator ?? file.CommonJsModuleIndicator) != null;
        }

        /// <nodoc/>
        public static bool IsDeclarationFile(ISourceFile file)
        {
            return file.IsDeclarationFile;
        }

        /// <nodoc/>
        public static bool IsConstEnumDeclaration(INode node)
        {
            return node.Kind == SyntaxKind.EnumDeclaration && IsConst(node);
        }

        /// <summary>
        /// Returns the node flags for this node and all relevant parent nodes.
        /// </summary>
        /// <remarks>
        /// This is done so that nodes like variable declarations and binding elements can return a view of
        /// their flags that includes the modifiers from their container. i.e., flags like export/declare aren't
        /// stored on the variable declaration directly, but on the containing variable statement
        /// (if it has one). Similarly, flags for let/var are stored on the variable declaration
        /// list. By calling this function, all those flags are combined so that the client can treat
        /// the node as if it actually had those flags.
        /// </remarks>
        public static NodeFlags GetCombinedNodeFlags(INode node)
        {
            node = WalkUpBindingElementsAndPatterns(node);

            var flags = node.Flags;
            if (node.Kind == SyntaxKind.Identifier)
            {
                node = node.Parent;
            }

            if (node.Kind == SyntaxKind.VariableDeclaration)
            {
                flags |= node.Flags;
                node = node.Parent;
            }

            if (node != null && node.Kind == SyntaxKind.VariableDeclarationList)
            {
                flags |= node.Flags;
                node = node.Parent;
            }

            if (node != null && node.Kind == SyntaxKind.VariableStatement)
            {
                flags |= node.Flags;
                var variableStatement = node.Cast<IVariableStatement>();
                flags |= variableStatement.DeclarationList.Flags;
            }

            return flags;
        }

        /// <summary>
        /// Extends <see cref="GetCombinedNodeFlags"/> (which only deals with variable declarations) to include export specifiers
        /// </summary>
        /// <remarks>
        /// The intended use is to retrieve potential public decorators on the export declaration
        /// </remarks>
        public static NodeFlags GetCombinedFlagsIncludingExportSpecifiers(INode node)
        {
            if (node.Kind != SyntaxKind.ExportSpecifier)
            {
                return GetCombinedNodeFlags(node);
            }

            // We go up to the export declaration collecting flags
            var flags = node.Flags;
            var namedExports = node.Parent;
            flags = flags | namedExports.Flags;
            var exportDeclaration = namedExports.Parent;
            flags = flags | exportDeclaration.Flags;
            return flags;
        }

        /// <nodoc/>
        public static bool IsConst(INode node)
        {
            return (GetCombinedNodeFlags(node) & NodeFlags.Const) != NodeFlags.None;
        }

        /// <nodoc/>
        public static bool IsLet(INode node)
        {
            return (GetCombinedNodeFlags(node) & NodeFlags.Let) != NodeFlags.None;
        }

        /// <nodoc/>
        public static bool IsPrologueDirective(INode node)
        {
            return node.Kind == SyntaxKind.ExpressionStatement && node.Cast<IExpressionStatement>().Expression.Kind == SyntaxKind.StringLiteral;
        }

        /// <nodoc/>
        public static ICommentRange[] GetLeadingCommentRangesOfNode(INode node, ISourceFile sourceFileOfNode)
        {
            return Scanner.GetLeadingCommentRanges(sourceFileOfNode.Text, node.Pos);
        }

        /// <nodoc/>
        public static ICommentRange[] GetLeadingCommentRangesOfNodeFromText(INode node, TextSource text)
        {
            return Scanner.GetLeadingCommentRanges(text, node.Pos);
        }

        /// <nodoc/>
        public static object GetJsDocComments(INode node, ISourceFile sourceFileOfNode)
        {
            return GetJsDocCommentsFromText(node, sourceFileOfNode.Text);
        }

        /// <nodoc/>
        public static ICommentRange[] GetJsDocCommentsFromText(INode node, TextSource text)
        {
            Func<ICommentRange, bool> isJsDocComment = (ICommentRange comment) =>
            {
                // True if the comment starts with '/**' but not if it is '/**/'
                return text.CharCodeAt(comment.Pos + 1) == CharacterCodes.Asterisk &&
                       text.CharCodeAt(comment.Pos + 2) == CharacterCodes.Asterisk &&
                       text.CharCodeAt(comment.Pos + 3) != CharacterCodes.Slash;
            };

            var commentRanges = node.Kind == SyntaxKind.Parameter || node.Kind == SyntaxKind.TypeParameter ?
                Scanner.GetTrailingCommentRanges(text, node.Pos).Concatenate(Scanner.GetLeadingCommentRanges(text, node.Pos)) :
                GetLeadingCommentRangesOfNodeFromText(node, text);

            return commentRanges.Where(isJsDocComment).ToList().ToArray();
        }

        /// <nodoc/>
        public static bool IsTypeNode(INode node)
        {
            if (node.Kind >= SyntaxKind.FirstTypeNode && node.Kind <= SyntaxKind.LastTypeNode)
            {
                return true;
            }

            switch (node.Kind)
            {
                case SyntaxKind.AnyKeyword:
                case SyntaxKind.NumberKeyword:
                case SyntaxKind.StringKeyword:
                case SyntaxKind.BooleanKeyword:
                case SyntaxKind.SymbolKeyword:
                    return true;
                case SyntaxKind.VoidKeyword:
                    return node.Parent.Kind != SyntaxKind.VoidExpression;
                case SyntaxKind.ExpressionWithTypeArguments:
                    return !IsExpressionWithTypeArgumentsInClassExtendsClause(node);

                // Identifiers and qualified names may be type nodes, depending on their context. Climb
                // above them to find the lowest container
                case SyntaxKind.Identifier:
                    {
                        // If the identifier is the RHS of a qualified name, then it's a type iff its parent is.
                        if (node.Parent.Kind == SyntaxKind.QualifiedName && node.Parent.Cast<IQualifiedName>().Right.ResolveUnionType() == node.ResolveUnionType())
                        {
                            node = node.Parent;
                        }
                        else if (node.Parent.Kind == SyntaxKind.PropertyAccessExpression && node.Parent.Cast<IPropertyAccessExpression>().Name.ResolveUnionType() == node.ResolveUnionType())
                        {
                            node = node.Parent;
                        }

                        // At this point, node is either a qualified name or an identifier
                        Contract.Assert(
                            node.Kind == SyntaxKind.Identifier || node.Kind == SyntaxKind.QualifiedName ||
                                        node.Kind == SyntaxKind.PropertyAccessExpression, "'node' was expected to be a qualified name, identifier or property access in 'isTypeNode'.");
                        break;
                }

                case SyntaxKind.QualifiedName:
                case SyntaxKind.PropertyAccessExpression:
                case SyntaxKind.ThisKeyword:
                    {
                        var parent = node.Parent;
                        if (parent.Kind == SyntaxKind.TypeQuery)
                        {
                            return false;
                        }

                        // Do not recursively call isTypeNode on the parent. In the example:
                        //
                        //     var A a.B.C;
                        //
                        // Calling isTypeNode would consider the qualified name A.B a type node. Only C or
                        // A.B.C is a type node.
                        if (parent.Kind >= SyntaxKind.FirstTypeNode && parent.Kind <= SyntaxKind.LastTypeNode)
                        {
                            return true;
                        }

                        switch (parent.Kind)
                        {
                            case SyntaxKind.ExpressionWithTypeArguments:
                                return !IsExpressionWithTypeArgumentsInClassExtendsClause(parent);
                            case SyntaxKind.TypeParameter:
                                return node.ResolveUnionType() == parent.Cast<ITypeParameterDeclaration>().Constraint;
                            case SyntaxKind.PropertyDeclaration:
                            case SyntaxKind.PropertySignature:
                            case SyntaxKind.Parameter:
                            case SyntaxKind.VariableDeclaration:
                                return node == parent.Cast<IVariableLikeDeclaration>().Type;
                            case SyntaxKind.FunctionDeclaration:
                            case SyntaxKind.FunctionExpression:
                            case SyntaxKind.ArrowFunction:
                            case SyntaxKind.Constructor:
                            case SyntaxKind.MethodDeclaration:
                            case SyntaxKind.MethodSignature:
                            case SyntaxKind.GetAccessor:
                            case SyntaxKind.SetAccessor:
                                return node == parent.Cast<IFunctionLikeDeclaration>().Type;
                            case SyntaxKind.CallSignature:
                            case SyntaxKind.ConstructSignature:
                            case SyntaxKind.IndexSignature:
                                return node == parent.Cast<ISignatureDeclaration>().Type;
                            case SyntaxKind.TypeAssertionExpression:
                                return node == parent.Cast<ITypeAssertion>().Type;
                            case SyntaxKind.CallExpression:
                            case SyntaxKind.NewExpression:
                                return parent.Cast<ICallExpression>().TypeArguments != null && (CollectionExtensions.IndexOf(parent.Cast<ICallExpression>().TypeArguments, node.As<ITypeNode>()) >= 0);
                            case SyntaxKind.TaggedTemplateExpression:
                                // TODO: TaggedTemplateExpressions may eventually support type arguments.
                                return false;
                        }

                        break;
                    }
            }

            return false;
        }

        /// <summary>
        /// This Warning has the same semantics as the forEach family of functions,
        /// in that traversal terminates in the event that 'visitor' supplies a truthy value.
        /// </summary>
        public static T ForEachReturnStatement<T>(IBlock body, Func<IReturnStatement, T> visitor) where T : class
        {
            Func<INode, T> traverse = null;
            traverse = (INode node) =>
            {
                switch (node.Kind)
                {
                    case SyntaxKind.ReturnStatement:
                        return visitor(node.Cast<IReturnStatement>());
                    case SyntaxKind.CaseBlock:
                    case SyntaxKind.Block:
                    case SyntaxKind.IfStatement:
                    case SyntaxKind.DoStatement:
                    case SyntaxKind.WhileStatement:
                    case SyntaxKind.ForStatement:
                    case SyntaxKind.ForInStatement:
                    case SyntaxKind.ForOfStatement:
                    case SyntaxKind.WithStatement:
                    case SyntaxKind.SwitchStatement:
                    case SyntaxKind.CaseClause:
                    case SyntaxKind.DefaultClause:
                    case SyntaxKind.LabeledStatement:
                    case SyntaxKind.TryStatement:
                    case SyntaxKind.CatchClause:
                        return NodeWalker.ForEachChild(node, traverse);
                }

                return default(T);
            };

            return traverse(body);
        }

        /// <nodoc/>
        public static void ForEachYieldExpression(IBlock body, Action<IYieldExpression> visitor)
        {
            Action<INode> traverse = null;
            traverse = (INode node) =>
            {
                switch (node.Kind)
                {
                    case SyntaxKind.YieldExpression:
                        visitor(node.Cast<IYieldExpression>());
                        var operand = node.Cast<IYieldExpression>().Expression;
                        if (operand != null)
                        {
                            traverse(operand);
                        }

                        break;
                    case SyntaxKind.EnumDeclaration:
                    case SyntaxKind.InterfaceDeclaration:
                    case SyntaxKind.ModuleDeclaration:
                    case SyntaxKind.TypeAliasDeclaration:
                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.ClassExpression:
                        // These are not allowed inside a generator now, but eventually they may be allowed
                        // as local types. Regardless, any yield statements contained within them should be
                        // skipped in this traversal.
                        return;
                    default:
                        if (IsFunctionLike(node) != null)
                        {
                            var name = node.Cast<IFunctionLikeDeclaration>().Name;
                            if (name?.Kind == SyntaxKind.ComputedPropertyName)
                            {
                                // Note that we will not include methods/accessors of a class because they would require
                                // first descending into the class. This is by design.
                                traverse(name.Expression);
                                return;
                            }
                        }
                        else if (!IsTypeNode(node))
                        {
                            // This is the general case, which should include mostly expressions and statements.
                            // Also includes NodeArrays.
                            Func<INode, INode> cbNode = n =>
                            {
                                traverse(n);
                                return null;
                            };
                            NodeWalker.ForEachChild(node, cbNode);
                        }

                        break;
                }
            };

            traverse(body);
        }

        /// <nodoc/>
        public static IVariableLikeDeclaration IsVariableLike(INode node) // node is VariableLikeDeclaration
        {
            if (node != null)
            {
                switch (node.Kind)
                {
                    case SyntaxKind.BindingElement:
                    case SyntaxKind.EnumMember:
                    case SyntaxKind.Parameter:
                    case SyntaxKind.PropertyAssignment:
                    case SyntaxKind.PropertyDeclaration:
                    case SyntaxKind.PropertySignature:
                    case SyntaxKind.ShorthandPropertyAssignment:
                    case SyntaxKind.VariableDeclaration:
                        return node.Cast<IVariableLikeDeclaration>();
                }
            }

            return null;
        }

        /// <nodoc/>
        public static IAccessorDeclaration IsAccessor(INode node) // node is AccessorDeclaration
        {
            return
                node != null && (node.Kind == SyntaxKind.GetAccessor || node.Kind == SyntaxKind.SetAccessor)
                    ? node.Cast<IAccessorDeclaration>()
                    : null;
        }

        /// <nodoc/>
        public static IClassLikeDeclaration IsClassLike(INode node) // node is ClassLikeDeclaration
        {
            return
                node != null && (node.Kind == SyntaxKind.ClassDeclaration || node.Kind == SyntaxKind.ClassExpression)
                    ? node.Cast<IClassLikeDeclaration>()
                    : null;
        }

        /// <nodoc/>
        public static IFunctionLikeDeclaration IsFunctionLike(INode node) // node is FunctionLikeDeclaration
        {
            return
                node != null && IsFunctionLikeKind(node.Kind)
                    ? node.Cast<IFunctionLikeDeclaration>()
                    : null;
        }

        /// <nodoc/>
        public static bool IsFunctionLikeKind(SyntaxKind kind)
        {
            switch (kind)
            {
                case SyntaxKind.Constructor:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.CallSignature:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                    return true;
            }

            return false;
        }

        /// <nodoc/>
        public static bool IntroducesArgumentsExoticObject(INode node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionExpression:
                    return true;
            }

            return false;
        }

        /// <nodoc/>
        public static bool IsIterationStatement(INode node, bool lookInLabeledStatements)
        {
            switch (node.Kind)
            {
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.WhileStatement:
                    return true;
                case SyntaxKind.LabeledStatement:
                    return lookInLabeledStatements && IsIterationStatement(node.Cast<ILabeledStatement>().Statement, lookInLabeledStatements);
            }

            return false;
        }

        /// <nodoc/>
        public static bool IsFunctionBlock(INode node)
        {
            return node != null && node.Kind == SyntaxKind.Block && IsFunctionLike(node.Parent) != null;
        }

        /// <nodoc/>
        [System.Diagnostics.Contracts.Pure]
        public static IMethodDeclaration IsObjectLiteralMethod(INode node) // node is MethodDeclaration
        {
            return
                node != null && node.Kind == SyntaxKind.MethodDeclaration && node.Parent.Kind == SyntaxKind.ObjectLiteralExpression
                    ? node.Cast<IMethodDeclaration>()
                    : null;
        }

        /// <nodoc/>
        [CanBeNull]
        public static IIdentifierTypePredicate IsIdentifierTypePredicate(ITypePredicate predicate) // predicate is IdentifierTypePredicate
        {
            return
                predicate != null && predicate.Kind == TypePredicateKind.Identifier
                    ? (IIdentifierTypePredicate)predicate
                    : null;
        }

        /// <nodoc/>
        [CanBeNull]
        public static IFunctionLikeDeclaration GetContainingFunction(INode node)
        {
            while (node != null)
            {
                node = node.Parent;

                // Original code!
                // if (!node || isFunctionLike(node)) {
                if (node == null || IsFunctionLike(node) != null)
                {
                    // node could be null in this case.
                    // ?. is needed because Cast expect not-null value.
                    return node?.Cast<IFunctionLikeDeclaration>();
                }
            }

            return null;
        }

        /// <nodoc/>
        [CanBeNull]
        public static IClassLikeDeclaration GetContainingClass(INode node)
        {
            while (node != null)
            {
                node = node.Parent;
                if (node != null && IsClassLike(node) != null)
                {
                    return node.Cast<IClassLikeDeclaration>();
                }
            }

            return null;
        }

        /// <nodoc/>
        [CanBeNull]
        public static INode GetThisContainer(INode node, bool includeArrowFunctions)
        {
            while (node != null)
            {
                node = node.Parent;
                if (node == null)
                {
                    return null;
                }

                switch (node.Kind)
                {
                    case SyntaxKind.ComputedPropertyName:
                        // If the grandparent node is an object literal (as opposed to a class),
                        // then the computed property is not a 'this' container.
                        // A computed property name in a class needs to be a this container
                        // so that we can error on it.
                        if (IsClassLike(node.Parent.Parent) != null)
                        {
                            return node;
                        }

                        // If this is a computed property, then the parent should not
                        // make it a this container. The parent might be a property
                        // in an object literal, like a method or accessor. But in order for
                        // such a parent to be a this container, the reference must be in
                        // the *body* of the container.
                        node = node.Parent;
                        break;
                    case SyntaxKind.Decorator:
                        // Decorators are always applied outside of the body of a class or method.
                        if (node.Parent.Kind == SyntaxKind.Parameter && IsClassElement(node.Parent.Parent))
                        {
                            // If the decorator's parent is a Parameter, we resolve the this container from
                            // the grandparent class declaration.
                            node = node.Parent.Parent;
                        }
                        else if (IsClassElement(node.Parent))
                        {
                            // If the decorator's parent is a class element, we resolve the 'this' container
                            // from the parent class declaration.
                            node = node.Parent;
                        }

                        break;
                    case SyntaxKind.ArrowFunction:
                        if (!includeArrowFunctions)
                        {
                            // throw new NotImplementedException("Not sure about this pice!");
                            continue;
                        }

                        goto case SyntaxKind.FunctionDeclaration;

                        // Fall through
                    case SyntaxKind.FunctionDeclaration:
                    case SyntaxKind.FunctionExpression:
                    case SyntaxKind.ModuleDeclaration:
                    case SyntaxKind.PropertyDeclaration:
                    case SyntaxKind.PropertySignature:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.MethodSignature:
                    case SyntaxKind.Constructor:
                    case SyntaxKind.GetAccessor:
                    case SyntaxKind.SetAccessor:
                    case SyntaxKind.CallSignature:
                    case SyntaxKind.ConstructSignature:
                    case SyntaxKind.IndexSignature:
                    case SyntaxKind.EnumDeclaration:
                    case SyntaxKind.SourceFile:
                        return node;
                }
            }

            return null;
        }

        /// <nodoc/>
        [CanBeNull]
        public static INode GetSuperContainer(INode node, bool includeFunctions)
        {
            while (true)
            {
                node = node.Parent;
                if (node == null)
                {
                    return node;
                }

                switch (node.Kind)
                {
                    case SyntaxKind.ComputedPropertyName:
                        // If the grandparent node is an object literal (as opposed to a class),
                        // then the computed property is not a 'super' container.
                        // A computed property name in a class needs to be a super container
                        // so that we can error on it.
                        if (IsClassLike(node.Parent.Parent) != null)
                        {
                            return node;
                        }

                        // If this is a computed property, then the parent should not
                        // make it a super container. The parent might be a property
                        // in an object literal, like a method or accessor. But in order for
                        // such a parent to be a super container, the reference must be in
                        // the *body* of the container.
                        node = node.Parent;
                        break;
                    case SyntaxKind.Decorator:
                        // Decorators are always applied outside of the body of a class or method.
                        if (node.Parent.Kind == SyntaxKind.Parameter && IsClassElement(node.Parent.Parent))
                        {
                            // If the decorator's parent is a Parameter, we resolve the this container from
                            // the grandparent class declaration.
                            node = node.Parent.Parent;
                        }
                        else if (IsClassElement(node.Parent))
                        {
                            // If the decorator's parent is a class element, we resolve the 'this' container
                            // from the parent class declaration.
                            node = node.Parent;
                        }

                        break;
                    case SyntaxKind.FunctionDeclaration:
                    case SyntaxKind.FunctionExpression:
                    case SyntaxKind.ArrowFunction:
                        {
                            if (!includeFunctions)
                            {
                                continue;
                            }

                            goto case SyntaxKind.FunctionDeclaration;
                        }

                    case SyntaxKind.PropertyDeclaration:
                    case SyntaxKind.PropertySignature:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.MethodSignature:
                    case SyntaxKind.Constructor:
                    case SyntaxKind.GetAccessor:
                    case SyntaxKind.SetAccessor:
                        return node;
                }
            }
        }

        /// <nodoc/>
        [CanBeNull]
        public static /* EntityName | Expression */ INode GetEntityNameFromTypeNode(ITypeNode node)
        {
            if (node != null)
            {
                switch (node.Kind)
                {
                    case SyntaxKind.TypeReference:
                        return node.Cast<ITypeReferenceNode>().TypeName;
                    case SyntaxKind.ExpressionWithTypeArguments:
                        return node.Cast<IExpressionWithTypeArguments>().Expression;

                    // TODO: Check equivalence
                    //       return (< EntityName >< Node > node);
                    case SyntaxKind.Identifier:
                        return node.Cast<IIdentifier>();
                    case SyntaxKind.QualifiedName:
                        return node.Cast<IQualifiedName>();
                }
            }

            return null;
        }

        /// <nodoc />
        public static bool NodeCanBeDecorated(INode node, bool isScriptFile = false)
        {
            switch (node.Kind)
            {
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                    return true;

                case SyntaxKind.ImportDeclaration:
                case SyntaxKind.ImportClause:
                case SyntaxKind.VariableStatement:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.ExportDeclaration:
                    // classes are valid targets
                    // DS: import statements, interfaces, type aliases, functions variables, namespaces and export declarations are valid targets as well
                    return isScriptFile;

                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.EnumMember:
                    // DS: enums could have an annotations in DScript
                    return isScriptFile;

                // DS: for DScript interface members are valid targets!
                case SyntaxKind.PropertySignature:
                    return isScriptFile && (node.Parent.Kind == SyntaxKind.InterfaceDeclaration || node.Parent.Kind == SyntaxKind.TypeLiteral);

                case SyntaxKind.PropertyDeclaration:
                    // property declarations are valid if their parent is a class declaration.
                    return node.Parent.Kind == SyntaxKind.ClassDeclaration || (isScriptFile && node.Parent.Kind == SyntaxKind.InterfaceDeclaration);

                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.MethodDeclaration:
                    // if this method has a body and its parent is a class declaration, this is a valid target.
                    return node.Cast<IFunctionLikeDeclaration>().Body != null
                        && node.Parent.Kind == SyntaxKind.ClassDeclaration;

                case SyntaxKind.Parameter:
                    // if the parameter's parent has a body and its grandparent is a class declaration, this is a valid target;
                    return node.Parent.Cast<IFunctionLikeDeclaration>().Body != null
                        && (node.Parent.Kind == SyntaxKind.Constructor
                        || node.Parent.Kind == SyntaxKind.MethodDeclaration
                        || node.Parent.Kind == SyntaxKind.SetAccessor)
                        && (node.Parent.Parent.Kind == SyntaxKind.ClassDeclaration || (isScriptFile && node.Parent.Kind == SyntaxKind.InterfaceDeclaration));
                case SyntaxKind.StringLiteralType:
                    // DS: string literal types could have annotations in DScript
                    return isScriptFile;
            }

            return false;
        }

        /// <nodoc />
        public static bool NodeIsDecorated(INode node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.ClassDeclaration:
                    if (node.Decorators != null)
                    {
                        return true;
                    }

                    return false;

                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.Parameter:
                    if (node.Decorators != null)
                    {
                        return true;
                    }

                    return false;

                case SyntaxKind.GetAccessor:
                    if (node.Cast<IFunctionLikeDeclaration>().Body != null && node.Decorators != null)
                    {
                        return true;
                    }

                    return false;

                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.SetAccessor:
                    if (node.Cast<IFunctionLikeDeclaration>().Body != null && node.Decorators != null)
                    {
                        return true;
                    }

                    return false;
            }

            return false;
        }

        /// <nodoc/>
        public static IPropertyAccessExpression IsPropertyAccessExpression(INode node) // node is PropertyAccessExpression
        {
            return node.Kind == SyntaxKind.PropertyAccessExpression ? node.Cast<IPropertyAccessExpression>() : null;
        }

        /// <nodoc/>
        public static IElementAccessExpression IsElementAccessExpression(INode node) // node is ElementAccessExpression
        {
            return node.Kind == SyntaxKind.ElementAccessExpression ? node.Cast<IElementAccessExpression>() : null;
        }

        /// <nodoc/>
        public static bool IsExpression(INode node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.SuperKeyword:
                case SyntaxKind.NullKeyword:
                case SyntaxKind.TrueKeyword:
                case SyntaxKind.FalseKeyword:
                case SyntaxKind.RegularExpressionLiteral:
                case SyntaxKind.ArrayLiteralExpression:
                case SyntaxKind.ObjectLiteralExpression:
                case SyntaxKind.PropertyAccessExpression:
                case SyntaxKind.ElementAccessExpression:
                case SyntaxKind.CallExpression:
                case SyntaxKind.NewExpression:
                case SyntaxKind.TaggedTemplateExpression:
                case SyntaxKind.AsExpression:
                case SyntaxKind.TypeAssertionExpression:
                case SyntaxKind.ParenthesizedExpression:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ClassExpression:
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.VoidExpression:
                case SyntaxKind.DeleteExpression:
                case SyntaxKind.TypeOfExpression:
                case SyntaxKind.PrefixUnaryExpression:
                case SyntaxKind.PostfixUnaryExpression:
                case SyntaxKind.BinaryExpression:
                case SyntaxKind.ConditionalExpression:
                case SyntaxKind.SwitchExpression:
                case SyntaxKind.SwitchExpressionClause:
                case SyntaxKind.SpreadElementExpression:
                case SyntaxKind.TemplateExpression:
                case SyntaxKind.NoSubstitutionTemplateLiteral:
                case SyntaxKind.OmittedExpression:
                case SyntaxKind.YieldExpression:
                case SyntaxKind.AwaitExpression:
                    return true;
                case SyntaxKind.QualifiedName:
                    while (node.Parent.Kind == SyntaxKind.QualifiedName)
                    {
                        node = node.Parent;
                    }

                    return node.Parent.Kind == SyntaxKind.TypeQuery;
                case SyntaxKind.Identifier:
                    {
                        if (node.Parent.Kind == SyntaxKind.TypeQuery)
                        {
                            return true;
                        }

                        goto case SyntaxKind.NumericLiteral;
                    }

                // fall through
                case SyntaxKind.NumericLiteral:
                case SyntaxKind.StringLiteral:
                case SyntaxKind.ThisKeyword:
                    {
                        var parent = node.Parent;
                        switch (parent.Kind)
                        {
                            case SyntaxKind.VariableDeclaration:
                            case SyntaxKind.Parameter:
                            case SyntaxKind.PropertyDeclaration:
                            case SyntaxKind.PropertySignature:
                            case SyntaxKind.EnumMember:
                            case SyntaxKind.PropertyAssignment:
                            case SyntaxKind.BindingElement:
                                return parent.Cast<IVariableLikeDeclaration>().Initializer.ResolveUnionType() == node.ResolveUnionType();
                            case SyntaxKind.ExpressionStatement:
                            case SyntaxKind.IfStatement:
                            case SyntaxKind.DoStatement:
                            case SyntaxKind.WhileStatement:
                            case SyntaxKind.ReturnStatement:
                            case SyntaxKind.WithStatement:
                            case SyntaxKind.CaseClause:
                            case SyntaxKind.ThrowStatement:
                            case SyntaxKind.SwitchStatement:
                                return parent.Cast<IExpressionStatement>().Expression.ResolveUnionType() == node.ResolveUnionType();
                            case SyntaxKind.ForStatement:
                                {
                                    var forStatement = parent.Cast<IForStatement>();
                                    INode initializer = forStatement.Initializer;
                                    return (initializer.ResolveUnionType() == node.ResolveUnionType() && initializer.Kind != SyntaxKind.VariableDeclarationList)
                                           || forStatement.Condition.ResolveUnionType() == node.ResolveUnionType() || forStatement.Incrementor.ResolveUnionType() == node.ResolveUnionType();
                                }

                            case SyntaxKind.ForInStatement:
                                {
                                    var forInStatement = parent.Cast<IForInStatement>();
                                    return (forInStatement.Initializer.ResolveUnionType() == node.ResolveUnionType() && forInStatement.Initializer.Kind != SyntaxKind.VariableDeclarationList) ||
                                           forInStatement.Expression.ResolveUnionType() == node.ResolveUnionType();
                                }

                            case SyntaxKind.ForOfStatement:
                                {
                                    var forInStatement = parent.Cast<IForOfStatement>();
                                    return (forInStatement.Initializer.ResolveUnionType() == node.ResolveUnionType() && forInStatement.Initializer.Kind != SyntaxKind.VariableDeclarationList) ||
                                           forInStatement.Expression.ResolveUnionType() == node.ResolveUnionType();
                                }

                            case SyntaxKind.TypeAssertionExpression:
                                return node.ResolveUnionType() == parent.Cast<ITypeAssertion>().Expression.ResolveUnionType();
                            case SyntaxKind.AsExpression:
                                return node.ResolveUnionType() == parent.Cast<IAsExpression>().Expression.ResolveUnionType();
                            case SyntaxKind.TemplateSpan:
                                return node.ResolveUnionType() == parent.Cast<ITemplateSpan>().Expression.ResolveUnionType();
                            case SyntaxKind.ComputedPropertyName:
                                return node.ResolveUnionType() == parent.Cast<IComputedPropertyName>().Expression.ResolveUnionType();
                            case SyntaxKind.Decorator:
                                return true;
                            case SyntaxKind.ExpressionWithTypeArguments:
                                return parent.Cast<IExpressionWithTypeArguments>().Expression.ResolveUnionType() == node.ResolveUnionType() && IsExpressionWithTypeArgumentsInClassExtendsClause(parent);
                            default:
                                if (IsExpression(parent))
                                {
                                    return true;
                                }

                                break;
                        }
                    }

                    break;
            }

            return false;
        }

        /// <nodoc/>
        [System.Diagnostics.Contracts.Pure]
        public static bool IsExternalModuleImportEqualsDeclaration(INode node)
        {
            return node.Kind == SyntaxKind.ImportEqualsDeclaration && node.Cast<IImportEqualsDeclaration>().ModuleReference.Kind == SyntaxKind.ExternalModuleReference;
        }

        /// <nodoc/>
        public static IExpression GetExternalModuleImportEqualsDeclarationExpression(INode node)
        {
            Contract.Requires(IsExternalModuleImportEqualsDeclaration(node));

            return node.Cast<IImportEqualsDeclaration>().ModuleReference.Cast<IExternalModuleReference>().Expression;
        }

        /// <nodoc/>
        public static bool IsInternalModuleImportEqualsDeclaration(INode node) // node is ImportEqualsDeclaration
        {
            return node.Kind == SyntaxKind.ImportEqualsDeclaration &&
                   node.Cast<IImportEqualsDeclaration>().ModuleReference.Kind != SyntaxKind.ExternalModuleReference;
        }

        /// <nodoc/>
        [Obsolete("Use node.IsJavaScriptFile instead.")]
        public static bool IsSourceFileJavaScript(ISourceFile file)
        {
            return IsInJavaScriptFile(file);
        }

        /// <nodoc/>
        [Obsolete("Use node.IsJavaScriptFile instead.")]
        public static bool IsInJavaScriptFile(INode node)
        {
            return node.IsJavaScriptFile();
        }

        /// <summary>
        /// Returns true if the node is a CallExpression to the identifier 'require' with
        /// exactly one string literal argument.
        /// This  does not test if the node is in a JavaScript file or not.
        /// </summary>
        public static ICallExpression IsRequireCall(INode expression) // expression is CallExpression
        {
            // of the form 'require("name")'
            bool isValid = expression.Kind == SyntaxKind.CallExpression &&
                           ((ICallExpression)expression).Expression.Kind == SyntaxKind.Identifier &&
                           ((IIdentifier)((ICallExpression)expression).Expression).Text == "require" &&
                           ((ICallExpression)expression).Arguments.Length == 1 &&
                           ((ICallExpression)expression).Arguments[0].Kind == SyntaxKind.StringLiteral;

            return isValid ? (ICallExpression)expression : null;
        }

        /// <summary>
        /// Given a BinaryExpression, returns SpecialPropertyAssignmentKind for the various kinds of property
        /// assignments we treat as special in the binder
        /// </summary>
        public static SpecialPropertyAssignmentKind GetSpecialPropertyAssignmentKind(INode expression)
        {
            if (expression.Kind != SyntaxKind.BinaryExpression)
            {
                return SpecialPropertyAssignmentKind.None;
            }

            var expr = (IBinaryExpression)expression;
            if (expr.OperatorToken.Kind != SyntaxKind.EqualsToken || expr.Left.Kind != SyntaxKind.PropertyAccessExpression)
            {
                return SpecialPropertyAssignmentKind.None;
            }

            var lhs = (IPropertyAccessExpression)expr.Left;
            if (lhs.Expression.Kind == SyntaxKind.Identifier)
            {
                var lhsId = (IIdentifier)lhs.Expression;
                if (lhsId.Text == "exports")
                {
                    // exports.Name = expr
                    return SpecialPropertyAssignmentKind.ExportsProperty;
                }
                else if (lhsId.Text == "module" && lhs.Name.Text == "exports")
                {
                    // module.exports = expr
                    return SpecialPropertyAssignmentKind.ModuleExports;
                }
            }
            else if (lhs.Expression.Kind == SyntaxKind.ThisKeyword)
            {
                return SpecialPropertyAssignmentKind.ThisProperty;
            }
            else if (lhs.Expression.Kind == SyntaxKind.PropertyAccessExpression)
            {
                // chained dot, e.g. x.y.z = expr; this var is the 'x.y' part
                var innerPropertyAccess = (IPropertyAccessExpression)lhs.Expression;
                if (innerPropertyAccess.Expression.Kind == SyntaxKind.Identifier && innerPropertyAccess.Name.Text == "prototype")
                {
                    return SpecialPropertyAssignmentKind.PrototypeProperty;
                }
            }

            return SpecialPropertyAssignmentKind.None;
        }

        /// <nodoc/>
        public static IExpression GetExternalModuleName(INode node)
        {
            if (node.Kind == SyntaxKind.ImportDeclaration)
            {
                return node.Cast<IImportDeclaration>().ModuleSpecifier;
            }

            if (node.Kind == SyntaxKind.ImportEqualsDeclaration)
            {
                var reference = node.Cast<IImportEqualsDeclaration>().ModuleReference;
                if (reference.Kind == SyntaxKind.ExternalModuleReference)
                {
                    return reference.Cast<IExternalModuleReference>().Expression;
                }
            }

            if (node.Kind == SyntaxKind.ExportDeclaration)
            {
                return node.Cast<IExportDeclaration>().ModuleSpecifier;
            }

            return null;
        }

        /// <nodoc/>
        public static bool HasQuestionToken(INode node)
        {
            if (node != null)
            {
                switch (node.Kind)
                {
                    case SyntaxKind.Parameter:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.MethodSignature:
                    case SyntaxKind.ShorthandPropertyAssignment:
                    case SyntaxKind.PropertyAssignment:
                    case SyntaxKind.PropertyDeclaration:
                    case SyntaxKind.PropertySignature:
                    {
                        return node.As<IParameterDeclaration>()?.QuestionToken.ValueOrDefault != null ||
                               node.As<IMethodDeclaration>()?.QuestionToken.ValueOrDefault != null ||
                               node.As<IPropertyDeclaration>()?.QuestionToken.ValueOrDefault != null;
                    }
                }
            }

            return false;
        }

        /// <nodoc/>
        public static bool IsJsDocConstructSignature(INode node)
        {
            return node.Kind == SyntaxKind.JsDocFunctionType &&
                   node.Cast<IJsDocFunctionType>().Parameters?.FirstOrDefault()?.Type?.Kind == SyntaxKind.JsDocConstructorType;
        }

        /// <nodoc/>
        public static IJsDocTypeTag GetJsDocTypeTag(INode node)
        {
            return (IJsDocTypeTag)GetJsDocTag(node, SyntaxKind.JsDocTypeTag);
        }

        /// <nodoc/>
        public static IJsDocReturnTag GetJsDocReturnTag(INode node)
        {
            return (IJsDocReturnTag)GetJsDocTag(node, SyntaxKind.JsDocReturnTag);
        }

        /// <nodoc/>
        public static IJsDocTemplateTag GetJsDocTemplateTag(INode node)
        {
            return (IJsDocTemplateTag)GetJsDocTag(node, SyntaxKind.JsDocTemplateTag);
        }

        /// <nodoc/>
        public static IJsDocParameterTag GetCorrespondingJsDocParameterTag(IParameterDeclaration parameter)
        {
            if (parameter?.Name.Kind == SyntaxKind.Identifier)
            {
                // If it's a parameter, see if the parent has a jsdoc comment with an @param
                // annotation.
                var parameterName = ((IIdentifier)parameter.Name).Text;

                // var docComment = parameter.Parent.JsDocComment;
                // if (docComment.HasValue)
                // {
                //    throw PlaceHolder.NotImplemented();
                //    //return (JSDocParameterTag) forEach(docComment.tags, t =>
                //    //{
                //    //    if (t.kind == SyntaxKind.JSDocParameterTag)
                //    //    {
                //    //        var parameterTag = (JSDocParameterTag) t;
                //    //        var Name = parameterTag.preParameterName || parameterTag.postParameterName;
                //    //        if (Name.text == parameterName)
                //    //        {
                //    //            return t;
                //    //        }
                //    //    }
                //    //});
                // }
            }

            return null;
        }

        /// <nodoc/>
        public static bool HasRestParameter(ISignatureDeclaration s)
        {
            return IsRestParameter(s.Parameters.LastOrDefault());
        }

        /// <nodoc/>
        public static bool IsRestParameter(IParameterDeclaration node)
        {
            if (node != null)
            {
                if (node.IsJavaScriptFile())
                {
                    if (node?.Type?.Kind == SyntaxKind.JsDocVariadicType)
                    {
                        return true;
                    }

                    var paramTag = GetCorrespondingJsDocParameterTag(node);
                    if (paramTag?.TypeExpression != null)
                    {
                        return paramTag.TypeExpression.ValueOrDefault?.Type?.Kind == SyntaxKind.JsDocVariadicType;
                    }
                }

                return node.DotDotDotToken;
            }

            return false;
        }

        /// <nodoc/>
        public static IBindingPattern IsBindingPattern(INode node) // node is BindingPattern
        {
            return node?.Kind == SyntaxKind.ArrayBindingPattern || node?.Kind == SyntaxKind.ObjectBindingPattern
                ? node.Cast<IBindingPattern>()
                : null;
        }

        /// <nodoc/>
        public static bool IsNodeDescendentOf(INode node, INode ancestor)
        {
            while (node != null)
            {
                if (node.ResolveUnionType() == ancestor.ResolveUnionType())
                {
                    return true;
                }

                node = node.Parent;
            }

            return false;
        }

        /// <nodoc/>
        public static bool IsInAmbientContext(INode node)
        {
            while (node != null)
            {
                if ((node.Flags & (NodeFlags.Ambient | NodeFlags.DeclarationFile)) != NodeFlags.None)
                {
                    return true;
                }

                node = node.Parent;
            }

            return false;
        }

        /// <nodoc/>
        [System.Diagnostics.Contracts.Pure]
        public static bool IsDeclaration(INode node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.BindingElement:
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                case SyntaxKind.Constructor:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.EnumMember:
                case SyntaxKind.ExportSpecifier:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.ImportClause:
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.ImportSpecifier:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.NamespaceImport:
                case SyntaxKind.Parameter:
                case SyntaxKind.PropertyAssignment:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.ShorthandPropertyAssignment:
                case SyntaxKind.TypeAliasDeclaration:
                case SyntaxKind.TypeParameter:
                case SyntaxKind.VariableDeclaration:
                    return true;
            }

            return false;
        }

        /// <nodoc/>
        public static bool IsStatement(INode n)
        {
            switch (n.Kind)
            {
                case SyntaxKind.BreakStatement:
                case SyntaxKind.ContinueStatement:
                case SyntaxKind.DebuggerStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.ExpressionStatement:
                case SyntaxKind.EmptyStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.IfStatement:
                case SyntaxKind.LabeledStatement:
                case SyntaxKind.ReturnStatement:
                case SyntaxKind.SwitchStatement:
                case SyntaxKind.ThrowStatement:
                case SyntaxKind.TryStatement:
                case SyntaxKind.VariableStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.WithStatement:
                case SyntaxKind.ExportAssignment:
                    return true;
                default:
                    return false;
            }
        }

        /// <nodoc/>
        public static bool IsClassElement(INode n)
        {
            switch (n.Kind)
            {
                case SyntaxKind.Constructor:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.IndexSignature:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True if the given identifier, string literal, or int literal is the name of a declaration node
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        public static DeclarationName IsDeclarationName(INode name) // name is Identifier | StringLiteral | LiteralExpression
        {
            if (name.Kind != SyntaxKind.Identifier && name.Kind != SyntaxKind.StringLiteral && name.Kind != SyntaxKind.NumericLiteral)
            {
                return null;
            }

            var parent = name.Parent;
            if (parent.Kind == SyntaxKind.ImportSpecifier || parent.Kind == SyntaxKind.ExportSpecifier)
            {
                var propertyName = parent.Cast<IImportOrExportSpecifier>().PropertyName;

                if (propertyName != null)
                {
                    return DeclarationName.PropertyName(propertyName);
                }
            }

            if (IsDeclaration(parent))
            {
                var parentName = parent.Cast<IDeclaration>().Name;
                if (parentName.ResolveUnionType() == name.ResolveUnionType())
                {
                    return parentName;
                }
            }

            return null;
        }

        /// <summary>
        /// Return true if the given identifier is classified as an IdentifierName
        /// </summary>
        public static bool IsIdentifierName(IIdentifier node)
        {
            var parent = node.Parent;
            switch (parent.Kind)
            {
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.EnumMember:
                case SyntaxKind.PropertyAssignment:
                case SyntaxKind.PropertyAccessExpression:
                    // Name in member declaration or property name in property access
                    {
                        // Ts code: return (<Declaration | PropertyAccessExpression> parent).Name == node;
                        var propertyAccess = parent.As<IPropertyAccessExpression>();
                        if (propertyAccess != null)
                        {
                            return propertyAccess.Name.ResolveUnionType() == node.ResolveUnionType();
                        }

                        var declaration = parent.Cast<IDeclaration>();
                        return declaration.Name.ResolveUnionType() == node.ResolveUnionType();
                    }

                case SyntaxKind.QualifiedName:
                    // Name on right hand side of dot in a type query
                    if (parent.Cast<IQualifiedName>().Right.ResolveUnionType() == node.ResolveUnionType())
                    {
                        while (parent.Kind == SyntaxKind.QualifiedName)
                        {
                            parent = parent.Parent;
                        }

                        return parent.Kind == SyntaxKind.TypeQuery;
                    }

                    return false;
                case SyntaxKind.BindingElement:
                    return parent.Cast<IBindingElement>().PropertyName.ResolveUnionType() == node.ResolveUnionType();
                case SyntaxKind.ImportSpecifier:
                    return parent.Cast<IImportSpecifier>().PropertyName.ResolveUnionType() == node.ResolveUnionType();
                case SyntaxKind.ExportSpecifier:
                    // Any name in an export specifier
                    return true;
            }

            return false;
        }

        /// <summary>
        /// An alias symbol is created by one of the following declarations:
        /// <code>
        /// import &lt;symbol> = ...
        /// import &lt;symbol> from ...
        /// import * as &lt;symbol> from ...
        /// import { x as &lt;symbol> } from...
        /// export { x as &lt;symbol> } from...
        /// export = ...
        /// export default ...
        /// </code>
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        public static bool IsAliasSymbolDeclaration(INode node)
        {
            return node.Kind == SyntaxKind.ImportEqualsDeclaration ||
                   (node.Kind == SyntaxKind.ImportClause && node.Cast<IImportClause>().Name != null) ||
                   node.Kind == SyntaxKind.NamespaceImport ||
                   node.Kind == SyntaxKind.ImportSpecifier ||
                   node.Kind == SyntaxKind.ExportSpecifier ||
                   (node.Kind == SyntaxKind.ExportAssignment && node.Cast<IExportAssignment>().Expression.Kind == SyntaxKind.Identifier);
        }

        /// <nodoc/>
        public static IExpressionWithTypeArguments GetClassExtendsHeritageClauseElement(IClassLikeDeclaration node)
        {
            var heritageClause = GetHeritageClause(node.HeritageClauses, SyntaxKind.ExtendsKeyword);
            return heritageClause?.Types?.FirstOrDefault();
        }

        /// <nodoc/>
        public static NodeArray<IExpressionWithTypeArguments> GetClassImplementsHeritageClauseElements(IClassLikeDeclaration node)
        {
            var heritageClause = GetHeritageClause(node.HeritageClauses, SyntaxKind.ImplementsKeyword);
            return heritageClause?.Types;
        }

        /// <nodoc/>
        public static NodeArray<IExpressionWithTypeArguments> GetInterfaceBaseTypeNodes(IInterfaceDeclaration node)
        {
            var heritageClause = GetHeritageClause(node.HeritageClauses, SyntaxKind.ExtendsKeyword);
            return heritageClause?.Types;
        }

        /// <nodoc/>
        [System.Diagnostics.Contracts.Pure]
        public static IHeritageClause GetHeritageClause(NodeArray<IHeritageClause> clauses, SyntaxKind kind)
        {
            if (clauses != null)
            {
                foreach (var clause in clauses)
                {
                    if (clause.Token == kind)
                    {
                        return clause;
                    }
                }
            }

            return null;
        }

        /// <nodoc/>
        public static INode GetAncestor(INode node, SyntaxKind kind)
        {
            while (node != null)
            {
                if (node.Kind == kind)
                {
                    return node;
                }

                node = node.Parent;
            }

            return null;
        }

        /// <summary>
        /// Returns true if the <paramref name="node"/> is the parent of <paramref name="childCandidate"/>.
        /// </summary>
        public static bool IsParentFor(this INode node, INode childCandidate)
        {
            var parent = childCandidate;
            while (parent != null)
            {
                if (ReferenceEquals(node, parent))
                {
                    return true;
                }

                parent = parent.Parent;
            }

            return false;
        }

        /// <nodoc/>
        public static bool IsAsyncFunctionLike(INode node)
        {
            return IsFunctionLike(node) != null && ((node.Flags & NodeFlags.Async) != NodeFlags.None) && IsAccessor(node) == null;
        }

        /// <nodoc/>
        public static bool IsStringOrNumericLiteral(SyntaxKind kind)
        {
            return kind == SyntaxKind.StringLiteral || kind == SyntaxKind.NumericLiteral;
        }

        /// <summary>
        /// A declaration has a dynamic name if both of the following are true:
        ///   1. The declaration has a computed property name
        ///   2. The computed name is *not* expressed as Symbol.name, where name
        ///      is a property of the Symbol varructor that denotes a built in
        ///      Symbol.
        /// </summary>
        public static bool HasDynamicName(IDeclaration declaration)
        {
            var name = declaration.Name;
            return name != null && IsDynamicName(name);
        }

        /// <nodoc/>
        public static bool IsDynamicName(DeclarationName name)
        {
            return (name.Kind == SyntaxKind.ComputedPropertyName) &&
                   !IsStringOrNumericLiteral(name.Cast<IComputedPropertyName>().Expression.Kind) &&
                   !IsWellKnownSymbolSyntactically(name.Cast<IComputedPropertyName>().Expression);
        }

        /// <summary>
        /// Checks if the expression is of the form:
        ///    Symbol.Name
        /// where Symbol is literally the word "Symbol", and name is any identifierName
        /// </summary>
        public static bool IsWellKnownSymbolSyntactically(IExpression node)
        {
            return IsPropertyAccessExpression(node) != null && IsEsSymbolIdentifier(IsPropertyAccessExpression(node).Expression);
        }

        /// <summary>
        /// Includes the word "Symbol" with unicode escapes
        /// </summary>
        public static bool IsEsSymbolIdentifier(INode node)
        {
            return node.Kind == SyntaxKind.Identifier && node.Cast<IIdentifier>().Text == "Symbol";
        }

        /// <nodoc/>
        // TODO:SQ: IVariableLikeDeclaration for this function seems unnecessary
        public static bool IsParameterDeclaration(/*IVariableLikeDeclaration*/ INode node)
        {
            var root = GetRootDeclaration(node);
            return root.Kind == SyntaxKind.Parameter;
        }

        /// <nodoc/>
        public static INode GetRootDeclaration(INode node)
        {
            while (node.Kind == SyntaxKind.BindingElement)
            {
                node = node.Parent.Parent;
            }

            return node;
        }

        /// <nodoc/>
        public static bool NodeStartsNewLexicalEnvironment(INode n)
        {
            return IsFunctionLike(n) != null || n.Kind == SyntaxKind.ModuleDeclaration || n.Kind == SyntaxKind.SourceFile;
        }

        /// <nodoc/>
        public static IQualifiedName IsQualifiedName(INode node) // node is QualifiedName
        {
            return node.Kind == SyntaxKind.QualifiedName ? node.Cast<IQualifiedName>() : null;
        }

        /// <nodoc/>
        [System.Diagnostics.Contracts.Pure]
        public static bool NodeIsSynthesized(INode node)
        {
            return node.Pos == -1;
        }

        /// <nodoc/>
        public static string GetPropertyNameForPropertyNameNode(DeclarationName name)
        {
            if (name.Kind == SyntaxKind.Identifier || name.Kind == SyntaxKind.StringLiteral || name.Kind == SyntaxKind.NumericLiteral)
            {
                // TypeScript code: return (<Identifier | LiteralExpression> Name).text;
                var identifier = name.As<IIdentifier>();
                if (identifier != null)
                {
                    return identifier.Text;
                }

                return name.Cast<ILiteralExpression>().Text;
            }

            if (name.Kind == SyntaxKind.ComputedPropertyName)
            {
                var nameExpression = name.Cast<IComputedPropertyName>().Expression;
                if (IsWellKnownSymbolSyntactically(nameExpression))
                {
                    var rightHandSideName = nameExpression.Cast<IPropertyAccessExpression>().Name.Text;
                    return GetPropertyNameForKnownSymbolName(rightHandSideName);
                }
            }

            return null;
        }

        /// <nodoc/>
        public static string GetPropertyNameForKnownSymbolName(string symbolName)
        {
            return "__@" + symbolName;
        }

        /// <nodoc/>
        public static ITypeNode GetSetAccessorTypeAnnotationNode(IAccessorDeclaration accessor)
        {
            return accessor?.Parameters?.FirstOrDefault()?.Type;
        }

        /// <nodoc/>
        public static bool IsLeftHandSideExpression(IExpression expr)
        {
            if (expr != null)
            {
                switch (expr.Kind)
                {
                    case SyntaxKind.PropertyAccessExpression:
                    case SyntaxKind.ElementAccessExpression:
                    case SyntaxKind.NewExpression:
                    case SyntaxKind.CallExpression:
                    case SyntaxKind.TaggedTemplateExpression:
                    case SyntaxKind.ArrayLiteralExpression:
                    case SyntaxKind.ParenthesizedExpression:
                    case SyntaxKind.ObjectLiteralExpression:
                    case SyntaxKind.ClassExpression:
                    case SyntaxKind.FunctionExpression:
                    case SyntaxKind.Identifier:
                    case SyntaxKind.RegularExpressionLiteral:
                    case SyntaxKind.NumericLiteral:
                    case SyntaxKind.StringLiteral:
                    case SyntaxKind.NoSubstitutionTemplateLiteral:
                    case SyntaxKind.TemplateExpression:
                    case SyntaxKind.FalseKeyword:
                    case SyntaxKind.NullKeyword:
                    case SyntaxKind.ThisKeyword:
                    case SyntaxKind.TrueKeyword:
                    case SyntaxKind.SuperKeyword:
                        return true;
                }
            }

            return false;
        }

        /// <nodoc/>
        public static bool IsExpressionWithTypeArgumentsInClassExtendsClause(INode node)
        {
            return node?.Kind == SyntaxKind.ExpressionWithTypeArguments &&
                   node.Parent?.Cast<IHeritageClause>().Token == SyntaxKind.ExtendsKeyword &&
                   IsClassLike(node.Parent.Parent) != null;
        }

        /// <summary>
        /// Returns false if this heritage clause element's expression contains something unsupported
        /// (i.e., not a name or dotted name).
        /// </summary>
        public static bool IsSupportedExpressionWithTypeArguments(IExpressionWithTypeArguments node)
        {
            return IsSupportedExpressionWithTypeArgumentsRest(node.Expression);
        }

        /// <nodoc/>
        public static bool IsRightSideOfQualifiedNameOrPropertyAccess(INode node)
        {
            return (node.Parent.Kind == SyntaxKind.QualifiedName && node.Parent.Cast<IQualifiedName>().Right.ResolveUnionType() == node.ResolveUnionType()) ||
                   (node.Parent.Kind == SyntaxKind.PropertyAccessExpression && node.Parent.Cast<IPropertyAccessExpression>().Name.ResolveUnionType() == node.ResolveUnionType());
        }

        /// <nodoc/>
        public static bool IsEmptyObjectLiteralOrArrayLiteral(INode expression)
        {
            var kind = expression.Kind;
            if (kind == SyntaxKind.ObjectLiteralExpression)
            {
                return ((IObjectLiteralExpression)expression).Properties.Length == 0;
            }

            if (kind == SyntaxKind.ArrayLiteralExpression)
            {
                return ((IArrayLiteralExpression)expression).Elements.Length == 0;
            }

            return false;
        }

        /// <nodoc/>
        public static bool NeedToAddNewLineAfter(this INode node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.NumericLiteral:
                case SyntaxKind.StringLiteral:
                case SyntaxKind.StringKeyword:
                case SyntaxKind.NumberKeyword:
                case SyntaxKind.LastTypeNode: // this is "Literal" in interface X { y: "Literal"; } declaration
                    return false;
            }

            return true;
        }

        /// <nodoc/>
        public static int GetFullWidth(INode node)
        {
            return node.End - node.Pos;
        }

        /// <nodoc/>
        public static int GetStartPositionOfLine(int line, ISourceFile sourceFile)
        {
            Contract.Assert(line >= 0);
            return sourceFile.LineMap.Map[line];
        }

        /// <summary>
        /// This is a useful  for debugging purposes.
        /// </summary>
        public static string NodePosToString(INode node)
        {
            var file = NodeStructureExtensions.GetSourceFile(node);
            var loc = Scanner.GetLineAndCharacterOfPosition(file, node.Pos);
            return I($"${file.FileName} (${loc.Line + 1},${loc.Character + 1})");
        }

        /// <nodoc/>
        public static int GetStartPosOfNode(INode node)
        {
            return node.Pos;
        }

        /// <summary>
        /// Returns true if this node is missing from the actual source code. A 'missing' node is different
        /// from 'null/defined'. When a node is null (which can happen for optional nodes
        /// in the tree), it is definitely missing. However, a node may be defined, but still be
        /// missing.  This happens whenever the parser knows it needs to parse something, but can't
        /// get anything in the source code that it expects at that location. For example:
        ///
        ///          var a: ;
        ///
        /// Here, the Type in the Type-Annotation is not-optional (as there is a colon in the source
        /// code). So the parser will attempt to parse out a type, and will create an actual node.
        /// However, this node will be 'missing' in the sense that no actual source-code/tokens are
        /// contained within it.
        /// </summary>
        public static bool NodeIsMissing(INode node)
        {
            if (node == null)
            {
                return true;
            }

            // DScript-specific. If the node is injected, it is never missing (regardless of its position).
            return node.Pos == node.End && node.Pos >= 0 && node.Kind != SyntaxKind.EndOfFileToken && (!node.IsInjectedForDScript());
        }

        /// <nodoc/>
        public static bool NodeIsPresent(INode node)
        {
            return !NodeIsMissing(node);
        }

        /// <nodoc/>
        public static int GetTokenPosOfNode(INode node, ISourceFile sourceFile)
        {
            // With nodes that have no width (i.e., 'Missing' nodes), we actually *don't*
            // want to skip trivia because this will launch us forward to the next token.
            if (NodeIsMissing(node))
            {
                return node.Pos;
            }

            return Scanner.SkipTrivia((sourceFile ?? NodeStructureExtensions.GetSourceFile(node)).Text, node.Pos);
        }

        /// <nodoc/>
        public static int GetNonDecoratorTokenPosOfNode(INode node, ISourceFile sourceFile)
        {
            if (NodeIsMissing(node) || node.Decorators == null)
            {
                return GetTokenPosOfNode(node, sourceFile);
            }

            return Scanner.SkipTrivia((sourceFile ?? NodeStructureExtensions.GetSourceFile(node)).Text, node.Decorators.End);
        }

        /// <nodoc/>
        public static T Construct<T>(this T node, SyntaxKind kind, int pos, int end)
            where T : INode
        {
            node.Initialize(kind, pos, end);
            return node;
        }

        /// <nodoc/>
        public static bool NeedSeparatorAfter(this INode statement)
        {
            Contract.Requires(statement != null);

            // Injected nodes don't need a separator (since we don't print them)
            if (statement.IsInjectedForDScript())
            {
                return false;
            }

            switch (statement.Kind)
            {
                case SyntaxKind.IfStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.Constructor:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.Block:
                case SyntaxKind.ModuleBlock:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.EmptyStatement:
                case SyntaxKind.BlankLineStatement:
                case SyntaxKind.WhileStatement:
                // DScript-specific. Comments don't need a separator
                case SyntaxKind.SingleLineCommentTrivia:
                case SyntaxKind.MultiLineCommentTrivia:
                    return false;
            }

            return true;
        }

        /// <nodoc/>
        public static string ToDisplayString(this NodeFlags flags)
        {
            if (flags == NodeFlags.None)
            {
                return string.Empty;
            }

            return string.Join(" ", GetFlagsAsString(flags).Where(flagString => !string.IsNullOrEmpty(flagString)));
        }

        /// <summary>
        /// The same as <see cref="ToDisplayString"/> but with trailing space.
        /// </summary>
        /// <returns>Same as <see cref="ToDisplayString"/> but with trailing space.</returns>
        public static string ToWriteableDisplayString(this NodeFlags flags)
        {
            var result = flags.ToDisplayString();
            if (string.IsNullOrEmpty(result))
            {
                return result;
            }

            return string.Concat(result, " ");
        }

        /// <nodoc/>
        public static bool IsParameterPropertyDeclaration(IParameterDeclaration node)
        {
            return (node.Flags & NodeFlags.AccessibilityModifier) == NodeFlags.AccessibilityModifier &&
                   (node.Parent.Kind == SyntaxKind.Constructor) &&
                   (IsClassLike(node.Parent.Parent) != null);
        }

        /// <nodoc/>
        public static IDeclaration GetDeclarationOfKind(ISymbol symbol, SyntaxKind kind)
        {
            foreach (var declaration in symbol.DeclarationList)
            {
                if (declaration.Kind == kind)
                {
                    return declaration;
                }
            }

            return null;
        }

        private static void AggregateChildData(INode node)
        {
            if ((node.ParserContextFlags & ParserContextFlags.HasAggregatedChildData) == ParserContextFlags.None)
            {
                // A node is considered to contain a parse error if:
                //  a) the parser explicitly marked that it had an error
                //  b) any of its children reported that it had an error.
                var thisNodeOrAnySubNodesHasError =
                    ((node.ParserContextFlags & ParserContextFlags.ThisNodeHasError) != ParserContextFlags.None)
                    || NodeWalker.ForEachChildRecursively<Bool>(node, n => ContainsParseError(n)) != null;

                // If so, mark ourselves accordingly.
                if (thisNodeOrAnySubNodesHasError)
                {
                    node.ParserContextFlags = node.ParserContextFlags | ParserContextFlags.ThisNodeOrAnySubNodesHasError;
                }

                // Also mark that we've propogated the child information to this node.  This way we can
                // always consult the bit directly on this node without needing to check its children
                // again.
                node.ParserContextFlags = node.ParserContextFlags | ParserContextFlags.HasAggregatedChildData;
            }
        }

        private static INode WalkUpBindingElementsAndPatterns(INode node)
        {
            while (node != null && (node.Kind == SyntaxKind.BindingElement || IsBindingPattern(node) != null))
            {
                node = node.Parent;
            }

            return node;
        }

        private static IJsDocTag GetJsDocTag(INode node, SyntaxKind kind)
        {
            throw PlaceHolder.NotImplemented();

            // if (node?.JsDocComment != null)
            // {
            //    foreach (var tag in (node.JsDocComment.ValueOrDefault?.Tags ?? Enumerable.Empty<IJsDocTag>()))
            //    {
            //        if (tag.Kind == kind)
            //        {
            //            return tag;
            //        }
            //    }
            // }
            // return null;
        }

        private static bool IsSupportedExpressionWithTypeArgumentsRest(IExpression node)
        {
            if (node.Kind == SyntaxKind.Identifier)
            {
                return true;
            }

            if (IsPropertyAccessExpression(node) != null)
            {
                var node2 = IsPropertyAccessExpression(node);
                return IsSupportedExpressionWithTypeArgumentsRest(node2.Expression);
            }

            return false;
        }

        private static IEnumerable<string> GetFlagsAsString(NodeFlags flags)
        {
            // There are some flags that don't have a representation in code:
            // - multiline
            // - synthetic
            // - declarationFile
            // - exportContext
            // - hasImplicitReturn
            // - hasExplicitReturn
            // - containsThis
            // - ScriptPublic (it is also part of the node decorators, so we don't need the flag
            if ((flags & NodeFlags.Export) == NodeFlags.Export)
            {
                yield return "export";
            }

            if ((flags & NodeFlags.Ambient) == NodeFlags.Ambient)
            {
                yield return "declare";
            }

            if ((flags & NodeFlags.Public) == NodeFlags.Public)
            {
                yield return "public";
            }

            if ((flags & NodeFlags.Private) == NodeFlags.Private)
            {
                yield return "private";
            }

            if ((flags & NodeFlags.Protected) == NodeFlags.Protected)
            {
                yield return "protected";
            }

            if ((flags & NodeFlags.Static) == NodeFlags.Static)
            {
                yield return "static";
            }

            if ((flags & NodeFlags.Abstract) == NodeFlags.Abstract)
            {
                yield return "abstract";
            }

            if ((flags & NodeFlags.Async) == NodeFlags.Async)
            {
                yield return "async";
            }

            if ((flags & NodeFlags.Default) == NodeFlags.Default)
            {
                yield return "default";
            }

            if ((flags & NodeFlags.Let) == NodeFlags.Let)
            {
                yield return "let";
            }

            if ((flags & NodeFlags.Const) == NodeFlags.Const)
            {
                yield return "const";
            }

            if ((flags & NodeFlags.OctalLiteral) == NodeFlags.OctalLiteral)
            {
                yield return "octalLiteral";
            }

            if ((flags & NodeFlags.Namespace) == NodeFlags.Namespace)
            {
                yield return "namespace";
            }
        }
    }
}
