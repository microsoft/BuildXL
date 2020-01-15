// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities;
using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.DScript.Ast.Incrementality
{
    internal static class RoaringSetExtensions
    {
        /// <nodoc />
        public static HashSet<int> MaterializeSet(this RoaringBitSet bitSet, PathTable pathTable)
        {
            return bitSet.MaterializeSetIfNeeded(string.Empty, (s, i) =>
                AbsolutePath.Create(pathTable, A("C") + i.ToString()));
        }
    }
}
