// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
