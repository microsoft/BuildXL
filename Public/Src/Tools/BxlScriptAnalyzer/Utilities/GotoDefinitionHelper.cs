// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;
using ISymbol = TypeScript.Net.Types.ISymbol;
using NotNullAttribute = JetBrains.Annotations.NotNullAttribute;

namespace TypeScript.Net.DScript
{
    /// <summary>
    /// Provider for Go-to-definition and Peek-defintion functionalities.
    /// </summary>
    public sealed class GotoDefinitionHelper
    {
        private readonly ITypeChecker TypeChecker;

        /// <nodoc/>
        public GotoDefinitionHelper(ITypeChecker typeChecker)
        {
            TypeChecker = typeChecker;
        }

        /// <nodoc />
        public Possible<IReadOnlyList<SymbolLocation>> GetDefinitionAtPosition(INode node)
        {
            // TODO: consider special hint/message if the current node is a template expression with interpolated expressions.
            if (node.Kind == SyntaxKind.NoSubstitutionTemplateLiteral && node.Parent.Kind == SyntaxKind.TaggedTemplateExpression &&
                node.Parent.Cast<ITaggedTemplateExpression>().IsWellKnownTemplateExpression(out var name))
            {
                return DefinitionForTemplateExpression(node, name);
            }

            var result = GetDefinitionFromVariableDeclarationList(node);

            if (result.Succeeded)
            {
                return result;
            }

            if (Utilities.IsJumpStatementTarget(node))
            {
                var labelName = node.Cast<IIdentifier>().Text;
                var label = Utilities.GetTargetLabel(node.Parent, labelName);

                // TODO: saqadri - Port
                // return label ? [createDefinitionInfo(label, ScriptElementKind.label, labelName, /*containerName*/ undefined)] : undefined;
                return Success(new[]
                {
                    GetLocationFromNode(label,symbol: null),
                });
            }

            var symbol = TypeChecker.GetSymbolAtLocation(node) ?? node.Symbol ?? node.ResolvedSymbol;

            // Could not find a symbol e.g. node is string or number keyword,
            // or the symbol was an internal symbol and does not have a declaration e.g. undefined symbol
            if (symbol == null)
            {
                // return null;
                return SilentError();
            }

            // If this is an alias, and the request came at the declaration location
            // get the aliased symbol instead. This allows for goto def on an import e.g.
            //   import {A, B} from "mod";
            // to jump to the implementation directly.
            if ((symbol.Flags & SymbolFlags.Alias) != SymbolFlags.None)
            {
                var declaration = symbol.Declarations.FirstOrDefault();

                // Go to the original declaration for cases:
                //
                //   (1) when the aliased symbol was declared in the location(parent).
                //   (2) when the aliased symbol is originating from a named import.
                if (node.Kind == SyntaxKind.Identifier &&
                    declaration != null &&
                    (node.Parent.ResolveUnionType() == declaration.ResolveUnionType() ||
                    (declaration.Kind == SyntaxKind.ImportSpecifier && declaration?.Parent.Kind == SyntaxKind.NamedImports)))
                {
                    symbol = TypeChecker.GetAliasedSymbol(symbol);
                }
                else if (node.Kind == SyntaxKind.Identifier && declaration?.Kind == SyntaxKind.NamespaceImport && declaration.Name != null)
                {
                    return new[] {GetLocationFromNode(declaration.Name, symbol)};
                }
            }

            // Because name in short-hand property assignment has two different meanings: property name and property value,
            // using go-to-definition at such position should go to the variable declaration of the property value rather than
            // go to the declaration of the property name (in this case stay at the same position). However, if go-to-definition
            // is performed at the location of property access, we would like to go to definition of the property in the short-hand
            // assignment. This case and others are handled by the following code.
            if (node.Parent.Kind == SyntaxKind.ShorthandPropertyAssignment)
            {
                var shorthandSymbol = TypeChecker.GetShorthandAssignmentValueSymbol(symbol.ValueDeclaration);
                if (shorthandSymbol == null)
                {
                    return Success(new SymbolLocation[] { });
                }

                var shorthandDeclaratons = shorthandSymbol.GetDeclarations();

                // var shorthandSymbolKind = GetSymbolKind(shorthandSymbol, node);
                // var shorthandSymbolName = TypeChecker.SymbolToString(shorthandSymbol);
                // var shorthandContainerName = TypeChecker.SymbolToString(shorthandSymbol.Parent, node);
                return Success(shorthandDeclaratons.Select(
                    declaration => GetLocationFromNode(declaration, shorthandSymbol)).ToArray());
            }

            if (Utilities.IsNameOfPropertyAssignment(node))
            {
                return GetDefinitionForPropertyAssignment(node);
            }

            return GetDefinitionFromSymbol(symbol, node);
        }

        private Possible<IReadOnlyList<SymbolLocation>> GetDefinitionForPropertyAssignment(INode node)
        {
            var symbol = Utilities.GetPropertySymbolsFromContextualType(node, TypeChecker).FirstOrDefault();
            if (symbol == null)
            {
                return SilentError();
            }

            return GetDefinitionFromSymbol(symbol, node);
        }

        private static Possible<IReadOnlyList<SymbolLocation>> DefinitionForTemplateExpression(INode node, string name)
        {
            var taggedTemplte = node.Parent.Cast<ITaggedTemplateExpression>();
            var literal = taggedTemplte.TemplateExpression.Cast<ILiteralExpression>();
            var interpolationKind = taggedTemplte.GetInterpolationKind();

            // This is p`` or f`` or `r`
            if (interpolationKind == InterpolationKind.FileInterpolation ||
                interpolationKind == InterpolationKind.PathInterpolation ||
                interpolationKind == InterpolationKind.RelativePathInterpolation)
            {
                string fullPath = string.Empty;

                // Exceptions can happen if the string literal contains any invalid path characters (such as * and ?)
                // as well if there is any string interoplation in them such as ${variable} as the $ is also an invalid path
                // character.
                try
                {
                    if (Path.IsPathRooted(literal.Text))
                    {
                        fullPath = literal.Text;
                    }
                    else
                    {
                        fullPath = Path.Combine(Path.GetDirectoryName(node.GetSourceFile().Path.AbsolutePath), literal.Text);
                    }
                }
                catch (ArgumentException)
                {
                    // An ArgumentException from IsPathRooted indicates that the path has invalid characters.
                    // Path.IsPathRooted and Path.Combine throw ArgumentException;
                }

                // This is path or file.
                // They can be absolute or relative to the current file.

                if (!string.IsNullOrEmpty(fullPath))
                {
                    return Success(SymbolLocation.FileLocation(fullPath));
                }
            }

            return SilentError();
        }

        /// <summary>
        /// Handles when the user clicks on either the 'const' or 'let' portion of a variable declaration
        /// </summary>
        /// <param name="node">Node that _might_ be a <see cref="IVariableDeclarationList"/></param>
        /// <returns>The location of the variable declaration list's type or null if this doesn't apply</returns>
        private Possible<IReadOnlyList<SymbolLocation>> GetDefinitionFromVariableDeclarationList(INode node)
        {
            if (node.Kind != SyntaxKind.VariableDeclarationList)
            {
                return SilentError();
            }

            var asVariableStatement = node.Parent?.As<IVariableStatement>();

            if (asVariableStatement == null)
            {
                return SilentError();
            }

            var type = asVariableStatement.GetTypeOfFirstVariableDeclarationOrDefault(TypeChecker);
            var typeDeclaration = type?.Symbol?.GetFirstDeclarationOrDefault();

            if (typeDeclaration?.Symbol == null)
            {
                return SilentError();
            }

            return GetDefinitionFromSymbol(typeDeclaration.Symbol, typeDeclaration);
        }

        private Possible<IReadOnlyList<SymbolLocation>> GetDefinitionFromSymbol([NotNull]ISymbol symbol, INode node)
        {
            var result = new List<SymbolLocation>();
            var declarations = symbol.GetDeclarations();
            var symbolName = TypeChecker.SymbolToString(symbol);
            var symbolKind = Utilities.GetSymbolKind(symbol, node);
            var containerSymbol = symbol.Parent;
            var containerName = containerSymbol != null ? TypeChecker.SymbolToString(containerSymbol, node) : string.Empty;

            if (!TryAddConstructSignature(symbol, node, symbolKind, symbolName, containerName, result) &&
                !TryAddCallSignature(symbol, node, symbolKind, symbolName, containerName, result))
            {
                // Just add all the declarations
                foreach (var declaration in declarations)
                {
                    result.Add(GetLocationFromNode(declaration, symbol));
                }
            }

            return result.Count != 0 ? Success(result.ToArray()) : SilentError();
        }

        private static bool TryAddConstructSignature(
            ISymbol symbol,
            INode location,
            string symbolKind,
            string symbolName,
            string containerName,
            List<SymbolLocation> result)
        {
            // Applicable only if we are in a new expression, or we are on a constructor declaration
            // and in either case the symbol has a construct signature definition, i.e. class
            if (Utilities.IsNewExpressionTarget(location) || location.Kind == SyntaxKind.ConstructorKeyword)
            {
                if ((symbol.Flags & SymbolFlags.Class) != SymbolFlags.None)
                {
                    // Find the first class-like declaration and try to get the construct signature.
                    foreach (var declaration in symbol.GetDeclarations())
                    {
                        var classLikeDeclaration = NodeUtilities.IsClassLike(declaration);
                        if (classLikeDeclaration != null)
                        {
                            return TryAddSignature(
                                symbol,
                                classLikeDeclaration.Members.Elements,
                                /*selectConstructors*/ true,
                                symbolKind,
                                symbolName,
                                containerName,
                                result);
                        }
                    }

                    Debug.Fail("Expected declaration to have at least one class-like declaration");
                }
            }

            return false;
        }

        private static bool TryAddCallSignature(
            ISymbol symbol,
            INode location,
            string symbolKind,
            string symbolName,
            string containerName,
            List<SymbolLocation> result)
        {
            if (Utilities.IsCallExpressionTarget(location) ||
                Utilities.IsNewExpressionTarget(location) ||
                Utilities.IsNameOfFunctionDeclaration(location))
            {
                return TryAddSignature(
                    symbol,
                    symbol.Declarations,
                    /*selectConstructors*/ false,
                    symbolKind,
                    symbolName,
                    containerName,
                    result);
            }

            return false;
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters")]
        private static bool TryAddSignature(
            ISymbol symbol,
            IEnumerable<IDeclaration> signatureDeclarations,
            bool selectConstructors,
            string symbolKind,
            string symbolName,
            string containerName,
            List<SymbolLocation> result)
        {
            var declarations = new List<IDeclaration>();
            IDeclaration definition = null;

            foreach (var declaration in signatureDeclarations ?? Enumerable.Empty<IDeclaration>())
            {
                if ((selectConstructors && declaration.Kind == SyntaxKind.Constructor) ||
                    (!selectConstructors && (declaration.Kind == SyntaxKind.FunctionDeclaration || declaration.Kind == SyntaxKind.MethodSignature)))
                {
                    declarations.Add(declaration);
                    if (declaration.As<IFunctionLikeDeclaration>()?.Body != null)
                    {
                        definition = declaration;
                    }
                }
            }

            if (definition != null)
            {
                result.Add(GetLocationFromNode(definition, symbol));
                return true;
            }

            if (declarations.Count > 0)
            {
                result.Add(GetLocationFromNode(declarations.LastOrUndefined(), symbol));
                return true;
            }

            return false;
        }

        private static Possible<IReadOnlyList<SymbolLocation>> Success(params SymbolLocation[] locations)
        {
            return locations;
        }

        private static Possible<IReadOnlyList<SymbolLocation>> SilentError()
        {
            return new Failure<string>(string.Empty);
        }

        /// <nodoc />
        public static SymbolLocation GetLocationFromNode(INode node, ISymbol symbol)
        {
            return SymbolLocation.LocalLocation(
                node.GetSourceFile().Path.AbsolutePath,
                TextRange.FromLength(
                    node.GetNodeStartPositionWithoutTrivia(),
                    node.GetNodeWidth()),
                symbol);
        }
    }
}
