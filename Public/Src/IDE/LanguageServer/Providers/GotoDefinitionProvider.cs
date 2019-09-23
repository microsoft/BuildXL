// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using LanguageServer;
using LanguageServer.Json;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.DScript;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;
using CancellationToken = System.Threading.CancellationToken;
using ISymbol = TypeScript.Net.Types.ISymbol;
using DScriptUtilities = TypeScript.Net.DScript.Utilities;
using NotNullAttribute= JetBrains.Annotations.NotNullAttribute;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Provider for Go-to-definition and Peek-defintion functionalities.
    /// </summary>
    public sealed class GotoDefinitionProvider : IdeProviderBase
    {
        /// <nodoc/>
        public GotoDefinitionProvider(ProviderContext providerContext)
            : base(providerContext)
        { }

        /// <nodoc />
        public Result<ArrayOrObject<Location, Location>, ResponseError> GetDefinitionAtPosition(TextDocumentPositionParams @params, CancellationToken token)
        {
            // TODO: support cancellation
            if (!TryFindNode(@params, out var node))
            {
                return SilentError();
            }

            // TODO: consider special hint/message if the current node is a template expression with interpolated expressions.
            if (node.Kind == SyntaxKind.NoSubstitutionTemplateLiteral && node.Parent.Kind == SyntaxKind.TaggedTemplateExpression &&
                node.Parent.Cast<ITaggedTemplateExpression>().IsWellKnownTemplateExpression(out var name))
            {
                return DefinitionForTemplateExpression(node, name);
            }

            var result = GetDefinitionFromVariableDeclarationList(node);

            if (result != null)
            {
                return result;
            }

            if (DScriptUtilities.IsJumpStatementTarget(node))
            {
                var labelName = node.Cast<IIdentifier>().Text;
                var label = DScriptUtilities.GetTargetLabel(node.Parent, labelName);

                // TODO: saqadri - Port
                // return label ? [createDefinitionInfo(label, ScriptElementKind.label, labelName, /*containerName*/ undefined)] : undefined;
                return Success(new[]
                {
                    GetLocationFromNode(label),
                });
            }

            // TODO: saqadri - port
            // Triple slash reference comments
            // const comment = forEach(sourceFile.referencedFiles, r => (r.pos <= position && position < r.end) ? r : undefined);
            // if (comment)
            // {
            //    const referenceFile = tryResolveScriptReference(program, sourceFile, comment);
            //    if (referenceFile)
            //    {
            //        return [{
            //            fileName: referenceFile.fileName,
            //            textSpan: createTextSpanFromBounds(0, 0),
            //            kind: ScriptElementKind.scriptElement,
            //            name: comment.fileName,
            //            containerName: undefined,
            //            containerKind: undefined
            //        }];
            //    }
            //    return undefined;
            // }
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
                    return Success(new Location[] { });
                }

                var shorthandDeclaratons = shorthandSymbol.GetDeclarations();

                // var shorthandSymbolKind = GetSymbolKind(shorthandSymbol, node);
                // var shorthandSymbolName = TypeChecker.SymbolToString(shorthandSymbol);
                // var shorthandContainerName = TypeChecker.SymbolToString(shorthandSymbol.Parent, node);
                return Success(shorthandDeclaratons.Select(
                    declaration => GetLocationFromNode(declaration)).ToArray());
            }

            if (DScriptUtilities.IsNameOfPropertyAssignment(node))
            {
                return GetDefinitionForPropertyAssignment(node);
            }

            return GetDefinitionFromSymbol(symbol, node);
        }

        private Result<ArrayOrObject<Location, Location>, ResponseError> GetDefinitionForPropertyAssignment(INode node)
        {
            var symbol = DScriptUtilities.GetPropertySymbolsFromContextualType(node, TypeChecker).FirstOrDefault();
            if (symbol == null)
            {
                return SilentError();
            }

            return GetDefinitionFromSymbol(symbol, node);
        }

        private static Result<ArrayOrObject<Location, Location>, ResponseError> DefinitionForTemplateExpression(INode node, string name)
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
                    return Success( new[]
                    {
                        new Location()
                        {
                            Range = PositionExtensions.EmptyRange(),
                            Uri = UriExtensions.GetUriFromPath(fullPath).ToString(),
                        },
                    });
                }
            }

            return SilentError();
        }

        /// <summary>
        /// Handles when the user clicks on either the 'const' or 'let' portion of a variable declaration
        /// </summary>
        /// <param name="node">Node that _might_ be a <see cref="IVariableDeclarationList"/></param>
        /// <returns>The location of the variable declaration list's type or null if this doesn't apply</returns>
        private Result<ArrayOrObject<Location, Location>, ResponseError> GetDefinitionFromVariableDeclarationList(INode node)
        {
            if (node.Kind != SyntaxKind.VariableDeclarationList)
            {
                return null;
            }

            var asVariableStatement = node.Parent?.As<IVariableStatement>();

            if (asVariableStatement == null)
            {
                return null;
            }

            var type = asVariableStatement.GetTypeOfFirstVariableDeclarationOrDefault(TypeChecker);
            var typeDeclaration = type?.Symbol?.GetFirstDeclarationOrDefault();

            if (typeDeclaration?.Symbol == null)
            {
                return null;
            }

            return GetDefinitionFromSymbol(typeDeclaration.Symbol, typeDeclaration);
        }

        private Result<ArrayOrObject<Location, Location>, ResponseError> GetDefinitionFromSymbol([NotNull]ISymbol symbol, INode node)
        {
            var result = new List<Location>();
            var declarations = symbol.GetDeclarations();
            var symbolName = TypeChecker.SymbolToString(symbol);
            var symbolKind = DScriptUtilities.GetSymbolKind(symbol, node);
            var containerSymbol = symbol.Parent;
            var containerName = containerSymbol != null ? TypeChecker.SymbolToString(containerSymbol, node) : string.Empty;

            if (!TryAddConstructSignature(symbol, node, symbolKind, symbolName, containerName, result) &&
                !TryAddCallSignature(symbol, node, symbolKind, symbolName, containerName, result))
            {
                // Just add all the declarations
                foreach (var declaration in declarations)
                {
                    result.Add(GetLocationFromNode(declaration));
                }
            }

            return Success(result.ToArray());
        }

        private static bool TryAddConstructSignature(
            ISymbol symbol,
            INode location,
            string symbolKind,
            string symbolName,
            string containerName,
            List<Location> result)
        {
            // Applicable only if we are in a new expression, or we are on a constructor declaration
            // and in either case the symbol has a construct signature definition, i.e. class
            if (DScriptUtilities.IsNewExpressionTarget(location) || location.Kind == SyntaxKind.ConstructorKeyword)
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
            List<Location> result)
        {
            if (DScriptUtilities.IsCallExpressionTarget(location) ||
                DScriptUtilities.IsNewExpressionTarget(location) ||
                DScriptUtilities.IsNameOfFunctionDeclaration(location))
            {
                return TryAddSignature(
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
            IEnumerable<IDeclaration> signatureDeclarations,
            bool selectConstructors,
            string symbolKind,
            string symbolName,
            string containerName,
            List<Location> result)
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
                result.Add(GetLocationFromNode(definition));
                return true;
            }

            if (declarations.Count > 0)
            {
                result.Add(GetLocationFromNode(declarations.LastOrUndefined()));
                return true;
            }

            return false;
        }

        private static Result<ArrayOrObject<Location, Location>, ResponseError> Success(Location[] locations)
        {
            return Result<ArrayOrObject<Location, Location>, ResponseError>.Success(locations);
        }

        private static Result<ArrayOrObject<Location, Location>, ResponseError> SilentError()
        {
            return Result<ArrayOrObject<Location, Location>, ResponseError>.Success(new Location[] { });
        }

        /// <nodoc />
        public static Location GetLocationFromNode(INode node)
        {
            return new Location()
            {
                Range = node.ToRange(),
                Uri = node.GetSourceFile().ToUri().ToString(),
            };
        }
    }
}
