// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Core.Incrementality;

namespace Test.DScript.Ast.Incrementality
{
    public sealed class SpecInteractionStateEqualityComparer : IEqualityComparer<SpecBindingState>
    {
        private readonly PathTable m_pathTable;

        public SpecInteractionStateEqualityComparer(PathTable pathTable)
        {
            m_pathTable = pathTable;
        }

        public bool Equals(SpecBindingState x, SpecBindingState y)
        {
            return x.GetAbsolutePath(m_pathTable) == y.GetAbsolutePath(m_pathTable) &&
                   x.ReferencedSymbolsFingerprint == y.ReferencedSymbolsFingerprint &&
                   Enumerable.SequenceEqual(x.FileDependencies.MaterializedSet, y.FileDependencies.MaterializedSet) &&
                   Enumerable.SequenceEqual(x.FileDependents.MaterializedSet, y.FileDependents.MaterializedSet);
        }

        public int GetHashCode(SpecBindingState obj)
        {
            return 0;
        }
    }
}
