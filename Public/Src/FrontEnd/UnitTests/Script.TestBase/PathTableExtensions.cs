// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Core;

#pragma warning disable 1591

namespace Test.DScript.Ast.Utilities
{
    public static class PathTableExtensions
    {
        public static IEnumerable<string> ToStrings(this IEnumerable<AbsolutePath> paths, PathTable pathTable)
        {
            // Even that the return type is IEnumerable
            // calling ToList to materialize the sequence to simplify debugging.
            return paths.Select(p => p.ToString(pathTable)).ToList();
        }
    }
}
