// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using Microsoft.CodeAnalysis;

namespace BuildXL.LogGenerator
{
    /// <summary>
    /// Extension methods for symbol analysis.
    /// </summary>
    internal static class SymbolAnalysisHelper
    {
        /// <nodoc/>
        public static bool HasAttribute(this ISymbol? symbol, INamedTypeSymbol attributeSymbol)
        {
            return symbol.TryGetAttribute(attributeSymbol) != null;
        }

        /// <nodoc/>
        public static AttributeData? TryGetAttribute(this ISymbol? symbol, INamedTypeSymbol attributeSymbol)
        {
            if (symbol == null)
            {
                return null;
            }

            var result = symbol.GetAttributes().FirstOrDefault(ad => ad.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default) == true);
            return result;
        }
    }
}
