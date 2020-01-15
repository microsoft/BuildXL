// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using TypeScript.Net.Incrementality;

namespace Test.DScript.Ast.Incrementality
{
    public sealed class InteractionSymbolEqualityComparer : IEqualityComparer<InteractionSymbol>
    {
        public static InteractionSymbolEqualityComparer Instance { get; } = new InteractionSymbolEqualityComparer();

        public bool Equals(InteractionSymbol x, InteractionSymbol y)
        {
            return x == y;
        }

        public int GetHashCode(InteractionSymbol obj)
        {
            return 0;
        }
    }
}
