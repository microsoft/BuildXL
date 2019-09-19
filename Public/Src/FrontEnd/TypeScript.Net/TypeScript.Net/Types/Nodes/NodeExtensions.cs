// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Scanning;
using static BuildXL.Utilities.FormattableStringEx;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Set of extension methods for <see cref="INode"/> interface.
    /// </summary>
    public static class NodeExtensions
    {
        /// <summary>
        /// Clears all non-DScript specific flags.
        /// </summary>
        public static void ClearNonDScriptSpecificFlags(this INode node)
        {
            node.ParserContextFlags &= ParserContextFlags.DScriptSpecificFlags;
        }

        /// <summary>
        /// Sets the flag that indicates that a given <paramref name="node"/> was injected.
        /// </summary>
        public static void MarkAsDScriptInjected(this INode node)
        {
            node.ParserContextFlags |= ParserContextFlags.DScriptInjectedNode;
        }

        /// <summary>
        /// Returns whether the <paramref name="node"/> is injected.
        /// In other words: The node is added after the parsing is over.
        /// </summary>
        public static bool IsInjectedForDScript(this INode node)
        {
            return (node.ParserContextFlags & ParserContextFlags.DScriptInjectedNode) != ParserContextFlags.None;
        }

        /// <summary>
        /// Returns true if a given <paramref name="node"/> is an await statement. The method is always return <code>false</code>.
        /// </summary>
        /// <remarks>DScript does not support async/await and this method is left just as a marker to keep the old logic in place.</remarks>
        public static bool IsAwait(this INode node) => false;

        /// <summary>
        /// Returns true if a given <paramref name="node"/> is a generator. The method is always return <code>false</code>.
        /// </summary>
        /// <remarks>DScript does not support generators and this method is left just as a marker to keep the old logic in place.</remarks>
        public static bool IsYield(this INode node) => false;

        /// <summary>
        /// Returns true if a given <paramref name="node"/> is defined in a JavaScript file. The method is always return <code>false</code>.
        /// </summary>
        /// <remarks>DScript does not support generators and this method is left just as a marker to keep the old logic in place.</remarks>
        public static bool IsJavaScriptFile(this INode node) => false;

        /// <summary>
        /// Tries to find the node with the smallest width in the <paramref name="sourceFile"/> that contains the input <paramref name="position"/>.
        /// If the position points to a comment, the resulting node is null.
        /// </summary>
        public static bool TryGetNodeAtPosition(ISourceFile sourceFile, int position, out INode nodeAtPosition)
        {
            return TryGetNodeAtPosition(sourceFile, position, (n) => true, out nodeAtPosition);
        }

        /// <summary>
        /// Tries to find the node with the smallest width in the <paramref name="sourceFile"/>
        /// that contains the input <paramref name="position"/>, and is deemed acceptable by
        /// <paramref name="isNodeAcceptable"/>.
        /// If the position points to a comment, the resulting node is null.
        /// </summary>
        public static bool TryGetNodeAtPosition(ISourceFile sourceFile, int position, Func<INode, bool> isNodeAcceptable, out INode currentNode)
        {
            return TryGetNodeAtPosition(sourceFile, position, isNodeAcceptable, System.Threading.CancellationToken.None, out currentNode);
        }

        /// <summary>
        /// Tries to find the node with the smallest width in the <paramref name="sourceFile"/>
        /// that contains the input <paramref name="position"/>, and is deemed acceptable by
        /// <paramref name="isNodeAcceptable"/>.
        /// If the position points to a comment, the resulting node is null.
        /// </summary>
        public static bool TryGetNodeAtPosition(ISourceFile sourceFile, int position, Func<INode, bool> isNodeAcceptable, System.Threading.CancellationToken token, out INode currentNode)
        {
            Contract.Requires(sourceFile != null);
            Contract.Requires(position >= 0);

            INode result = null;
            NodeWalker.TraverseDepthFirstAndSelfInOrder(
                sourceFile,
                node =>
                {
                    var startPosition = node.GetNodeStartPositionWithoutTrivia(sourceFile);
                    if (result != null && startPosition > position)
                    {
                        // We can stop the traversal already because the next node starts after the expected position.
                        // And we know that the traversal is depth first from left to right.
                        return false;
                    }

                    if (startPosition <= position && position <= node.End)
                    {
                        if (result == null || node.GetNodeWidth(sourceFile) <= result.GetNodeWidth(sourceFile))
                        {
                            if (isNodeAcceptable(node))
                            {
                                result = node;
                            }
                        }
                    }

                    return true;
                },
                token);

            currentNode = result;
            return result != null;
        }

        /// <summary>
        /// Finds all the nodes at a given <paramref name="positions"/>.
        /// </summary>
        public static INode[] TryGetNodesAtPositions(ISourceFile sourceFile, IReadOnlyList<int> positions, Func<INode, bool> isNodeAcceptable, System.Threading.CancellationToken token)
        {
            Contract.Requires(sourceFile != null);

            INode[] nodeByPosition = new INode[positions.Count];
            foreach (var node in NodeWalker.TraverseBreadthFirstAndSelf(sourceFile))
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                int startPosition = node.GetNodeStartPositionWithoutTrivia(sourceFile);
                int endPosition = node.End;

                for (int i = 0; i < positions.Count; i++)
                {
                    int position = positions[i];
                    if (startPosition <= position && position <= endPosition)
                    {
                        if (nodeByPosition[i] == null || node.GetNodeWidth(sourceFile) <= nodeByPosition[i].GetNodeWidth(sourceFile))
                        {
                            if (isNodeAcceptable(node))
                            {
                                nodeByPosition[i] = node;
                            }
                        }
                    }
                }
            }

            return nodeByPosition;
        }

        /// <summary>
        /// Converts line and column to an absolute position inside the file.
        /// Both: <paramref name="line"/> and <paramref name="offset"/> starts with 1.
        /// </summary>
        public static bool TryConvertLineOffsetToPosition(ISourceFile sourceFile, int line, int offset, out int position)
        {
            position = -1;

            var lineMap = sourceFile.LineMap;
            if (line > lineMap.Map.Length)
            {
                return false;
            }

            position = lineMap.Map[line - 1] + offset - 1;
            return true;
        }

        /// <summary>
        /// Returns the width of a node.
        /// </summary>
        public static int GetNodeWidth(this INode node, ISourceFile file = null)
        {
            return node.End - node.GetNodeStartPositionWithoutTrivia(file);
        }

        /// <summary>
        /// Returns the size of the leading trivia for the node.
        /// </summary>
        /// <remarks>
        /// <see cref="ITextRange.Pos"/> points not directly to the beginning of the node but to the beginning of the trailing trivia for the node.
        /// In some cases this is fine, but for other cases, like error reporting, we need an actual start position of the node.
        /// Initial implementation rescanned the source text from the <see cref="ITextRange.Pos"/> to the next non-trivia token.
        /// But this approach requires keeping the entire source text in memory for too long (up to evaluation phase).
        /// Instead of that each node now knows about the length of the trailing trivia.
        /// Unfortunately, we can't just store the length in the node.
        /// Instead of that we keep it directly in the node if the length is less then 255 character and if the length is greater than that we store
        /// this information in the separate table.
        /// This is memory optimization crucial for keeping memory footprint low.
        /// </remarks>
        public static int GetLeadingTriviaLength(this INode node, ISourceFile file = null)
        {
            if (node.LeadingTriviaLength != byte.MaxValue)
            {
                return node.LeadingTriviaLength;
            }

            var sourceFile = (SourceFile)(file ?? node.GetSourceFile());
            Contract.Assert(sourceFile.LeadingTriviasMap != null, "Trivias map should be initialized");
            return sourceFile.LeadingTriviasMap[node.ResolveUnionType()];
        }

        /// <summary>
        /// Returns node position including the offset for the leading trivia.
        /// </summary>
        public static int GetNodeStartPositionWithoutTrivia(this INode node, ISourceFile file = null)
        {
            return node.Pos + node.GetLeadingTriviaLength(file);
        }

        /// <summary>
        /// Sets the size of the leading trivia for the node.
        /// </summary>
        public static void SetLeadingTriviaLength(this INode node, int length, ISourceFile file = null)
        {
            if (length < byte.MaxValue)
            {
                node.LeadingTriviaLength = (byte)length;
            }
            else
            {
                node.LeadingTriviaLength = byte.MaxValue;
                var sourceFile = (SourceFile)(file ?? node.GetSourceFile());
                var triviasMap = sourceFile.LeadingTriviasMap ?? (sourceFile.LeadingTriviasMap = new Dictionary<INode, int>());
                triviasMap[node] = length;
            }
        }

        /// <summary>
        /// Returns true if a given node is a special template expression like p``, d``, f``, a`` etc.
        /// </summary>
        public static bool IsWellKnownTemplateExpression(this ITaggedTemplateExpression node, out string name)
        {
            Contract.Requires(node != null);

            if (node.Tag.Kind == SyntaxKind.Identifier)
            {
                name = node.Tag.Cast<IIdentifier>().Text;
                return Scanner.IsPathLikeInterpolationFactory(name);
            }

            name = null;
            return false;
        }

        /// <summary>
        /// Generic safe-cast method that tries to convert from <paramref name="node"/> to <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>
        /// This method is important because <paramref name="node"/> could be of union type that will prevent
        /// regular conversion from it to target type.
        /// </remarks>
        [DebuggerStepThrough]
        [CanBeNull]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T As<T>([CanBeNull] this INode node) where T : class, INode
        {
            // Currently, As and TryCast are implemented in a different way.
            // As method doesn't try to convert node to a given type if a node is a union type.
            // This is unfortunate and needs to be fixed.
            // This means that node.As<T>() and node.Cast<T>() are not the same in terms of behior:
            // If the first one returns null, the second one still can return an instance.
            // TODO: unify this implementation with TryCast behavior.
            var union = node as IUnionNode;
            if (union != null)
            {
                return union.Node as T;
            }

            return node as T;
        }

        /// <summary>
        /// Generic unsafe-cast method that tries to convert from <paramref name="node"/> to <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>
        /// This method is important because <paramref name="node"/> could be of union type that will prevent
        /// regular conversion from it to target type.
        /// </remarks>
        [DebuggerStepThrough]
        [JetBrains.Annotations.NotNull]
        public static T Cast<T>([JetBrains.Annotations.NotNull]this INode node) where T : class, INode
        {
            // Even that this method is very small, it won't be inlined by the CLR,
            // because of generics: generic method that calls another generic
            // requires some CLR hooks to check type correctness, so such a method
            // is not inlined in a current CLR version (4.6).
            return node.TryCast<T>() ?? ThrowInvalidCastException<T>(node);
        }

        /// <summary>
        /// Returns identifier name of the <paramref name="bindingPattern"/>.
        /// </summary>
        public static string GetName(this IBindingPattern bindingPattern)
        {
            Contract.Requires(bindingPattern != null);
            return bindingPattern.Cast<IIdentifier>().Text;
        }

        /// <summary>
        /// Returns true if <paramref name="node"/> is a const enum declaration.
        /// </summary>
        public static bool IsConstEnumDeclaration(this INode node)
        {
            return node.Kind == SyntaxKind.EnumDeclaration && NodeUtilities.IsConst(node);
        }

        /// <summary>
        /// Returns true if a given node defined on top level.
        /// </summary>
        public static bool IsTopLevelDeclaration(this INode node)
        {
            Contract.Assume(node.Parent != null, "Binding phase should be completed in order to use this API.");

            return node.Parent.Kind == SyntaxKind.SourceFile;
        }

        /// <summary>
        /// Returns a module specifier expression for <see cref="IImportDeclaration"/> or <see cref="IExportDeclaration"/>.
        /// </summary>
        /// <returns>
        /// Returns module specifier (i.e. the right most part of the import/export declaration) or null for 'export {a}' case.
        /// </returns>
        [CanBeNull]
        public static IExpression GetModuleSpecifier([JetBrains.Annotations.NotNull]this INode node)
        {
            var importDeclaration = node.As<IImportDeclaration>();
            if (importDeclaration != null)
            {
                return importDeclaration.ModuleSpecifier;
            }

            var exportDeclaration = node.As<IExportDeclaration>();
            if (exportDeclaration != null)
            {
                return exportDeclaration.ModuleSpecifier;
            }

            throw new InvalidOperationException(I($"Unsupported statement kind '{node.Kind}'. Only 'IImportDeclaration' or 'IExportDeclaration' are supported."));
        }

        /// <summary>
        /// Returns true if a given node defined on top or namespace level.
        /// </summary>
        public static bool IsTopLevelOrNamespaceLevelDeclaration(this INode node)
        {
            Contract.Assume(node.Parent != null || node.Kind == SyntaxKind.SourceFile, "Binding phase should be completed in order to use this API.");

            INode parentNode = node.Parent;
            while (parentNode != null)
            {
                switch (parentNode.Kind)
                {
                    case SyntaxKind.FunctionDeclaration:
                    case SyntaxKind.ArrowFunction:
                    case SyntaxKind.Block:
                    case SyntaxKind.CaseBlock:
                    // This is all safe only for DScript, because DScript prohibits any statements on a top level.
                    case SyntaxKind.SwitchStatement:
                    case SyntaxKind.IfStatement:
                    case SyntaxKind.DoStatement:
                    case SyntaxKind.WhileStatement:
                    case SyntaxKind.ForStatement:
                    case SyntaxKind.ForInStatement:
                        return false;
                    case SyntaxKind.SourceFile:
                    case SyntaxKind.ModuleBlock: // namespace and module are covered by this case.
                        return true;
                }

                // Getting here for compound expressions or compound declarations like variable declarations and others.
                parentNode = parentNode.Parent;
            }

            return true;
        }

        /// <summary>
        /// Returns text representation for tag from <paramref name="expression"/>.
        /// </summary>
        /// <remarks>
        /// Getting text representation of a tag is widely used operation that could appear on the hot path.
        /// To avoid performance and memory problems this function should be used instead of more generic function of getting text
        /// representation, like <see cref="ReformatterHelper.GetFormattedText"/>.
        /// </remarks>
        public static string GetTagText(this ITaggedTemplateExpression expression)
        {
            Contract.Requires(expression != null);

            // There is two common cases for tagged expression in the system:
            // 1) custom factory, like p``, d``, f`` or
            // 2) string interpolation, like ``
            // This implementation is optimized for them.
            if (expression.Tag == null)
            {
                // Second case: this is ``
                return string.Empty;
            }

            var identifier = expression.Tag as IIdentifier;
            if (identifier != null)
            {
                // First case: tag is just a function.
                return identifier.Text;
            }

            // Falling back to generic implementation.
            return expression.Tag.GetFormattedText();
        }

        /// <summary>
        /// Returns the text of a <see cref="ILiteralLikeNode"/> that was constructed as part of a <see cref="ITaggedTemplateExpression"/>.
        /// </summary>
        public static string GetTemplateText(this IPrimaryExpression expression)
        {
            Contract.Requires(expression != null);

            return expression.Cast<ILiteralLikeNode>().Text;
        }

        /// <summary>
        /// Returns alias for import declaration.
        /// For: import * as X from 'f'; returns X
        /// For: import * from 'f'; returns string.Empty;
        /// </summary>
        public static string GetAlias(this IImportDeclaration importDeclaration)
        {
            // TODO:ST: consider better design for optional named stuff.
            // Current design was copied from language service and not perfect!
            if (importDeclaration.IsLikeImport)
            {
                return string.Empty;
            }

            return importDeclaration.ImportClause.NamedBindings.Name.Text;
        }

        /// <summary>
        /// Gets the first declaration of a variable statement
        /// </summary>
        /// <param name="variableStatement">A variable statement</param>
        /// <returns>The first variable declaration within the statement or null if no variable exists</returns>
        public static IDeclaration GetFirstDeclarationOrDefault(this IVariableStatement variableStatement)
        {
            var elements = variableStatement?.DeclarationList?.Declarations;

            if (elements == null || elements.Count == 0)
            {
                return null;
            }

            return elements[0];
        }

        /// <summary>
        /// Gets the type of the first declaration of a variable statement
        /// </summary>
        /// <param name="variableStatement">A variable statement</param>
        /// <param name="typeChecker">A typechecker</param>
        /// <returns>The type of the first variable declaration within the statement or null if no variable exists</returns>
        [CanBeNull]
        public static IType GetTypeOfFirstVariableDeclarationOrDefault(this IVariableStatement variableStatement, ITypeChecker typeChecker)
        {
            var firstDeclaration = GetFirstDeclarationOrDefault(variableStatement);

            if (firstDeclaration == null)
            {
                return null;
            }

            return typeChecker.GetTypeAtLocation(firstDeclaration);
        }

        private static T ThrowInvalidCastException<T>(INode sourceNode)
        {
            string targetType = typeof(T).Name;
            throw new InvalidCastException(I($"Specified cast from node '{sourceNode.GetType()}' with '{sourceNode.Kind}' kind to '{targetType}' is not valid."));
        }
    }

    /// <nodoc/>
    public static class ModuleDeclarationExtensions
    {
        /// <summary>
        /// Returns a full name for compound namespace.
        /// </summary>
        /// <remarks>
        /// This method returns A.B for 'namespace A.B {}' declaration.
        /// </remarks>
        public static string GetFullNameString(this IModuleDeclaration moduleDeclaration)
        {
            return string.Join(".", moduleDeclaration.GetFullName());
        }

        /// <summary>
        /// Returns a full name for compound namespace.
        /// </summary>
        /// <remarks>
        /// This property returns A.B for 'namespace A.B {}' declaration.
        /// Technically, such a declaration is nested and has 'A' as a name and IModuleDeclaration as a body.
        /// </remarks>
        public static IEnumerable<string> GetFullName(this IModuleDeclaration moduleDeclaration)
        {
            return CompoundName(moduleDeclaration).Select(m => m.Text);
        }

        /// <summary>
        /// Returns nested most module block for the <paramref name="moduleDeclaration"/>.
        /// </summary>
        /// <remarks>
        /// Namespace declaration like 'namespace A.B {}' has two nested declaration, first of it
        /// will have another declaration inside.
        /// </remarks>
        [JetBrains.Annotations.NotNull]
        public static IModuleBlock GetModuleBlock(this IModuleDeclaration moduleDeclaration)
        {
            var resultCandidate = moduleDeclaration.Body.AsModuleBlock();
            if (resultCandidate != null)
            {
                return resultCandidate;
            }

            return GetModuleBlock(moduleDeclaration.Body);
        }

        /// <summary>
        /// Returns set of identifiers for module declaration.
        /// </summary>
        public static IEnumerable<IdentifierOrLiteralExpression> CompoundName(IModuleDeclaration moduleDeclaration)
        {
            yield return moduleDeclaration.Name;

            var nestedModule = moduleDeclaration.Body.AsModuleDeclaration();
            if (nestedModule != null)
            {
                foreach (var n in CompoundName(nestedModule))
                {
                    yield return n;
                }
            }
        }

        /// <summary>
        /// Returns whether this is a predefined type.
        /// </summary>
        public static bool IsPredefinedType([JetBrains.Annotations.NotNull]this ITypeNode type)
        {
            switch (type.Kind)
            {
                case SyntaxKind.AnyKeyword:
                case SyntaxKind.NumberKeyword:
                case SyntaxKind.BooleanKeyword:
                case SyntaxKind.StringKeyword:
                case SyntaxKind.VoidKeyword:
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns type arguments of a given type, if any.  The result is never null;
        /// </summary>
        public static INodeArray<ITypeNode> GetTypeArguments([JetBrains.Annotations.NotNull] this ITypeNode type)
        {
            return type is ITypeReferenceNode typeReference
                ? typeReference.TypeArguments ?? NodeArray.Empty<ITypeNode>()
                : NodeArray.Empty<ITypeNode>();
        }
    }
}
