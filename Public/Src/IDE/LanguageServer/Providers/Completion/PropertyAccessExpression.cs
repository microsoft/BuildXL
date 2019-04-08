// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Collections;
using System.Collections.Generic;
using System.Linq;
using TypeScript.Net.Types;

using static TypeScript.Net.DScript.Utilities;

namespace BuildXL.Ide.LanguageServer.Completion
{
    /// <nodoc />
    public static class PropertyAccessExpression
    {
        /// <summary>
        /// Returns whether or not the symbol is a type, or if it references a module that has exported types.
        /// </summary>
        /// <remarks>
        /// Note: This was ported directly from the TypeScript implementation. The name of the function
        /// isn't exactly representative of what it is doing.
        /// </remarks>
        private static bool SymbolCanBeReferencedAtTypeLocation(ISymbol symbol, ITypeChecker typeChecker)
        {
            symbol = symbol.ExportSymbol ?? symbol;

            symbol = SkipAlias(symbol, typeChecker);

            if ((symbol.Flags & SymbolFlags.Type) != SymbolFlags.None)
            {
                return true;
            }

            if ((symbol.Flags & SymbolFlags.Module) != SymbolFlags.None)
            {
                var exportedSymbols = typeChecker.GetExportsOfModule(symbol);
                foreach (var exportedSymbol in exportedSymbols)
                {
                    if (SymbolCanBeReferencedAtTypeLocation(exportedSymbol.Value, typeChecker))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Given a <paramref name="type"/>, returns the symbols that can be accessed relative to <paramref name="node"/>
        /// </summary>
        /// <param name="type">The type whose symbols will be tested for valid property access.</param>
        /// <param name="node">The node in which the type's symbols will be accessed.</param>
        /// <param name="typeChecker">Reference to the type checker.</param>
        /// <remarks>
        /// Ported from TypeScript version.
        /// </remarks>
        private static IEnumerable<ISymbol> AddTypeProperties(IType type, INode node, ITypeChecker typeChecker)
        {
            var typeSymbols = typeChecker.GetPropertiesOfType(type);
            if (typeSymbols != null)
            {
                return typeSymbols.Where(typeSymbol => typeSymbol.Name != null && typeChecker.IsValidPropertyAccess(node.Parent /*HINT: IPropertyAccessExpression*/, typeSymbol.Name));
            }

            return Enumerable.Empty<ISymbol>();
        }

        /// <summary>
        /// Creates an array of symbols for a node whose parent is either a qualified name or a property access expression.
        /// </summary>
        /// <remarks>
        /// This was ported from the TypeScript version of the language server.
        /// </remarks>
        public static IEnumerable<ISymbol> GetTypeScriptMemberSymbols(INode node, ITypeChecker typeChecker)
        {
            var symbols = new HashSet<ISymbol>();

            // If we are part of a type (say creating an interface and and assigning a type to a property)
            // interface Test {
            //    myField: ImportedModuleQualifiedName.<Type>
            // };
            // when we want to filter the symbols ot the types
            bool isTypeLocation = node.Parent != null && IsPartOfTypeNode(node.Parent);

            // This case handles when you are accessing a property out of
            // an import statement.
            // const foo = importFrom("").<Type or Value>.
            // NOTE: We don't really hit this case in our completion implementation
            // as it is already handled as part of the property access expression completion code.
            bool isRhsOfImportDeclaration = IsInRightSideOfImport(node);

            if (IsEntityNode(node))
            {
                var symbol = typeChecker.GetSymbolAtLocation(node);

                if (symbol != null)
                {
                    symbol = SkipAlias(symbol, typeChecker);

                    if ((symbol.Flags & (SymbolFlags.Enum | SymbolFlags.Module)) != SymbolFlags.None)
                    {
                        var exportedSymbols = typeChecker.GetExportsOfModule(symbol);
                        foreach (var exportedSymbol in exportedSymbols)
                        {
                            if (isRhsOfImportDeclaration)
                            {
                                if (typeChecker.IsValidPropertyAccess(node.Parent, exportedSymbol.Value.Name) ||
                                    SymbolCanBeReferencedAtTypeLocation(exportedSymbol.Value, typeChecker))
                                {
                                    symbols.Add(exportedSymbol.Value);
                                }
                            }
                            else if (isTypeLocation)
                            {
                                if (SymbolCanBeReferencedAtTypeLocation(exportedSymbol.Value, typeChecker))
                                {
                                    symbols.Add(exportedSymbol.Value);
                                }
                            }
                            else
                            {
                                if (typeChecker.IsValidPropertyAccess(node.Parent, exportedSymbol.Value.Name))
                                {
                                    symbols.Add(exportedSymbol.Value);
                                }
                            }
                        }

                        // If the module is merged with a value, we must get the type of the class and add its propertes (for inherited static methods).
                        if (!isTypeLocation &&
                            symbol.Declarations != null &&
                            symbol.Declarations.Any(d => d.Kind != SyntaxKind.SourceFile && d.Kind != SyntaxKind.ModuleDeclaration && d.Kind != SyntaxKind.EnumDeclaration))
                        {
                            symbols.AddRange(AddTypeProperties(typeChecker.GetTypeOfSymbolAtLocation(symbol, node), node, typeChecker));
                        }
                    }
                }
            }

            // If we are not in a type location, and not handling module exports, then add the symbols for the 
            // type of the "left side" (i.e. the "QualifiedName" in  "QualifiedName."Access") that can be accessed
            // through QualifiedName (i.e. if the user types QualifiedName.<Completion happens here> what symbols
            // have valid property access from there).
            if (!isTypeLocation)
            {
                symbols.AddRange(AddTypeProperties(typeChecker.GetTypeAtLocation(node), node, typeChecker));
            }

            return symbols;
        }

        /// <summary>
        /// Handles the case where auto-complete is from a property access.
        /// </summary>
        /// <remarks>
        /// This occurs when typing a property access of
        /// let someValue = object.
        /// A property access expression (<see cref="IPropertyAccessExpression"/>) is composed of an "left hand expression"
        /// (represented in the <see cref="IPropertyAccessExpression.Expression"/> field, the "dot" has its
        /// own field <see cref="IPropertyAccessExpression.DotToken"/>, and the remainder is store in the <see cref="IPropertyAccessExpression.Name"/>
        /// field.
        ///
        /// The type checker needs the node that proeprty is being accessed on, the expression field.
        /// </remarks>
        internal static IEnumerable<ISymbol> CreateSymbolsFromPropertyAccessExpression(CompletionState completionState, INode completionNode)
        {
            // A property access expression contains a
            // "Left hand expression", the "Dot Token" and the "Name"
            // We care about the properties
            var propertyAccessExpression = completionNode.Cast<IPropertyAccessExpression>();
            if (propertyAccessExpression.Expression != null)
            {
                return GetTypeScriptMemberSymbols(propertyAccessExpression.Expression, completionState.TypeChecker);
            }

            return null;
        }
    }
}
