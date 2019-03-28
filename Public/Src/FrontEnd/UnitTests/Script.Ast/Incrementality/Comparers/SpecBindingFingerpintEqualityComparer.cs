// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using TypeScript.Net.Incrementality;

namespace Test.DScript.Ast.Incrementality
{
    public sealed class SpecBindingFingerpintEqualityComparer : IEqualityComparer<SpecBindingSymbols>
    {
        public static SpecBindingFingerpintEqualityComparer Instance { get; } = new SpecBindingFingerpintEqualityComparer();

        public bool Equals(SpecBindingSymbols x, SpecBindingSymbols y)
        {
            if (x.Symbols.Count != y.Symbols.Count)
            {
                return false;
            }

            var symbolsX = x.Symbols.ToList();
            var symbolsY = y.Symbols.ToList();

            for (int i = 0; i < symbolsX.Count; i++)
            {
                if (symbolsX[i] != symbolsY[i])
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(SpecBindingSymbols obj)
        {
            return 0;
        }
    }
}
