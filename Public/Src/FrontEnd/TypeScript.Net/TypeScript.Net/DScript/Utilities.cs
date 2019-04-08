// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Constants;
using JetBrains.Annotations;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Scanning;
using TypeScript.Net.Types;
using static BuildXL.Utilities.Collections.HashSetExtensions;
using static TypeScript.Net.Types.NodeUtilities;

namespace TypeScript.Net.DScript
{
    /// <summary>
    /// Set of IDE-specific utilities for <see cref="INode"/>.
    /// </summary>
    public static class Utilities
    {
        /// <summary>
        /// Returns true if a given node is a literal for <see cref="IImportDeclaration"/>,
        /// <see cref="IExportDeclaration"/> or DScript-specific `importFrom` function.
        /// </summary>
        public static bool IsLiteralFromImportOrExportDeclaration(this INode node)
        {
            Contract.Requires(node != null);

            if (node.Kind != SyntaxKind.StringLiteral)
            {
                return false;
            }

            return node.Parent.IsImportFrom() || IsImportOrExportDeclaration();

            bool IsImportOrExportDeclaration()
            {
                return node.Parent?.Kind == SyntaxKind.ImportDeclaration || node.Parent?.Kind == SyntaxKind.ExportDeclaration;
            }
        }

        /// <nodoc />
        public static bool IsJumpStatementTarget(INode node)
        {
            return
                node.Kind == SyntaxKind.Identifier &&
                (node.Parent?.Kind == SyntaxKind.BreakStatement || node.Parent?.Kind == SyntaxKind.ContinueStatement) &&
                node.Parent.Cast<IBreakOrContinueStatement>().Label?.ResolveUnionType() == node.ResolveUnionType();
        }

        /// <nodoc />
        public static bool IsLabelOfLabeledStatement(INode node)
        {
            return
                node.Kind == SyntaxKind.Identifier &&
                node.Parent?.Kind == SyntaxKind.LabeledStatement &&
                node.Parent.Cast<ILabeledStatement>().Label?.ResolveUnionType() == node.ResolveUnionType();
        }

        /// <nodoc />
        public static IIdentifier GetTargetLabel(INode referenceNode, string labelName)
        {
            while (referenceNode != null)
            {
                if (referenceNode.Kind == SyntaxKind.LabeledStatement &&
                    referenceNode.Cast<ILabeledStatement>().Label.Text.Equals(labelName, StringComparison.Ordinal))
                {
                    return referenceNode.Cast<ILabeledStatement>().Label;
                }

                referenceNode = referenceNode.Parent;
            }

            return null;
        }

        /// <nodoc />
        public static bool IsAmbientModule(INode node)
        {
            return
                node?.Kind == SyntaxKind.ModuleDeclaration &&
                node.Cast<IModuleDeclaration>().Name?.Kind == SyntaxKind.StringLiteral; /* TODO: saqadri - verify correctness! || IsGlobalScopeAugmentation(node.Cast<IModuleDeclaration>());*/
        }

        /// <nodoc />
        public static IDeclaration GetContainerNode(INode node)
        {
            while (true)
            {
                node = node?.Parent;
                if (node == null)
                {
                    return null;
                }

                switch (node.Kind)
                {
                    case SyntaxKind.SourceFile:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.MethodSignature:
                    case SyntaxKind.FunctionDeclaration:
                    case SyntaxKind.FunctionExpression:
                    case SyntaxKind.GetAccessor:
                    case SyntaxKind.SetAccessor:
                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.InterfaceDeclaration:
                    case SyntaxKind.EnumDeclaration:
                    case SyntaxKind.ModuleDeclaration:
                        return node.Cast<IDeclaration>();
                }
            }
        }

        /// <nodoc />
        public static bool IsInString(ISourceFile sourceFile, int position)
        {
            INode token;

            return DScriptNodeUtilities.TryGetNodeAtPosition(sourceFile, position, out token) &&
                    (token?.Kind == SyntaxKind.StringLiteral || token?.Kind == SyntaxKind.StringLiteralType) &&
                    position > GetTokenPosOfNode(token, token.GetSourceFile());
        }

        /// <nodoc />
        public static bool IsInCommentHelper(ISourceFile sourceFile, int position, Func<ICommentRange, bool> predicate = null)
        {
            if (!DScriptNodeUtilities.TryGetNodeAtPosition(sourceFile, position, out var token) || position > GetTokenPosOfNode(token, sourceFile))
            {
                return false;
            }

            var commentRanges = Scanner.GetLeadingCommentRanges(sourceFile.Text, token.Pos);

            // The end marker of a single-line comment does not include the newline character.
            // In the following case, we are inside a comment (^ denotes the cursor position):
            //
            //    // asdf   ^\n
            //
            // But for multi-line comments, we don't want to be inside the comment in the following case:
            //
            //    /* asdf */^
            //
            // Internally, we represent the end of the comment at the newline and closing '/', respectively.
            foreach (var c in commentRanges)
            {
                if (c.Pos < position &&
                    (c.Kind == SyntaxKind.SingleLineCommentTrivia ? position <= c.End : position < c.End) &&
                    (predicate == null || predicate(c)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <nodoc />
        public static bool IsCallExpressionTarget(INode node)
        {
            if (IsRightSideOfQualifiedNameOrPropertyAccess(node))
            {
                node = node.Parent;
            }

            return
                node?.Parent?.Kind == SyntaxKind.CallExpression &&
                node.Parent.Cast<ICallExpression>().Expression.ResolveUnionType() == node.ResolveUnionType();
        }

        /// <nodoc />
        public static bool IsNewExpressionTarget(INode node)
        {
            if (IsRightSideOfQualifiedNameOrPropertyAccess(node))
            {
                node = node.Parent;
            }

            return
                node?.Parent?.Kind == SyntaxKind.NewExpression &&
                node.Parent.Cast<ICallExpression>().Expression.ResolveUnionType() == node.ResolveUnionType();
        }

        /// <nodoc />
        public static bool IsNameOfFunctionDeclaration(INode node)
        {
            return
                node.Kind == SyntaxKind.Identifier &&
                IsFunctionLike(node.Parent)?.Name.ResolveUnionType() == node.ResolveUnionType();
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters")]
        public static string GetSymbolKind(ISymbol symbol, INode location)
        {
            /*var flags = symbol.Flags;

            if ((flags & SymbolFlags.Class) != SymbolFlags.None)
            {
                return GetDeclarationOfKind(symbol, SyntaxKind.ClassExpression) != null ?
                    ScriptElementKind.localClassElement :
                    ScriptElementKind.classElement;
                if (flags & SymbolFlags.Enum) return ScriptElementKind.enumElement;
                if (flags & SymbolFlags.TypeAlias) return ScriptElementKind.typeElement;
                if (flags & SymbolFlags.Interface) return ScriptElementKind.interfaceElement;
                if (flags & SymbolFlags.TypeParameter) return ScriptElementKind.typeParameterElement;

                const result = getSymbolKindOfConstructorPropertyMethodAccessorFunctionOrVar(symbol, flags, location);
                if (result === ScriptElementKind.unknown)
                {
                    if (flags & SymbolFlags.TypeParameter) return ScriptElementKind.typeParameterElement;
                    if (flags & SymbolFlags.EnumMember) return ScriptElementKind.variableElement;
                    if (flags & SymbolFlags.Alias) return ScriptElementKind.alias;
                    if (flags & SymbolFlags.Module) return ScriptElementKind.moduleElement;
                }

                return result;
            }*/

            // TODO: saqadri - fix up!
            return "hackathon";
        }

        /// <nodoc />
        public static bool IsImportSpecifierSymbol(ISymbol symbol)
        {
            return
                (symbol.Flags & SymbolFlags.Alias) != SymbolFlags.None &&
                GetDeclarationOfKind(symbol, SyntaxKind.ImportSpecifier) != null;
        }

        /// <nodoc />
        public static bool IsNameOfModuleDeclaration(INode node)
        {
            return
                node.Parent?.Kind == SyntaxKind.ModuleDeclaration &&
                node.Parent.Cast<IModuleDeclaration>().Name.ResolveUnionType() == node.ResolveUnionType();
        }

        /// <nodoc />
        public static bool IsLiteralNameOfPropertyDeclarationOrIndexAccess(INode node)
        {
            if (node.Kind == SyntaxKind.StringLiteral || node.Kind == SyntaxKind.NumericLiteral)
            {
                switch (node.Parent.Kind)
                {
                    case SyntaxKind.PropertyDeclaration:
                    case SyntaxKind.PropertySignature:
                    case SyntaxKind.PropertyAssignment:
                    case SyntaxKind.EnumMember:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.MethodSignature:
                    case SyntaxKind.GetAccessor:
                    case SyntaxKind.SetAccessor:
                    case SyntaxKind.ModuleDeclaration:
                        return node.Parent?.Cast<IDeclaration>().Name.ResolveUnionType() == node.ResolveUnionType();

                    case SyntaxKind.ElementAccessExpression:
                        return node.Parent?.Cast<IElementAccessExpression>().ArgumentExpression.ResolveUnionType() == node.ResolveUnionType();
                }
            }

            return false;
        }

        /// <nodoc />
        public static bool IsNameOfPropertyAssignment(INode node)
        {
            return
                (node.Kind == SyntaxKind.Identifier || node.Kind == SyntaxKind.StringLiteral || node.Kind == SyntaxKind.NumericLiteral) &&
                (node.Parent?.Kind == SyntaxKind.PropertyAssignment || node.Parent?.Kind == SyntaxKind.ShorthandPropertyAssignment) &&
                node.Parent.Cast<IDeclaration>().Name.ResolveUnionType() == node.ResolveUnionType();
        }

        /// <nodoc />
        public static IReadOnlyList<ISymbol> GetPropertySymbolsFromContextualType(INode node, ITypeChecker checker)
        {
            if (IsNameOfPropertyAssignment(node))
            {
                var objectLiteral = node.Parent?.Parent.As<IObjectLiteralExpression>();
                var contextualType = objectLiteral != null ? checker.GetContextualType(objectLiteral) : null;
                var name = node.GetText(); // TODO: saqadri - verify correctness (const name = (<Identifier>node).text;

                if (contextualType != null)
                {
                    if ((contextualType.Flags & TypeFlags.Union) != TypeFlags.None)
                    {
                        // TODO: saqadri - port
                        // This is a union type, first see if the property we are looking for is a union property (i.e. exists in all types)
                        // if not, search the constituent types for the property
                        var unionProperty = checker.GetPropertyOfType(contextualType, name);
                        if (unionProperty != null)
                        {
                            return new List<ISymbol> { unionProperty };
                        }

                        var result = new List<ISymbol>();
                        foreach (var t in contextualType.Cast<IUnionType>().Types ?? Enumerable.Empty<IType>())
                        {
                            var symbol = checker.GetPropertyOfType(t, name);
                            if (symbol != null)
                            {
                                result.Add(symbol);
                            }
                        }

                        return result;
                    }

                    var contextualSymbol = checker.GetPropertyOfType(contextualType, name);
                    if (contextualSymbol != null)
                    {
                        return new List<ISymbol> { contextualSymbol };
                    }
                }
            }

            return BuildXL.Utilities.Collections.CollectionUtilities.EmptyArray<ISymbol>();
        }

        /// <nodoc />
        public static bool IsNameOfExternalModuleImportOrDeclaration(INode node)
        {
            if (node.Kind == SyntaxKind.StringLiteral)
            {
                return
                    IsNameOfModuleDeclaration(node) ||
                    (IsExternalModuleImportEqualsDeclaration(node.Parent?.Parent) &&
                    GetExternalModuleImportEqualsDeclarationExpression(node.Parent?.Parent)?.ResolveUnionType() == node.ResolveUnionType());
            }

            return false;
        }

        /// <nodoc />
        public static bool IsLabelName(INode node)
        {
            return IsLabelOfLabeledStatement(node) || IsJumpStatementTarget(node);
        }

        /// <nodoc />
        public static string GetDeclaredName(ITypeChecker typeChecker, ISymbol symbol, INode location)
        {
            // If this is an export or import specifier it could have been renamed using the 'as' syntax.
            // If so we want to search for whatever is under the cursor.
            if (IsImportOrExportSpecifierName(location))
            {
                return location.GetText();
            }

            // Try to get the local symbol if we're dealing with an 'export default'
            // since that symbol has the "true" name.
            var localExportDefaultSymbol = GetLocalSymbolForExportDefault(symbol);

            return typeChecker.SymbolToString(localExportDefaultSymbol ?? symbol);
        }

        /// <nodoc />
        public static bool IsImportOrExportSpecifierName(INode location)
        {
            return
                location?.Parent != null &&
                (location.Parent.Kind == SyntaxKind.ImportSpecifier || location.Parent.Kind == SyntaxKind.ExportSpecifier) &&
                location.Parent.Cast<IImportOrExportSpecifier>().PropertyName?.ResolveUnionType() == location.ResolveUnionType();
        }

        /// <nodoc />
        public static bool IsInRightSideOfImport(INode node)
        {
            while (node?.Parent?.Kind == SyntaxKind.QualifiedName)
            {
                node = node.Parent;
            }

            return
                IsInternalModuleImportEqualsDeclaration(node?.Parent) &&
                node.Parent.Cast<IImportEqualsDeclaration>().ModuleReference?.ResolveUnionType() == node.ResolveUnionType();
        }

        /// <nodoc />
        public static bool IsTypeReference(INode node)
        {
            if (IsRightSideOfQualifiedNameOrPropertyAccess(node))
            {
                node = node.Parent;
            }

            return
                (node.Parent?.Kind == SyntaxKind.TypeReference) ||
                (node.Parent?.Kind == SyntaxKind.ExpressionWithTypeArguments && !IsExpressionWithTypeArgumentsInClassExtendsClause(node?.Parent)) ||
                (node.Kind == SyntaxKind.ThisKeyword && !IsExpression(node)) ||
                node.Kind == SyntaxKind.ThisType;
        }

        /// <nodoc />
        public static bool IsNamespaceReference(INode node)
        {
            return IsQualifiedNameNamespaceReference(node) || IsPropertyAccessNamespaceReference(node);
        }

        /// <nodoc />
        public static bool IsPropertyAccessNamespaceReference(INode node)
        {
            var root = node;
            var isLastClause = true;
            if (root.Parent?.Kind == SyntaxKind.PropertyAccessExpression)
            {
                while (root?.Parent?.Kind == SyntaxKind.PropertyAccessExpression)
                {
                    root = root.Parent;
                }

                isLastClause = root.Cast<IPropertyAccessExpression>().Name.ResolveUnionType() == node.ResolveUnionType();
            }

            if (!isLastClause && root?.Parent?.Kind == SyntaxKind.ExpressionWithTypeArguments && root?.Parent?.Parent?.Kind == SyntaxKind.HeritageClause)
            {
                var decl = root.Parent.Parent.Parent;

                return
                    (decl.Kind == SyntaxKind.ClassDeclaration && root.Parent.Parent.Cast<IHeritageClause>().Token == SyntaxKind.ImplementsKeyword) ||
                    (decl.Kind == SyntaxKind.InterfaceDeclaration && root.Parent.Parent.Cast<IHeritageClause>().Token == SyntaxKind.ExtendsKeyword);
            }

            return false;
        }

        /// <nodoc />
        public static bool IsQualifiedNameNamespaceReference(INode node)
        {
            var root = node;
            var isLastClause = true;
            if (root.Parent?.Kind == SyntaxKind.QualifiedName)
            {
                while (root?.Parent?.Kind == SyntaxKind.QualifiedName)
                {
                    root = root.Parent;
                }

                isLastClause = root.Cast<IQualifiedName>().Right.ResolveUnionType() == node.ResolveUnionType();
            }

            return root.Parent?.Kind == SyntaxKind.TypeReference && !isLastClause;
        }

        // TODO: saqadri - duplicated with TypeScript.Net.Utils (Utilities.cs)
        /// <nodoc />
        public static ISymbol GetLocalSymbolForExportDefault(ISymbol symbol)
        {
            if (symbol?.ValueDeclaration != null && (symbol.ValueDeclaration.Flags & NodeFlags.Default) != NodeFlags.None)
            {
                return symbol.ValueDeclaration.LocalSymbol;
            }

            return null;
        }

        /// <nodoc />
        public static bool IsArgumentOfElementAccessExpression(INode node)
        {
            return
                node?.Parent?.Kind == SyntaxKind.ElementAccessExpression &&
                node.Parent.Cast<IElementAccessExpression>().ArgumentExpression?.ResolveUnionType() == node.ResolveUnionType();
        }

        /// <summary>
        /// Returns whether or not a node one of the ===, !==,==, or !=.
        /// </summary>
        public static bool IsEqualityOperator(INode node)
        {
            var kind = node.Kind;

            // BuildXL does not support the != or == operator as they allow type coercion.
            // However, it the language server completion (IDE) is invoked on these types of tokens
            // (because they can be typed by the user).
            return kind == SyntaxKind.EqualsEqualsToken || 
                kind == SyntaxKind.ExclamationEqualsToken ||
                kind == SyntaxKind.EqualsEqualsEqualsToken || 
                kind == SyntaxKind.ExclamationEqualsEqualsToken;
        }

        private static IEnumerable<IType> GetAllTypesIfUnion(IType type, ITypeChecker typeChecker, HashSet<IType> types)
        {
            if ((type.Flags & TypeFlags.Union) != TypeFlags.None)
            {
                foreach (var unionType in type.Cast<IUnionType>().Types)
                {
                    types.AddRange(GetAllTypesIfUnion(unionType, typeChecker, types));
                }
            }
            else
            {
                types.Add(type);
            }

            return types;
        }

        /// <summary>
        /// Returns an enumerable of all types present in a union if the type specified is a union type.
        /// </summary>
        public static IEnumerable<IType> GetAllTypesIfUnion(IType type, ITypeChecker typeChecker)
        {
            var types = new HashSet<IType>();
            return GetAllTypesIfUnion(type, typeChecker, types);
        }

        /// <summary>
        /// Give a case or default clause, locates the switch statement associated with it.
        /// </summary>
        public static ISwitchStatement GetSwitchStatement(INode node)
        {
            Contract.Requires(node.Kind == SyntaxKind.CaseClause || node.Kind == SyntaxKind.DefaultClause);

            // Walk up the parent chain until we find the switch statement as the expression of the
            // switch statement is the type we want to complete.
            // The type-script version of this does a "node.parent.parent.parent.parent" type of expression
            // which seems fragile if the AST parsing were changed in any way. So we will just walk the chain.
            var switchStatement = node.Parent;
            while (switchStatement != null && switchStatement.Kind != SyntaxKind.SwitchStatement)
            {
                switchStatement = switchStatement.Parent;
            }

            Contract.Assert(switchStatement != null);

            return switchStatement.Cast<ISwitchStatement>();
        }

        /// <summary>
        /// Returns whether a node is an "entity" which is either an identifier
        /// or a qualified name.
        /// </summary>
        /// <remarks>
        /// Ported from typescript version.
        /// </remarks>
        public static bool IsEntityNode(INode node)
        {
            return node.Kind == SyntaxKind.QualifiedName || node.Kind == SyntaxKind.Identifier;
        }

        /// <summary>
        /// Finds the actual symbol for an aliased symbol.
        /// </summary>
        /// <remarks>
        /// If a symbol is a type alias (for example Foo in this statement):
        /// import {Foo} from "Module.Foo";
        /// this function will return the symbol from inside the "Module.Foo" module.
        /// Ported from TypeScript implementation.
        /// </remarks>
        public static ISymbol SkipAlias(ISymbol symbol, ITypeChecker typeChecker)
        {
            return (symbol.Flags & SymbolFlags.Alias) != SymbolFlags.None ? typeChecker.GetAliasedSymbol(symbol) : symbol;
        }

        /// <summary>
        /// Returns whether a node is part of a type node by examining the context it is being used in.
        /// </summary>
        /// <remarks>
        /// Ported from TypeScript version.
        /// 
        /// There are many examples of this that will true.
        /// 
        /// Given this case
        /// <code>
        /// export interface Executable extends Shared.{completion happens here}
        /// </code>
        /// 
        /// The node passed in will be "Shared." which is a property access expression.
        /// That property access experssion happens to used in an "extends" clause which
        /// results in this returning true.
        /// </remarks>
        public static bool IsPartOfTypeNode(INode node)
        {
            if (node.Kind >= SyntaxKind.FirstTypeNode && node.Kind <= SyntaxKind.LastTypeNode)
            {
                return true;
            }

            // Identifiers and qualified names may be type nodes, depending on their context. Climb
            // above them to find the lowest container

            if (node.Kind == SyntaxKind.Identifier)
            {
                // If the identifier is the RHS of a qualified name, then it's a type iff its parent is.
                if (node.Parent.Kind == SyntaxKind.QualifiedName && node.Parent.Cast<IQualifiedName>().Right == node)
                {
                    node = node.Parent;
                }
                else if (node.Parent.Kind == SyntaxKind.PropertyAccessExpression && node.Parent.Cast<IPropertyAccessExpression>().Name == node)
                {
                    node = node.Parent;
                }

                // At this point, node is either a qualified name or an identifier
                Contract.Assert(
                    node.Kind == SyntaxKind.Identifier ||
                    node.Kind == SyntaxKind.QualifiedName ||
                    node.Kind == SyntaxKind.PropertyAccessExpression,
                    "'node' was expected to be a qualified name, identifier or property access in 'isPartOfTypeNode'.");
            }

            switch (node.Kind)
            {
                case SyntaxKind.AnyKeyword:
                case SyntaxKind.NumberKeyword:
                case SyntaxKind.StringKeyword:
                case SyntaxKind.BooleanKeyword:
                case SyntaxKind.SymbolKeyword:
                // These syntax kinds do not appear to be present in DScript version of TypeScript
                // case SyntaxKind.UndefinedKeyword:
                // case SyntaxKind.NeverKeyword:
                    return true;

                case SyntaxKind.VoidKeyword:
                    return node.Parent.Kind != SyntaxKind.VoidExpression;

                case SyntaxKind.ExpressionWithTypeArguments:
                    return !IsExpressionWithTypeArgumentsInClassExtendsClause(node);

                // falls through
                case SyntaxKind.QualifiedName:
                case SyntaxKind.PropertyAccessExpression:
                case SyntaxKind.ThisKeyword:
                    var parent = node.Parent;
                    if (parent.Kind == SyntaxKind.TypeQuery)
                    {
                        return false;
                    }

                    // Do not recursively call isPartOfTypeNode on the parent. In the example:
                    //
                    //     let a: A.B.C;
                    //
                    // Calling isPartOfTypeNode would consider the qualified name A.B a type node.
                    // Only C and A.B.C are type nodes.
                    if (parent.Kind >= SyntaxKind.FirstTypeNode && parent.Kind <= SyntaxKind.LastTypeNode)
                    {
                        return true;
                    }

                    switch (parent.Kind)
                    {
                        case SyntaxKind.ExpressionWithTypeArguments:
                            return !IsExpressionWithTypeArgumentsInClassExtendsClause(parent);
                        case SyntaxKind.TypeParameter:
                            return node == parent.Cast<ITypeParameterDeclaration>().Constraint;
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
                            var typeNode = node.As<ITypeNode>();
                            return typeNode != null && parent.Cast<ICallExpression>().TypeArguments != null && parent.Cast<ICallExpression>().TypeArguments.IndexOf(typeNode) >= 0;
                        case SyntaxKind.TaggedTemplateExpression:
                            // TODO: TaggedTemplateExpressions may eventually support type arguments.
                            return false;
                    }

                    return false;
            }

            return false;
        }

        /// <summary>
        /// Returns true if a given <paramref name="node"/> is a <see cref="IStringLiteral"/> or <see cref="IStringLiteralType"/>.
        /// </summary>
        public static bool IsStringLikeLiteral(this INode node)
        {
            return node.Kind == SyntaxKind.StringLiteral || node.Kind == SyntaxKind.StringLiteralType;
        }

        /// <summary>
        /// Returns true if a given <paramref name="node"/> is a <see cref="IPathLikeLiteralExpression"/>.
        /// </summary>
        public static bool IsPathLikeLiteral(this INode node)
        {
            return node.As<IPathLikeLiteralExpression>() != null;
        }

        /// <summary>
        /// Finds a word-like node at <paramref name="position"/>.
        /// </summary>
        [CanBeNull]
        public static INode GetTouchingWord(ISourceFile sourceFile, int position, System.Threading.CancellationToken token)
        {
            NodeExtensions.TryGetNodeAtPosition(
                sourceFile,
                position,
                isNodeAcceptable: (n) => n.Kind.IsWord() || n.IsStringLikeLiteral(),
                token: token,
                currentNode: out var node);

            return node;
        }

        /// <summary>
        /// Finds a word-like node at <paramref name="position"/>.
        /// </summary>
        [CanBeNull]
        public static INode GetTouchingWord(ISourceFile sourceFile, int position) =>
            GetTouchingWord(sourceFile, position, System.Threading.CancellationToken.None);

        /// <summary>
        /// Finds all word-like nodes at the given <paramref name="positions"/>.
        /// </summary>
        [CanBeNull]
        public static INode[] GetTouchingWords(ISourceFile sourceFile, IReadOnlyList<int> positions, System.Threading.CancellationToken token)
        {
            return NodeExtensions.TryGetNodesAtPositions(
                sourceFile,
                positions,
                isNodeAcceptable: (n) => n.Kind.IsWord() || n.IsStringLikeLiteral(),
                token: token);
        }

        /// <summary>
        /// Finds a path-like nodes at <paramref name="positions"/>.
        /// </summary>
        [CanBeNull]
        public static INode[] GetTouchingPaths(ISourceFile sourceFile, IReadOnlyList<int> positions, System.Threading.CancellationToken token)
        {
            return NodeExtensions.TryGetNodesAtPositions(
                sourceFile,
                positions,
                isNodeAcceptable: (n) => n.Kind.IsWord() || n.IsPathLikeLiteral(),
                token: token);
        }

        /// <summary>
        /// Returns whether the node passed in represents the "merge", "override" or "overrideKey" template expressions.
        /// </summary>
        public static bool IsMergeOrOverrideCallExpression([CanBeNull] INode node)
        {
            string calledFunction = string.Empty;
            if (node?.Kind == SyntaxKind.CallExpression)
            {
                var callExpression = node.Cast<ICallExpression>();
                if (callExpression.Expression?.Kind == SyntaxKind.PropertyAccessExpression)
                {
                    var propertyAccessExpression = callExpression.Expression.Cast<IPropertyAccessExpression>();
                    calledFunction = propertyAccessExpression.Name?.Text;
                }
                else if (callExpression.Expression?.Kind == SyntaxKind.Identifier)
                {
                    calledFunction = callExpression.Expression.Cast<Identifier>().Text;
                }

                if ((calledFunction.Equals(Names.OverrideFunction, StringComparison.Ordinal) ||
                    calledFunction.Equals(Names.OverrideKeyFunction, StringComparison.Ordinal) ||
                    calledFunction.Equals(Names.MergeFunction, StringComparison.Ordinal)) && callExpression.TypeArguments?.Length == 1)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
