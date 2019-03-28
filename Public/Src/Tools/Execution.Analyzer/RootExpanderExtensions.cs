// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;

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
