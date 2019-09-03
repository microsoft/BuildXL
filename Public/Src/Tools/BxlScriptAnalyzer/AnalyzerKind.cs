// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Analyzer
{
    /// <summary>
    /// Available DScript analyzers
    /// </summary>
    public enum AnalyzerKind
    {
        /// <nodoc />
        PrettyPrint,

        /// <nodoc />
        LegacyLiteralCreation,

        /// <nodoc />
        PathFixer,

        /// <nodoc />
        Documentation,

        /// <nodoc />
        Codex,

        /// <nodoc />
        GraphFragment,
    }
}
