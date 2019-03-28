// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using TypeScript.Net.Types;

namespace BuildXL.Ide.LanguageServer.Completion
{
    internal static class ObjectLiteralExpressions
    {
        /// <summary>
        /// Gets any properties that haven't yet been filled in for an object literal
        /// </summary>
        public static IEnumerable<ISymbol> GetRemainingPropertiesForObjectLiteral(IObjectLiteralExpression objectLiteralExpression, IEnumerable<IType> types, ITypeChecker typeChecker)
        {
            if (objectLiteralExpression?.Properties?.Elements == null)
            {
                return Enumerable.Empty<ISymbol>();
            }

            var propertiesThatAlreadyAreSet = new HashSet<string>(
                objectLiteralExpression.Properties.Elements
                    .Select(property => property.As<IPropertyAssignment>()?.Name?.Text)
                    .Where(name => name != null));

            var allPossibleSymbols = AutoCompleteHelpers.GetSymbolsFromTypes(typeChecker, types);
            Contract.Assert(allPossibleSymbols != null);

            return allPossibleSymbols.Where(symbol => !propertiesThatAlreadyAreSet.Contains(symbol.Name));
        }

        /// <summary>
        /// Handles the case where auto-complete is invoked directly in an
        /// object literal expression with no type information.
        /// I.e. calling a function with an object literal as an expression.
        /// Func(
        ///    {
        ///      ...Typing here
        ///    }
        /// );
        /// </summary>
        public static IEnumerable<ISymbol> CreateSymbolsFromObjectLiteralExpression(CompletionState completionState, INode completionNode)
        {
            var objectLiteralExpression = completionNode.Cast<IObjectLiteralExpression>();

            var contextualType = completionState.TypeChecker.GetContextualType(objectLiteralExpression);

            if (contextualType != null)
            {
                var objectLiteralTypes = AutoCompleteHelpers.ExpandUnionAndBaseInterfaceTypes(contextualType);

                return GetRemainingPropertiesForObjectLiteral(objectLiteralExpression, objectLiteralTypes, completionState.TypeChecker);
            }

            return null;
        }
    }
}
