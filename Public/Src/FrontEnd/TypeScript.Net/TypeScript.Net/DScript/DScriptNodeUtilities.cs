// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using BuildXL.FrontEnd.Script.Constants;
using JetBrains.Annotations;
using TypeScript.Net.DScript;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;
using TypeScript.Net.Utilities;

namespace TypeScript.Net.Parsing
{
    /// <nodoc/>
    public static class DScriptNodeUtilities
    {
        /// <summary>
        /// Tries to find the node with the smallest width in the <paramref name="sourceFile"/> that contains the input <paramref name="position"/>.
        /// </summary>
        public static bool TryGetNodeAtPosition(
            ISourceFile sourceFile,
            int position,
            out INode nodeAtPosition)
        {
            return TryGetNodeAtPosition(
                sourceFile,
                position,
                isNodeAcceptable: (node) => true, // Any node that matches the position is acceptable
                nodeAtPosition: out nodeAtPosition);
        }

        /// <summary>
        /// Tries to find the node with the smallest width in the <paramref name="sourceFile"/>
        /// that contains the input <paramref name="position"/>, and is deemed acceptable by
        /// <paramref name="isNodeAcceptable"/>.
        /// </summary>
        public static bool TryGetNodeAtPosition(
            ISourceFile sourceFile,
            int position,
            Func<INode, bool> isNodeAcceptable,
            out INode nodeAtPosition)
        {
            var lineAndColumn = LineInfoExtensions.GetLineAndColumnBy(
                position: position,
                sourceFile: sourceFile,
                skipTrivia: false);

            return TryGetNodeAtPosition(sourceFile, lineAndColumn, isNodeAcceptable, out nodeAtPosition);
        }

        /// <summary>
        /// Tries to find the node with the smallest width in the <paramref name="sourceFile"/> that contains the input <paramref name="lineAndColumn"/>.
        /// </summary>
        public static bool TryGetNodeAtPosition(
            ISourceFile sourceFile,
            LineAndColumn lineAndColumn,
            out INode nodeAtPosition)
        {
            return TryGetNodeAtPosition(
                sourceFile,
                lineAndColumn,
                isNodeAcceptable: (node) => true, // Any node that matches the lineAndColumn is acceptable
                nodeAtPosition: out nodeAtPosition);
        }

        /// <summary>
        /// Tries to find the node with the smallest width in the <paramref name="sourceFile"/>
        /// that contains the input <paramref name="lineAndColumn"/>, and is deemed acceptable by
        /// <paramref name="isNodeAcceptable"/>.
        /// </summary>
        /// <param name="sourceFile">Source file to search for node in.</param>
        /// <param name="lineAndColumn">The line-and-column location to get the node for.</param>
        /// <param name="isNodeAcceptable">A function that lets the caller have a say about which node to pick.</param>
        /// <param name="nodeAtPosition">Holds the returned node if the function returns true, and null otherwise.</param>
        /// <remarks>
        /// This function can return null in cases were there is no node at the current position. For example, asking
        /// for a node at an empty source line, or asking for a node when the position is inside a comment that
        /// encompasses the remainder of the file.
        /// </remarks>
        public static bool TryGetNodeAtPosition(
            ISourceFile sourceFile,
            LineAndColumn lineAndColumn,
            Func<INode, bool> isNodeAcceptable,
            out INode nodeAtPosition)
        {
            INode currentNode = null;

            NodeWalker.ForEachChildRecursively(sourceFile, (node) =>
            {
                if (node.IsInjectedForDScript())
                {
                    // Need to skip injected nodes.
                    return null;
                }

                var nodeStart = LineInfoExtensions.GetLineAndColumnBy(
                    position: Types.NodeUtilities.GetTokenPosOfNode(node, sourceFile),
                    sourceFile: sourceFile,
                    skipTrivia: false);

                var nodeEnd = LineInfoExtensions.GetLineAndColumnBy(
                    position: node.End,
                    sourceFile: sourceFile,
                    skipTrivia: false);

                if (nodeStart > lineAndColumn || nodeEnd < lineAndColumn)
                {
                    // If this node starts after or ends before the position we are searching for, then we skip it
                    return null;
                }

                if (currentNode == null || node.GetWidth() <= currentNode.GetWidth())
                {
                    if (isNodeAcceptable(node))
                    {
                        currentNode = node;
                    }
                }

                return (object)null;
            });

            nodeAtPosition = currentNode;
            return currentNode != null;
        }

        /// <nodoc />
        public static int GetWidth(this INode node)
        {
            return node.End - Types.NodeUtilities.GetTokenPosOfNode(node, node.GetSourceFile());
        }
    }

    /// <summary>
    /// Type of an import function.
    /// </summary>
    public enum DScriptImportFunctionKind
    {
        /// <nodoc />
        None,

        /// <summary>
        /// <code>importFrom(...)</code>
        /// </summary>
        ImportFrom,

        /// <summary>
        /// <code>importFile(...)</code>
        /// </summary>
        ImportFile,
    }

    /// <nodoc/>
    public static class DScriptImportUtilities
    {
        /// <summary>
        /// Whether the <paramref name="node"/> is an <code>importFrom(...)</code> or <code>importFile(...)</code> with the required number of arguments (i.e. with 1 argument).
        /// </summary>
        public static bool IsImportCall(
            [NotNull] this INode node,
            out ICallExpression callExpression,
            out DScriptImportFunctionKind importKind,
            out IExpression argumentAsExpression,
            out ILiteralExpression argumentAsLiteral)
        {
            Contract.Requires(node != null);

            const int parameterCount = 1;
            callExpression = node.As<ICallExpression>();
            var expressionIdentifier = callExpression?.Expression.As<IIdentifier>();

            if (
                (expressionIdentifier?.Text == Names.InlineImportFunction ||
                 expressionIdentifier?.Text == Names.InlineImportFileFunction)
                && (callExpression.TypeArguments == null || callExpression.TypeArguments.Count == 0)
                && callExpression.Arguments.Length >= parameterCount)
            {
                importKind = expressionIdentifier.Text == Names.InlineImportFunction
                    ? DScriptImportFunctionKind.ImportFrom
                    : DScriptImportFunctionKind.ImportFile;

                argumentAsExpression = callExpression.Arguments[0];
                argumentAsLiteral = argumentAsExpression.As<ILiteralExpression>();

                return true;
            }

            importKind = DScriptImportFunctionKind.None;
            argumentAsExpression = null;
            argumentAsLiteral = null;
            return false;
        }

        /// <summary>
        /// Deconstructs a template expression.
        /// </summary>
        /// <remarks>
        /// "pattern matches" a tagged template expression into two cases:
        /// 1. Literal case like <code>p`string literal`</code> (in this case <paramref name="literal"/> would not be null).
        /// 2. Template expression case like <code>p`{foo}</code> (in this case <paramref name="head"/> and <paramref name="templateSpans"/> are not null).
        /// </remarks>
        public static void Deconstruct(
            [CanBeNull]this ITaggedTemplateExpression node,
            out InterpolationKind kind,
            out ILiteralExpression literal,
            out ITemplateLiteralFragment head,
            out INodeArray<ITemplateSpan> templateSpans)
        {
            kind = InterpolationKind.Unknown;
            literal = null;
            head = null;
            templateSpans = null;

            if (node == null)
            {
                return;
            }

            if (node.Tag.Kind != SyntaxKind.Identifier)
            {
                // Looks like the tagged expression is invalid.
                return;
            }

            var text = node.Tag.Cast<IIdentifier>().Text;
            kind = GetInterpolationKind(text);

            if (kind == InterpolationKind.Unknown)
            {
                return;
            }

            literal = node.TemplateExpression.As<ILiteralExpression>();
            if (literal == null)
            {
                // This is another case: tagged template actually has template expressions.
                var template = node.TemplateExpression.Cast<ITemplateExpression>();
                head = template.Head;
                templateSpans = template?.TemplateSpans;

                Contract.Assert(head != null);
                Contract.Assert(templateSpans != null);
            }

            InterpolationKind GetInterpolationKind(string factoryName)
            {
                if (factoryName.Length == 0)
                {
                    return InterpolationKind.StringInterpolation;
                }

                var c = factoryName[0];

                switch (c)
                {
                    case Names.PathInterpolationFactory:
                        return InterpolationKind.PathInterpolation;
                    case Names.DirectoryInterpolationFactory:
                        return InterpolationKind.DirectoryInterpolation;
                    case Names.FileInterpolationFactory:
                        return InterpolationKind.FileInterpolation;
                    case Names.RelativePathInterpolationFactory:
                        return InterpolationKind.RelativePathInterpolation;
                    case Names.PathAtomInterpolationFactory:
                        return InterpolationKind.PathAtomInterpolation;
                    default:
                        return InterpolationKind.Unknown;
                }
            }
        }

        /// <summary>
        /// Whether the node is an 'importFrom(...)' with a minimum number of arguments (1 is the default)
        /// </summary>
        public static bool IsImportFrom(this INode node, bool checkArgumentIsStringLiteral = true, int parameterCount = 1)
        {
            return IsImport(node, Names.InlineImportFunction, checkArgumentIsStringLiteral, parameterCount);
        }

        /// <summary>
        /// Whether the node is an 'importFile(...)' with a minimum number of arguments (1 is the default)
        /// </summary>
        public static bool IsImportFile(this INode node, int parameterCount = 1)
        {
            // Do not check argument for string literal - it should be a TaggedTemplate!
            return IsImport(node, Names.InlineImportFileFunction, false, parameterCount);
        }

        /// <summary>
        /// Helper function for discovering whether <paramref name="node"/> is a method call for the import method named by <paramref name="functionName"/>
        /// </summary>
        private static bool IsImport(this INode node, string functionName, bool checkArgumentIsStringLiteral = true, int parameterCount = 1)
        {
            var callExpression = node.As<ICallExpression>();
            var expressionIdentifier = callExpression?.Expression.As<IIdentifier>();

            if (expressionIdentifier?.Text == functionName &&
                callExpression.Arguments.Length >= parameterCount)
            {
                // In some cases we should skip checking literal for 'importFrom'.
                return !checkArgumentIsStringLiteral || callExpression.Arguments[0].Kind == SyntaxKind.StringLiteral;
            }

            return false;
        }

        /// <summary>
        /// Whether the node is a call expression. Sets the out parameter to the name of the method.
        /// </summary>
        public static bool TryGetCallMethodName(this INode node, out string functionName, bool checkArgumentIsStringLiteral = true, int parameterCount = 1)
        {
            var callExpression = node.As<ICallExpression>();
            var expressionIdentifier = callExpression?.Expression?.As<IIdentifier>();

            functionName = expressionIdentifier?.Text;

            if ((callExpression?.Arguments?.Length ?? 0) >= parameterCount)
            {
                // In some cases we should skip checking literal for 'importFrom'.
                return !checkArgumentIsStringLiteral || callExpression.Arguments[0].Kind == SyntaxKind.StringLiteral;
            }

            return false;
        }

        /// <nodoc />
        public static bool IsWithQualifier(this INode node)
        {
            var callExpression = node.As<ICallExpression>();
            var expressionIdentifier = callExpression?.Expression.As<IIdentifier>();

            if (expressionIdentifier?.Text == Names.WithQualifierFunction &&
                callExpression.Arguments.Length == 1)
            {
                // In some cases we should skip checking literal for 'importFrom'.
                return true;
            }

            return false;
        }

        /// <nodoc />
        public static bool IsToStringCall(this IPropertyAccessExpression propertyAccessExpression)
        {
            var callExpression = propertyAccessExpression.Parent?.As<ICallExpression>();

            if (propertyAccessExpression.Name.Text == Names.ToStringFunction &&
                callExpression?.Arguments.Length == 0)
            {
                return true;
            }

            return false;
        }

        /// <nodoc />
        public static bool IsCurrentQualifier(this INode node)
        {
            var identifier = node.As<IIdentifier>();
            return identifier != null && identifier.Text == Names.CurrentQualifier;
        }

        /// <nodoc />
        public static bool IsTemplateDeclaration(this IDeclaration node)
        {
            var variableDeclaration = node.As<IVariableDeclaration>();
            if (variableDeclaration == null)
            {
                return false;
            }

            var id = variableDeclaration.Name.As<IIdentifier>();
            return id != null && id.Text == Names.Template;
        }

        /// <summary>
        /// Returns the module name specified in the 'importFrom' call
        /// </summary>
        public static IStringLiteral GetSpecifierInImportFrom(this INode node)
        {
            Contract.Requires(IsImportFrom(node));

            var specifier = node.As<ICallExpression>().Arguments[0];
            return specifier.As<IStringLiteral>();
        }

        /// <summary>
        /// Returns the module name specified in the 'importFile' call, or <code>null</code>
        /// if <paramref name="node"/> is not an <see cref="ICallExpression"/> with
        /// a <see cref="TaggedTemplateExpression"/> argument.
        /// </summary>
        public static ILiteralExpression GetSpecifierInImportFile(this INode node)
        {
            Contract.Requires(IsImportFile(node));

            var specifier = node.As<ICallExpression>().Arguments.FirstOrDefault();
            var taggedTemplate = specifier?.As<ITaggedTemplateExpression>();
            var literal = taggedTemplate?.Template?.As<ILiteralExpression>();
            if (literal != null && literal.Text.StartsWith("./"))
            {
                return new LiteralExpression(literal.Text.Substring(2));
            }
            else
            {
                return literal;
            }
        }

        /// <summary>
        /// Returns whether the <paramref name="node"/> is a DScript 'public' decorator.
        /// </summary>
        public static bool IsScriptPublicDecorator(this IDecorator node)
        {
            return node.Expression.As<IIdentifier>()?.OriginalKeywordKind == SyntaxKind.PublicKeyword;
        }
    }

    /// <nodoc/>
    // TODO: move parsing-specific functionality from DScript.Ast here. We currently have some duplication.
    public static class DScriptConfigurationUtilities
    {
        /// <summary>
        /// Extracts an object literal that was specified in a configuration function.
        /// </summary>
        public static bool TryExtractConfigurationLiteral(
            this ISourceFile sourceFile,
            out IObjectLiteralExpression literal, out string failureReason)
        {
            List<IObjectLiteralExpression> literals;

            if (!sourceFile.TryExtractLiterals(
                functionName: Names.ConfigurationFunctionCall,
                allowMultipleLiterals: false,
                literals: out literals,
                failureReason: out failureReason))
            {
                literal = null;
                return false;
            }

            literal = literals.First();
            return true;
        }

        /// <summary>
        /// Return the expression initializer for a given property name in an object literal
        /// </summary>
        public static bool TryFindAssignmentPropertyInitializer(
            this IObjectLiteralExpression objectLiteral,
            string propertyName,
            out IExpression expression)
        {
            foreach (var property in objectLiteral.Properties)
            {
                if (string.Equals(property.Name.Text, propertyName))
                {
                    var expressionAssignment = property.As<IPropertyAssignment>()?.Initializer;
                    if (expressionAssignment != null)
                    {
                        expression = expressionAssignment;
                        return true;
                    }
                }
            }

            expression = null;
            return false;
        }

        /// <summary>
        /// Return the literal initializer for a given property name in an object literal
        /// </summary>
        public static bool TryExtractLiteralFromAssignmentPropertyInitializer(
            this IObjectLiteralExpression objectLiteral,
            string propertyName,
            out string literal)
        {
            IExpression expression;
            if (TryFindAssignmentPropertyInitializer(objectLiteral, propertyName, out expression))
            {
                literal = expression.As<ILiteralExpression>()?.Text;
                return literal != null;
            }

            literal = null;
            return false;
        }

        /// <summary>
        /// Whether a statement is a configuration declaration (i.e., config(...))
        /// </summary>
        private static bool IsConfigurationDeclaration(this IStatement statement)
        {
            return statement.IsFunctionCallDeclaration(Names.ConfigurationFunctionCall);
        }

        /// <summary>
        /// Returns true when <paramref name="statement"/> is a call to a function <paramref name="functionName"/>.
        /// </summary>
        public static bool IsFunctionCallDeclaration(this IStatement statement, string functionName)
        {
            Contract.Requires(statement != null);

            // config statement is a special kind of statement declaration
            if (statement.Kind != SyntaxKind.ExpressionStatement)
            {
                return false;
            }

            var expressionStatement = statement.As<IExpressionStatement>();
            if (expressionStatement.Expression.Kind != SyntaxKind.CallExpression)
            {
                return false;
            }

            var callExpression = expressionStatement.Expression.Cast<ICallExpression>();

            var identifier = callExpression?.Expression.As<IIdentifier>();
            if (identifier == null)
            {
                return false;
            }

            return identifier.Text == functionName;
        }

        /// <nodoc />
        public static bool TryExtractLiterals(
            this ISourceFile sourceFile,
            string functionName,
            bool allowMultipleLiterals,
            out List<IObjectLiteralExpression> literals, out string failureReason)
        {
            Contract.Ensures(!Contract.Result<bool>() || Contract.ValueAtReturn(out literals).Any());

            literals = new List<IObjectLiteralExpression>();
            failureReason = string.Empty;

            if (sourceFile.Statements.Count == 0)
            {
                failureReason = "The source file contains no statements";
                return false;
            }

            if (!allowMultipleLiterals && sourceFile.Statements.Count > 1)
            {
                failureReason = string.Format(
                    CultureInfo.InvariantCulture,
                    "The source file must contain a single call to '{0}' function.",
                    functionName);
                return false;
            }

            foreach (var statement in sourceFile.Statements)
            {
                if (!statement.IsFunctionCallDeclaration(functionName))
                {
                    failureReason = string.Format(
                        CultureInfo.InvariantCulture,
                        "Unexpected statement in source file. Only call(s) to '{0}' function are allowed.",
                        functionName);
                    return false;
                }

                var expressionStatement = statement.As<IExpressionStatement>();
                var callExpression = expressionStatement.Expression.Cast<ICallExpression>();

                if (callExpression.Arguments.Count != 1)
                {
                    failureReason = string.Format(
                        CultureInfo.InvariantCulture,
                        "Configuration expression should take 1 argument but got {0}", callExpression.Arguments.Count);
                    return false;
                }

                var objectLiteral = callExpression.Arguments[0].As<IObjectLiteralExpression>();
                if (objectLiteral == null)
                {
                    failureReason = string.Format(
                        CultureInfo.InvariantCulture,
                        "Configuration expression should take object literal but got {0}", callExpression.Arguments[0].Kind);
                    return false;
                }

                literals.Add(objectLiteral);
            }

            return true;
        }

        /// <summary>
        /// Adds a public decorator to the node.
        /// </summary>
        /// <remarks>
        /// Doesn't check if already present, always adds it
        /// </remarks>
        public static TNode WithPublicDecorator<TNode>(this TNode node) where TNode : Node
        {
            var publicDecorator = new Decorator(
                new Identifier(Names.PublicDecorator));

            if (node.Decorators == null)
            {
                node.Decorators = new NodeArray<IDecorator>(publicDecorator);
            }
            else
            {
                node.Decorators.Add(publicDecorator);
            }

            return node;
        }
    }

    /// <nodoc/>
    public static class DScriptGlobConfigurationUtilities
    {
        /// <nodoc/>
        public static bool IsGlob(this IIdentifier identifier)
        {
            var functionName = identifier.Text;

            return string.Equals(functionName, Names.GlobFunction) ||
                   string.Equals(functionName, Names.GlobRFunction) ||
                   string.Equals(functionName, Names.GlobRecursivelyFunction);
        }

        /// <nodoc/>
        public static bool IsGlobRecursive(this IIdentifier identifier)
        {
            var functionName = identifier.Text;

            return string.Equals(functionName, Names.GlobRFunction) ||
                   string.Equals(functionName, Names.GlobRecursivelyFunction);
        }
    }
}
