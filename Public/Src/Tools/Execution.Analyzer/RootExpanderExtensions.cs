// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using BuildXL.Utilities.Core;

namespace BuildXL.Execution.Analyzer
{
    internal static class RootExpanderExtensions
    {
        public static string ToString(this AbsolutePath path, PathTable pathTable, RootExpander expander)
        {
            return pathTable.ExpandName(path.Value, expander);
        }
    }
}
