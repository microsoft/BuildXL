// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;

namespace Test.DScript.Ast.Incrementality
{
    public sealed class RoarerBitSetEqualityComparer : IEqualityComparer<RoaringBitSet>
    {
        private readonly PathTable m_pathTable;

        public RoarerBitSetEqualityComparer(PathTable pathTable)
        {
            m_pathTable = pathTable;
        }

        public bool Equals(RoaringBitSet x, RoaringBitSet y)
        {
            return 
                Enumerable.SequenceEqual(x.MaterializeSet(m_pathTable), y.MaterializeSet(m_pathTable));
        }

        public int GetHashCode(RoaringBitSet obj)
        {
            return 0;
        }
    }
}
